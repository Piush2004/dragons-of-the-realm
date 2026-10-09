import * as THREE from 'three';
import type { DriveInput } from '../core/InputManager';
import type { CombatManager } from './CombatManager';
import type { Combatant } from './Combatant';
import type { Lair } from '../world/WorldMap';

function wrapAngle(a: number): number {
  return Math.atan2(Math.sin(a), Math.cos(a));
}

type Mood = 'melee' | 'ranged';

const aim = { yaw: 0, pitch: 0 };
const toTarget = new THREE.Vector3();

/**
 * Flies an enemy dragon: closes in, circles, lands when its target is on the ground, and chooses between
 * bites, flame and wing buffets. Skill scales how often it attacks and how patiently it lines shots up.
 */
export class DragonAI {
  private mood: Mood = 'ranged';
  private moodTimer = 0;
  private strafe = Math.random() < 0.5 ? 1 : -1;
  private strafeTimer = 3 + Math.random() * 4;
  private altitudeJitter = (Math.random() - 0.5) * 20;

  /** Awake and hunting. Idle dragons circle their lair until the player comes close or hurts them. */
  private aggro = false;
  private patrolAngle = Math.random() * Math.PI * 2;

  constructor(private readonly self: Combatant, private readonly combat: CombatManager, private readonly lair: Lair) {}

  update(dt: number, target: Combatant | null): DriveInput {
    const c = this.self;
    const out: DriveInput = { throttle: 0, brake: 0, steer: 0, handbrake: false, descend: false, action: false };
    if (!c.alive) return out;

    const S = c.scale;
    this.moodTimer -= dt;
    this.strafeTimer -= dt;
    if (this.moodTimer <= 0) {
      this.mood = Math.random() < 0.5 + (c.profile.skill - 0.7) ? 'melee' : 'ranged';
      this.moodTimer = 6 + Math.random() * 6;
      this.altitudeJitter = (Math.random() - 0.5) * 24;
    }
    if (this.strafeTimer <= 0) {
      this.strafe *= -1;
      this.strafeTimer = 4 + Math.random() * 5;
    }

    const controller = c.controller;
    if (target?.alive) {
      const range = Math.hypot(target.position.x - c.position.x, target.position.z - c.position.z);
      if (range < 650 || this.self.lastHitTime > -50 && range < 1500) this.aggro = true;
      else if (range > 1600) this.aggro = false;
    }
    if (!target || !target.alive || !this.aggro) {
      // At rest: circle the lair at height, keeping an eye out.
      this.patrolAngle += (c.controller.currentSpeed / Math.max(60, this.lair.patrol)) * dt * 0.9;
      const aheadX = this.lair.x + Math.cos(this.patrolAngle + 0.5) * this.lair.patrol;
      const aheadZ = this.lair.z + Math.sin(this.patrolAngle + 0.5) * this.lair.patrol;
      const diff = wrapAngle(Math.atan2(aheadX - c.position.x, aheadZ - c.position.z) - controller.heading);
      out.steer = THREE.MathUtils.clamp(-diff * 1.6, -1, 1);
      out.throttle = 0.45;
      this.altitude(out, c, this.lair.altitude);
      c.model.setLook(0, 0);
      return out;
    }

    toTarget.copy(target.position).sub(c.position);
    const distance = toTarget.length();
    const flatDistance = Math.hypot(toTarget.x, toTarget.z);
    const targetGrounded = target.controller.isGrounded();
    const fighting = distance < 170 * S;

    // Where to point the body.
    // Lead the target: aim where it will be, so a banking opponent is intercepted instead of chased round a circle.
    const targetVelocity = target.controller.body.velocity;
    const lead = THREE.MathUtils.clamp(flatDistance / Math.max(25, c.controller.currentSpeed + 10), 0, 3.0);
    let desiredYaw = Math.atan2(toTarget.x + targetVelocity.x * lead, toTarget.z + targetVelocity.z * lead);
    const circleRadius = (this.mood === 'ranged' ? 55 : 16) * S + target.radius;
    if (!targetGrounded && flatDistance < circleRadius * 1.6 && this.mood === 'ranged') {
      // Circle at fire range, keeping the target in the nose's arc.
      desiredYaw += this.strafe * THREE.MathUtils.clamp(1.15 * (1 - flatDistance / (circleRadius * 1.6)) + 0.35, 0.2, 1.2);
    }
    // Too close and not about to strike: peel away for another pass instead of hanging over the target.
    const peelDistance = target.radius + 9 * S;
    const peeling = distance < peelDistance && !c.model.isAttacking() && (c.biteCooldown > 0.4 || this.mood === 'ranged');
    if (peeling) desiredYaw += this.strafe * 1.7;
    const diff = wrapAngle(desiredYaw - controller.heading);
    out.steer = THREE.MathUtils.clamp(-diff * 3.5, -1, 1);

    // Speed: flat out to close from range, then bleed speed as the gap shrinks. A slower dragon turns on a tighter
    // circle, so it cuts inside a target that is banking round it instead of chasing its tail.
    const attacking = c.model.isAttacking();
    const topSpeed = c.controller.tuning.airTopSpeed;
    const fightZone = 170 * S;
    const wanted = c.controller.isGrounded()
      ? c.controller.tuning.groundTopSpeed
      : flatDistance > fightZone
        ? topSpeed
        : THREE.MathUtils.clamp(topSpeed * (flatDistance / fightZone), topSpeed * 0.38, topSpeed);
    const speed = c.controller.currentSpeed;
    if (peeling) out.throttle = 1;
    else if (attacking && c.breathing) out.brake = 0.6;
    else if (speed < wanted - 1.5) out.throttle = 1;
    else if (speed > wanted + 2.5) out.brake = 0.8;
    else out.throttle = 0;

    // Height: land beside a grounded target, otherwise fly at its level.
    if (targetGrounded && fighting) this.land(out, c);
    else this.altitude(out, c, Math.max(25, target.controller.getAltitude() + c.controller.tuning.groundOffset * 0 + 12 + this.altitudeJitter));

    // Choose an attack.
    const facing = Math.abs(diff);
    const skill = c.profile.skill;
    const vertical = Math.abs(toTarget.y);
    if (!attacking) {
      const biteReach = target.radius + 14 * S + c.radius * 0.3;
      if (distance < biteReach && facing < 0.9 && Math.random() < dt * 5 * skill) {
        this.combat.tryAttack(c, 'bite');
      } else if (distance > 18 * S && distance < 90 * S && facing < 0.42 && vertical < distance * 0.7 && Math.random() < dt * 2.2 * skill) {
        this.combat.tryAttack(c, 'breath');
      } else if (distance < 40 * S && Math.random() < dt * 0.35 * skill) {
        this.combat.tryAttack(c, 'buffet');
      }
    }

    // Head: track the target during an attack, relax otherwise.
    if (c.model.isAttacking()) {
      this.combat.aimAt(c, target, aim);
      c.model.setLook(aim.yaw, aim.pitch);
    } else {
      c.model.setLook(0, 0);
    }
    return out;
  }

  private altitude(out: DriveInput, c: Combatant, desired: number): void {
    const altitude = c.controller.getAltitude();
    if (altitude < desired - 6) out.handbrake = true;
    else if (altitude > desired + 10) out.descend = true;
  }

  private land(out: DriveInput, c: Combatant): void {
    if (c.controller.getAltitude() > 1.5) out.descend = true;
  }
}
