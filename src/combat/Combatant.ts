import * as THREE from 'three';
import type { DragonInstance, DragonProfile } from '../dragons/DragonCatalog';
import type { DragonModel } from '../dragons/DragonModel';
import type { FlyingMountController } from '../mounts/FlyingMountController';

export type Team = 'player' | 'enemy';

/** One dragon in a fight: its body, its rules and its current condition. */
export class Combatant {
  readonly team: Team;
  readonly controller: FlyingMountController;
  maxHealth: number;
  health: number;
  alive = true;
  biteCooldown = 0;
  fireCooldown = 0;
  buffetCooldown = 0;
  /** Seconds of burning left after being caught in flame. */
  burn = 0;
  /** Time since death, for removal. */
  deadTime = 0;
  /** Breathing flame right now. */
  breathing = false;
  lastHitTime = -99;

  profile: DragonProfile;
  model: DragonModel;
  scale: number;
  name: string;

  constructor(team: Team, controller: FlyingMountController, instance: DragonInstance) {
    this.team = team;
    this.controller = controller;
    this.profile = instance.profile;
    this.model = instance.model;
    this.scale = instance.scale;
    this.name = instance.profile.name;
    this.maxHealth = instance.profile.maxHealth;
    this.health = this.maxHealth;
  }

  /** Switches to another dragon, restoring full health. */
  become(instance: DragonInstance): void {
    this.profile = instance.profile;
    this.model = instance.model;
    this.scale = instance.scale;
    this.name = instance.profile.name;
    this.maxHealth = instance.profile.maxHealth;
    this.health = this.maxHealth;
    this.alive = true;
    this.burn = 0;
    this.deadTime = 0;
    this.breathing = false;
    this.biteCooldown = this.fireCooldown = this.buffetCooldown = 0;
    this.controller.dead = false;
  }

  /** Back on its feet with full health, same dragon. */
  revive(): void {
    this.health = this.maxHealth;
    this.alive = true;
    this.burn = 0;
    this.deadTime = 0;
    this.breathing = false;
    this.biteCooldown = this.fireCooldown = this.buffetCooldown = 0;
    this.controller.dead = false;
    this.model.setDead(false);
  }

  get position(): THREE.Vector3 {
    return this.controller.mesh.position;
  }

  get radius(): number {
    return this.controller.bodyRadius * 1.2;
  }

  get healthFraction(): number {
    return Math.max(0, this.health / this.maxHealth);
  }
}
