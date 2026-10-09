import * as CANNON from 'cannon-es';

const FIXED_TIME_STEP = 1 / 60;
const MAX_SUB_STEPS = 5;

export class PhysicsWorld {
  readonly world: CANNON.World;
  readonly groundMaterial: CANNON.Material;
  readonly wheelMaterial: CANNON.Material;

  constructor() {
    this.world = new CANNON.World({ gravity: new CANNON.Vec3(0, -9.82, 0) });
    // Body count stays small because ObstacleField streams props in around the cars.
    // (Never add a CANNON.Plane here: wheel raycasts against it miss unpredictably.)
    this.world.broadphase = new CANNON.NaiveBroadphase();
    this.world.defaultContactMaterial.friction = 0.25;
    this.world.defaultContactMaterial.restitution = 0.15;
    (this.world.solver as CANNON.GSSolver).iterations = 10;
    this.world.allowSleep = false;

    this.groundMaterial = new CANNON.Material('ground');
    this.wheelMaterial = new CANNON.Material('wheel');

    const wheelGroundContact = new CANNON.ContactMaterial(this.groundMaterial, this.wheelMaterial, {
      friction: 0.3,
      restitution: 0,
      contactEquationStiffness: 1000,
    });
    this.world.addContactMaterial(wheelGroundContact);
  }

  step(deltaSeconds: number): void {
    this.world.step(FIXED_TIME_STEP, deltaSeconds, MAX_SUB_STEPS);
  }
}
