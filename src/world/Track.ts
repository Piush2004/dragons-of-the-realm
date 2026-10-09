import * as THREE from 'three';
import type { RoadTextures } from './ProceduralTextures';

export interface TrackCheckpoint {
  index: number;
  position: THREE.Vector3;
  forward: THREE.Vector3;
  radius: number;
}

const ROAD_LIFT = 0.06;
const DASH_REPEAT_METERS = 12;

/**
 * A closed loop road built from a Catmull-Rom spline. Produces the visual road
 * ribbon, centreline samples (used to flatten terrain under the road) and a
 * checkpoint ring used by race logic / AI navigation.
 */
export class Track {
  readonly curve: THREE.CatmullRomCurve3;
  readonly closed: boolean;
  readonly roadWidth: number;
  readonly length: number;
  readonly checkpoints: TrackCheckpoint[];
  readonly mesh: THREE.Mesh;

  constructor(
    controlPointsXZ: Array<[number, number]>,
    heightAt: (x: number, z: number) => number,
    textures: RoadTextures,
    roadWidth = 10,
    checkpointCount = 24,
    closed = true,
    /** 0..1 per point: how strongly to keep the raw ground height instead of the smoothed one (castle platforms). */
    rawWeight?: (x: number, z: number) => number,
  ) {
    // Lay the path on the ground: sample the plan-view spline densely, read the terrain height under each
    // sample and smooth it, so the road follows the land's broad shape instead of every bump.
    const plan = new THREE.CatmullRomCurve3(controlPointsXZ.map(([x, z]) => new THREE.Vector3(x, 0, z)), closed, 'catmullrom', 0.5);
    const sampleCount = Math.max(8, Math.ceil(plan.getLength() / 22));
    const raw = plan.getSpacedPoints(sampleCount);
    if (closed) raw.pop();
    const heights = raw.map((p) => heightAt(p.x, p.z));
    const window = 5;
    const smoothed = heights.map((_, i) => {
      let sum = 0;
      let count = 0;
      for (let k = -window; k <= window; k++) {
        let j = i + k;
        if (closed) j = (j + heights.length) % heights.length;
        else if (j < 0 || j >= heights.length) continue;
        sum += heights[j];
        count++;
      }
      return sum / count;
    });
    const points = raw.map((p, i) => {
      const keep = rawWeight ? rawWeight(p.x, p.z) : 0;
      return new THREE.Vector3(p.x, smoothed[i] + (heights[i] - smoothed[i]) * keep, p.z);
    });
    this.closed = closed;
    this.curve = new THREE.CatmullRomCurve3(points, closed, 'catmullrom', 0.5);
    this.roadWidth = roadWidth;
    this.length = this.curve.getLength();
    this.mesh = this.buildRoadMesh(textures);
    this.checkpoints = this.buildCheckpoints(checkpointCount);
  }

  /** Evenly spaced centreline points, roughly one per `spacing` meters. */
  getCenterlineSamples(spacing = 1.5): THREE.Vector3[] {
    return this.curve.getSpacedPoints(Math.ceil(this.length / spacing));
  }

  private buildRoadMesh(textures: RoadTextures): THREE.Mesh {
    const segments = Math.ceil(this.length / 2);
    const positions: number[] = [];
    const uvs: number[] = [];
    const normals: number[] = [];
    const indices: number[] = [];
    const up = new THREE.Vector3(0, 1, 0);

    for (let i = 0; i <= segments; i++) {
      const t = this.closed ? (i % segments) / segments : i / segments;
      const p = this.curve.getPointAt(t);
      const tangent = this.curve.getTangentAt(t);
      const right = new THREE.Vector3().crossVectors(tangent, up).normalize();
      const normal = new THREE.Vector3().crossVectors(right, tangent).normalize();

      const left = p.clone().addScaledVector(right, -this.roadWidth / 2);
      const rightEdge = p.clone().addScaledVector(right, this.roadWidth / 2);
      positions.push(left.x, p.y + ROAD_LIFT, left.z, rightEdge.x, p.y + ROAD_LIFT, rightEdge.z);
      normals.push(normal.x, normal.y, normal.z, normal.x, normal.y, normal.z);

      const v = ((i / segments) * this.length) / DASH_REPEAT_METERS;
      uvs.push(0, v, 1, v);

      if (i < segments) {
        const a = i * 2;
        indices.push(a, a + 1, a + 2, a + 1, a + 3, a + 2);
      }
    }

    const geometry = new THREE.BufferGeometry();
    geometry.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
    geometry.setAttribute('normal', new THREE.Float32BufferAttribute(normals, 3));
    geometry.setAttribute('uv', new THREE.Float32BufferAttribute(uvs, 2));
    geometry.setIndex(indices);

    const material = new THREE.MeshStandardMaterial({
      map: textures.map,
      normalMap: textures.normalMap,
      normalScale: new THREE.Vector2(0.35, 0.35),
      roughnessMap: textures.roughnessMap,
      roughness: 1,
      metalness: 0,
      polygonOffset: true,
      polygonOffsetFactor: -2,
      polygonOffsetUnits: -2,
    });
    const mesh = new THREE.Mesh(geometry, material);
    mesh.receiveShadow = true;
    return mesh;
  }

  private buildCheckpoints(count: number): TrackCheckpoint[] {
    const checkpoints: TrackCheckpoint[] = [];
    for (let i = 0; i < count; i++) {
      const t = i / count;
      checkpoints.push({
        index: i,
        position: this.curve.getPointAt(t),
        forward: this.curve.getTangentAt(t),
        radius: this.roadWidth * 0.8,
      });
    }
    return checkpoints;
  }

  getStartTransform(): { position: THREE.Vector3; yaw: number } {
    const position = this.curve.getPointAt(0).clone();
    const tangent = this.curve.getTangentAt(0);
    const yaw = Math.atan2(tangent.x, tangent.z);
    return { position, yaw };
  }
}
