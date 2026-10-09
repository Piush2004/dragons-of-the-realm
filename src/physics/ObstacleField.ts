import * as CANNON from 'cannon-es';

export interface CylinderObstacle {
  x: number;
  y: number;
  z: number;
  radius: number;
}

const CELL_SIZE = 40;
const OBSTACLE_HEIGHT = 5;

/**
 * Streams static cylinder colliders (tree trunks, posts) in and out of the physics world.
 * Only obstacles in the grid cells surrounding the tracked positions exist as bodies, so
 * the cost stays flat no matter how many thousands of props the map contains.
 */
export class ObstacleField {
  private readonly world: CANNON.World;
  private readonly cells = new Map<string, CylinderObstacle[]>();
  private readonly activeBodies = new Map<string, CANNON.Body[]>();
  private readonly shapes = new Map<number, CANNON.Cylinder>();

  constructor(world: CANNON.World, obstacles: CylinderObstacle[]) {
    this.world = world;
    for (const obstacle of obstacles) {
      const key = this.keyFor(obstacle.x, obstacle.z);
      const bucket = this.cells.get(key);
      if (bucket) bucket.push(obstacle);
      else this.cells.set(key, [obstacle]);
    }
  }

  private keyFor(x: number, z: number): string {
    return `${Math.floor(x / CELL_SIZE)},${Math.floor(z / CELL_SIZE)}`;
  }

  private shapeFor(radius: number): CANNON.Cylinder {
    // Quantize radii so a handful of shapes are shared by every obstacle.
    const key = Math.max(2, Math.round(radius * 10));
    let shape = this.shapes.get(key);
    if (!shape) {
      shape = new CANNON.Cylinder(key / 10, key / 10, OBSTACLE_HEIGHT, 8);
      this.shapes.set(key, shape);
    }
    return shape;
  }

  /** Activates colliders in the 3x3 cells around each position and retires the rest. */
  update(positions: Array<{ x: number; z: number }>): void {
    const wanted = new Set<string>();
    for (const position of positions) {
      const cx = Math.floor(position.x / CELL_SIZE);
      const cz = Math.floor(position.z / CELL_SIZE);
      for (let dx = -1; dx <= 1; dx++) {
        for (let dz = -1; dz <= 1; dz++) wanted.add(`${cx + dx},${cz + dz}`);
      }
    }

    for (const [key, bodies] of this.activeBodies) {
      if (wanted.has(key)) continue;
      for (const body of bodies) this.world.removeBody(body);
      this.activeBodies.delete(key);
    }

    for (const key of wanted) {
      if (this.activeBodies.has(key)) continue;
      const bodies: CANNON.Body[] = [];
      for (const obstacle of this.cells.get(key) ?? []) {
        const body = new CANNON.Body({ mass: 0, shape: this.shapeFor(obstacle.radius) });
        body.position.set(obstacle.x, obstacle.y + OBSTACLE_HEIGHT / 2 - 0.5, obstacle.z);
        this.world.addBody(body);
        bodies.push(body);
      }
      this.activeBodies.set(key, bodies);
    }
  }

  get activeBodyCount(): number {
    let count = 0;
    for (const bodies of this.activeBodies.values()) count += bodies.length;
    return count;
  }
}
