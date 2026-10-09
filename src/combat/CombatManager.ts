import * as THREE from 'three';
import type { DragonInstance } from '../dragons/DragonCatalog';
import type { AttackKind } from '../dragons/DragonModel';
import { Combatant } from './Combatant';
import { Particles } from './Particles';

export type CombatEventKind = 'info' | 'hit' | 'hurt' | 'kill' | 'death';

const FIRE_RANGE = 90;
const FIRE_SPREAD = 0.2;
const BURN_SECONDS = 3.5;

const scratchOrigin = new THREE.Vector3();
const scratchDir = new THREE.Vector3();
const scratchV = new THREE.Vector3();
const scratchForward = new THREE.Vector3();

function wrapAngle(a: number): number {
  return Math.atan2(Math.sin(a), Math.cos(a));
}

/** Applies attacks, flame, damage, knockback and death between dragons, and drives the visual effects. */
export class CombatManager {
  readonly combatants: Combatant[] = [];
  readonly particles = new Particles();
  /** Called for notable events so the HUD can show them. */
  onEvent: (message: string, kind: CombatEventKind, source?: Combatant) => void = () => {};
  /** Called when a combatant takes damage (for camera shake and hit markers). */
  onDamage: (victim: Combatant, amount: number, attacker: Combatant | null) => void = () => {};

  private time = 0;
  private readonly lastBurst = new WeakMap<Combatant, number>();

  add(combatant: Combatant): void {
    this.combatants.push(combatant);
    this.hook(combatant);
  }

  remove(combatant: Combatant): void {
    const index = this.combatants.indexOf(combatant);
    if (index >= 0) this.combatants.splice(index, 1);
    combatant.model.events = {};
    combatant.controller.dispose();
  }

  /** Switches a combatant to another dragon and reconnects the attack events. */
  setDragon(combatant: Combatant, instance: DragonInstance): void {
    combatant.model.events = {};
    combatant.controller.swapCreature(instance.model, instance.tuning);
    combatant.become(instance);
    this.hook(combatant);
  }

  private hook(c: Combatant): void {
    c.model.events = {
      onStrike: (kind) => this.strike(c, kind),
      onBreathStart: () => {
        c.breathing = true;
      },
      onBreathEnd: () => {
        c.breathing = false;
      },
    };
  }

  enemiesOf(c: Combatant): Combatant[] {
    return this.combatants.filter((o) => o.team !== c.team && o.alive);
  }

  /** The enemy best lined up with where `c` is facing, within range. */
  bestTarget(c: Combatant, maxDistance: number, minDot = 0.35): Combatant | null {
    const heading = c.controller.heading;
    scratchForward.set(Math.sin(heading), 0, Math.cos(heading));
    let best: Combatant | null = null;
    let bestScore = -Infinity;
    for (const other of this.enemiesOf(c)) {
      scratchV.copy(other.position).sub(c.position);
      const distance = scratchV.length();
      if (distance > maxDistance) continue;
      const flat = scratchV.clone().setY(0).normalize();
      const dot = flat.dot(scratchForward);
      if (dot < minDot) continue;
      const score = dot * 2 - distance / maxDistance;
      if (score > bestScore) {
        bestScore = score;
        best = other;
      }
    }
    return best;
  }

  /** Starts an attack if the dragon is ready; returns whether it started. */
  tryAttack(c: Combatant, kind: AttackKind): boolean {
    if (!c.alive || c.model.isAttacking()) return false;
    const cooldown = kind === 'bite' ? c.biteCooldown : kind === 'breath' ? c.fireCooldown : c.buffetCooldown;
    if (cooldown > 0) return false;
    if (!c.model.triggerAttack(kind)) return false;
    if (kind === 'bite') c.biteCooldown = c.profile.biteCooldown;
    if (kind === 'breath') c.fireCooldown = c.profile.fireCooldown;
    if (kind === 'buffet') c.buffetCooldown = c.profile.buffetCooldown;
    return true;
  }

  private strike(c: Combatant, kind: AttackKind): void {
    if (!c.alive) return;
    const forward = scratchForward.set(Math.sin(c.controller.heading), 0, Math.cos(c.controller.heading)).clone();
    if (kind === 'bite') {
      const mouth = c.model.mouthWorld(new THREE.Vector3());
      let hitAny = false;
      for (const target of this.enemiesOf(c)) {
        const reach = target.radius * 1.15 + 7 * c.scale;
        if (mouth.distanceTo(target.position) > reach) continue;
        const toTarget = target.position.clone().sub(c.position).setY(0).normalize();
        if (toTarget.dot(forward) < -0.1) continue;
        const damage = c.profile.biteDamage * (0.9 + Math.random() * 0.2);
        this.damage(target, damage, c, toTarget.multiplyScalar(18 * c.scale / Math.max(0.7, target.scale)), 4);
        this.particles.emitBurst(mouth, 0.9 * c.scale, 10);
        this.particles.emitDust(mouth, 8, 0.5 * c.scale);
        hitAny = true;
        break;
      }
      if (!hitAny) this.particles.emitDust(mouth, 3, 0.3 * c.scale);
    } else if (kind === 'buffet') {
      const base = c.position;
      for (const target of this.enemiesOf(c)) {
        const toTarget = target.position.clone().sub(base);
        const distance = toTarget.length();
        if (distance > 60 * c.scale + target.radius) continue;
        const flat = toTarget.clone().setY(0).normalize();
        if (flat.dot(forward) < 0.0) continue;
        this.damage(target, 45 * c.scale, c, flat.multiplyScalar(36 * c.scale / Math.max(0.7, target.scale)), 6);
      }
      const ground = base.clone().add(forward.clone().multiplyScalar(10 * c.scale));
      ground.y = c.controller.mesh.position.y - c.controller.tuning.groundOffset;
      this.particles.emitDust(ground, 26, 1.2 * c.scale, forward.clone().multiplyScalar(20));
    }
  }

  private damage(target: Combatant, amount: number, attacker: Combatant | null, push: THREE.Vector3, lift = 0): void {
    if (!target.alive) return;
    target.health -= amount;
    target.lastHitTime = this.time;
    target.controller.addKnockback(push.x, lift, push.z);
    const side = attacker ? Math.sign(scratchV.copy(attacker.position).sub(target.position).cross(new THREE.Vector3(0, 1, 0)).dot(scratchForward.set(Math.sin(target.controller.heading), 0, Math.cos(target.controller.heading)))) : 1;
    target.model.hit(Math.min(1.2, amount / 150), side || 1);
    this.onDamage(target, amount, attacker);
    if (target.health <= 0) {
      target.health = 0;
      target.alive = false;
      target.breathing = false;
      target.controller.dead = true;
      target.model.setDead(true);
      this.onEvent(target.team === 'player' ? `${target.name} has fallen` : `${target.name} is slain`, target.team === 'player' ? 'death' : 'kill', target);
    }
  }

  update(dt: number): void {
    this.time += dt;
    const particles = this.particles;

    for (const c of this.combatants) {
      c.biteCooldown = Math.max(0, c.biteCooldown - dt);
      c.fireCooldown = Math.max(0, c.fireCooldown - dt);
      c.buffetCooldown = Math.max(0, c.buffetCooldown - dt);
      if (!c.alive) {
        c.deadTime += dt;
        continue;
      }

      // Burning: slow damage and flames licking off the body.
      if (c.burn > 0) {
        c.burn -= dt;
        c.health -= 14 * c.scale * dt;
        if (Math.random() < dt * 18) {
          scratchV.set((Math.random() - 0.5) * c.radius, (Math.random() - 0.2) * c.radius, (Math.random() - 0.5) * c.radius).add(c.position);
          particles.emitBurst(scratchV, 0.35 * c.scale, 2);
        }
        if (c.health <= 0) {
          c.health = 0;
          c.alive = false;
          c.controller.dead = true;
          c.model.setDead(true);
          this.onEvent(c.team === 'player' ? `${c.name} burned to death` : `${c.name} is slain`, c.team === 'player' ? 'death' : 'kill', c);
        }
      }

      // Dust from heavy feet and from wings beating near the ground.
      const ground = c.position.clone();
      ground.y -= c.controller.tuning.groundOffset;
      const altitude = c.controller.getAltitude();
      if (c.controller.isGrounded() && c.controller.currentSpeed > 4 && Math.random() < dt * 14) {
        const feet = ground.clone();
        particles.emitDust(feet, 2, 0.5 * c.scale, new THREE.Vector3(Math.sin(c.controller.heading), 0, Math.cos(c.controller.heading)).multiplyScalar(-c.controller.currentSpeed));
      } else if (!c.controller.isGrounded() && altitude < 28 * c.scale && Math.random() < dt * 9) {
        ground.y = c.position.y - altitude - c.controller.tuning.groundOffset;
        particles.emitDust(ground, 3, 0.9 * c.scale);
      }

      // Throat glow and flame.
      if (c.model.breathGlow > 0.05 && !c.breathing) {
        c.model.mouthWorld(scratchOrigin);
        particles.emitCharge(scratchOrigin, dt, c.scale, c.model.breathGlow);
      }
      if (c.breathing) this.updateFlame(c, dt);
    }

    this.separate(dt);
    particles.update(dt);
  }

  private updateFlame(c: Combatant, dt: number): void {
    const origin = c.model.mouthWorld(scratchOrigin);
    const dir = c.model.mouthDirection(scratchDir);
    this.particles.emitBreath(origin, dir, dt, c.scale, 1);

    const range = FIRE_RANGE * c.scale;
    for (const target of this.enemiesOf(c)) {
      scratchV.copy(target.position).sub(origin);
      const along = scratchV.dot(dir);
      if (along < 0 || along > range + target.radius) continue;
      const lateral = Math.sqrt(Math.max(0, scratchV.lengthSq() - along * along));
      if (lateral > target.radius * 1.15 + along * FIRE_SPREAD) continue;

      target.burn = BURN_SECONDS;
      const damage = c.profile.fireDps * dt * (1 - Math.min(0.5, (along / range) * 0.5));
      target.health -= damage;
      target.lastHitTime = this.time;
      this.onDamage(target, damage, c);
      target.controller.addKnockback(dir.x * 7 * dt * c.scale, 0, dir.z * 7 * dt * c.scale);
      target.model.hit(0.25 * dt * 60 * 0.08, 1);

      // Flames splash across whatever they strike.
      const last = this.lastBurst.get(target) ?? -1;
      if (this.time - last > 0.09) {
        this.lastBurst.set(target, this.time);
        const impact = origin.clone().addScaledVector(dir, Math.max(2, along - target.radius * 0.7));
        this.particles.emitBurst(impact, 1.1 * target.scale, 7);
      }
      if (target.health <= 0 && target.alive) {
        target.health = 0;
        target.alive = false;
        target.breathing = false;
        target.controller.dead = true;
        target.model.setDead(true);
        this.onEvent(target.team === 'player' ? `${target.name} was burned down` : `${target.name} is slain`, target.team === 'player' ? 'death' : 'kill', target);
      }
    }
  }

  /** Stops dragons passing through each other. */
  private separate(dt: number): void {
    const list = this.combatants.filter((c) => c.alive);
    for (let i = 0; i < list.length; i++) {
      for (let j = i + 1; j < list.length; j++) {
        const a = list[i];
        const b = list[j];
        scratchV.copy(b.position).sub(a.position);
        const distance = scratchV.length();
        const minimum = (a.radius + b.radius) * 0.9;
        if (distance >= minimum || distance < 1e-3) continue;
        scratchV.normalize();
        const push = (minimum - distance) * 4.0;
        const massA = a.scale ** 3;
        const massB = b.scale ** 3;
        const shareA = massB / (massA + massB);
        a.controller.addKnockback(-scratchV.x * push * shareA * dt * 8, 0, -scratchV.z * push * shareA * dt * 8);
        b.controller.addKnockback(scratchV.x * push * (1 - shareA) * dt * 8, 0, scratchV.z * push * (1 - shareA) * dt * 8);
      }
    }
  }

  /** Where a dragon should aim its head to hit `target`: relative yaw and pitch. */
  aimAt(c: Combatant, target: Combatant, out: { yaw: number; pitch: number }): void {
    const mouth = c.model.headWorld(scratchOrigin);
    scratchV.copy(target.position).sub(mouth);
    const flat = Math.hypot(scratchV.x, scratchV.z);
    out.yaw = wrapAngle(Math.atan2(scratchV.x, scratchV.z) - c.controller.heading);
    out.pitch = -Math.atan2(scratchV.y, Math.max(1, flat)) * 0.9;
  }
}
