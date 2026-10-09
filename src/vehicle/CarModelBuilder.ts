import * as THREE from 'three';
import { getWheelLayout, type VehicleConfig } from './VehicleConfig';

/** Height of the car floor relative to the chassis body centre (keeps a realistic ride height). */
const FLOOR_Y = -0.4;

interface CabinLayout {
  /** Base of the windscreen / rear window, as a fraction of half the car length (+ = towards the nose). */
  front: number;
  rear: number;
  /** Horizontal run of the raked windscreen / rear window in meters. */
  windscreenRun: number;
  rearWindowRun: number;
  roofHeight: number;
}

const CABIN_LAYOUTS: Record<VehicleConfig['body']['style'], CabinLayout> = {
  coupe: { front: 0.3, rear: -0.62, windscreenRun: 1.05, rearWindowRun: 0.9, roofHeight: 0.98 },
  sedan: { front: 0.36, rear: -0.6, windscreenRun: 0.8, rearWindowRun: 0.72, roofHeight: 1.1 },
  hatch: { front: 0.38, rear: -0.86, windscreenRun: 0.75, rearWindowRun: 0.4, roofHeight: 1.14 },
};

function extrudeShape(shape: THREE.Shape, width: number, bevel: number): THREE.BufferGeometry {
  const depth = width - bevel * 2;
  const geometry = new THREE.ExtrudeGeometry(shape, {
    depth,
    bevelEnabled: true,
    bevelThickness: bevel,
    bevelSize: bevel,
    // Keep the outer silhouette at the authored size instead of growing it by the bevel.
    bevelOffset: -bevel,
    bevelSegments: 5,
    curveSegments: 14,
  });
  // Shape X runs along the car's length; rotate so it maps to +Z (forward) and the extrusion to X (width).
  geometry.translate(0, 0, -depth / 2);
  geometry.rotateY(-Math.PI / 2);
  return geometry;
}

function box(
  group: THREE.Group,
  size: [number, number, number],
  position: [number, number, number],
  material: THREE.Material,
  castShadow = false,
): THREE.Mesh {
  const mesh = new THREE.Mesh(new THREE.BoxGeometry(...size), material);
  mesh.position.set(...position);
  mesh.castShadow = castShadow;
  group.add(mesh);
  return mesh;
}

/** Builds an original car body from the vehicle's dimensions and body options: shell, glasshouse, lights, aero. */
export function buildCarBody(config: VehicleConfig): THREE.Group {
  const group = new THREE.Group();
  const { body } = config;
  const width = config.chassisSize.x;
  const half = config.chassisSize.z / 2;
  const cabinLayout = CABIN_LAYOUTS[body.style];
  const beltY = FLOOR_Y + 0.66;
  const roofY = FLOOR_Y + cabinLayout.roofHeight;
  const satin = body.finish === 'satin';

  const paint = new THREE.MeshPhysicalMaterial({
    color: config.color,
    metalness: satin ? 0.5 : 0.25,
    roughness: satin ? 0.48 : 0.36,
    clearcoat: satin ? 0.25 : 1,
    clearcoatRoughness: satin ? 0.45 : 0.05,
    envMapIntensity: 0.85,
  });
  const glass = new THREE.MeshPhysicalMaterial({
    color: 0x07090c,
    metalness: 0.3,
    roughness: 0.05,
    clearcoat: 1,
    clearcoatRoughness: 0.02,
  });
  const gloss = new THREE.MeshPhysicalMaterial({
    color: body.roofColor ?? config.color,
    metalness: 0.4,
    roughness: 0.3,
    clearcoat: 1,
    clearcoatRoughness: 0.08,
  });
  const trim = new THREE.MeshStandardMaterial({ color: 0x0b0b0c, roughness: 0.55, metalness: 0.3 });

  // --- Lower shell: side silhouette rear-to-front over the top, back along the floor with wheel arches. ---
  const layout = getWheelLayout(config);
  const archRadius = config.wheelRadius + 0.075;
  const wheelCenterY = -(config.suspensionRestLength - 0.07);
  const archHalfChord = Math.sqrt(Math.max(0.01, archRadius ** 2 - (FLOOR_Y - wheelCenterY) ** 2));
  const archStart = Math.atan2(FLOOR_Y - wheelCenterY, archHalfChord);
  const deckY = body.style === 'coupe' ? beltY + 0.02 : beltY + 0.05;

  const silhouette = new THREE.Shape();
  silhouette.moveTo(-half + 0.22, FLOOR_Y);
  silhouette.lineTo(-half, FLOOR_Y + 0.22);
  silhouette.lineTo(-half + 0.03, deckY - 0.1);
  silhouette.quadraticCurveTo(-half + 0.06, deckY, -half + 0.3, deckY + 0.01);
  silhouette.lineTo(half * cabinLayout.rear, beltY + 0.03);
  silhouette.lineTo(half * cabinLayout.front, beltY);
  silhouette.quadraticCurveTo(half - 0.6, beltY - 0.07, half - 0.1, FLOOR_Y + 0.46);
  silhouette.quadraticCurveTo(half + 0.03, FLOOR_Y + 0.34, half - 0.02, FLOOR_Y + 0.14);
  silhouette.lineTo(half - 0.25, FLOOR_Y);
  for (const wheelZ of [layout.frontZ, layout.rearZ]) {
    silhouette.lineTo(wheelZ + archHalfChord, FLOOR_Y);
    silhouette.absarc(wheelZ, wheelCenterY, archRadius, archStart, Math.PI - archStart, false);
  }
  silhouette.closePath();
  const shell = new THREE.Mesh(extrudeShape(silhouette, width, 0.1), paint);
  shell.castShadow = true;
  shell.receiveShadow = true;
  group.add(shell);

  // --- Glasshouse ---
  const cabinFront = half * cabinLayout.front;
  const cabinRear = half * cabinLayout.rear;
  const roofFront = cabinFront - cabinLayout.windscreenRun;
  const roofRear = cabinRear + cabinLayout.rearWindowRun;
  const cabinShape = new THREE.Shape();
  cabinShape.moveTo(cabinRear, beltY - 0.02);
  cabinShape.lineTo(roofRear, roofY - 0.03);
  cabinShape.quadraticCurveTo((roofRear + roofFront) / 2, roofY + 0.03, roofFront, roofY - 0.01);
  cabinShape.lineTo(cabinFront, beltY - 0.02);
  cabinShape.closePath();
  const cabinWidth = width * 0.8;
  const cabin = new THREE.Mesh(extrudeShape(cabinShape, cabinWidth, 0.1), glass);
  cabin.castShadow = true;
  group.add(cabin);

  // Roof skin + pillars over the glass.
  const roofLength = roofFront - roofRear;
  const roofMid = (roofFront + roofRear) / 2;
  box(group, [cabinWidth - 0.14, 0.04, roofLength + 0.2], [0, roofY + 0.012, roofMid], gloss, true);
  for (const side of [-1, 1]) {
    const x = side * (cabinWidth / 2 - 0.005);
    const pillarHeight = roofY - beltY;
    if (body.style !== 'coupe') {
      box(group, [0.03, pillarHeight, 0.14], [x, beltY + pillarHeight / 2, roofMid - 0.05], trim);
    }
    // Roof rail above the side windows.
    box(group, [0.035, 0.07, roofLength + 0.1], [x - side * 0.02, roofY - 0.035, roofMid], gloss);
    // Door mirrors.
    box(group, [0.2, 0.1, 0.12], [side * (width / 2 - 0.02), beltY + 0.1, cabinFront - 0.25], gloss, true);
  }

  // --- Lower trim: sills, splitter, diffuser ---
  const sillLength = layout.frontZ - layout.rearZ - archRadius * 2 - 0.1;
  box(group, [width + 0.03, 0.1, sillLength], [0, FLOOR_Y + 0.05, (layout.frontZ + layout.rearZ) / 2], trim);
  box(group, [width - 0.16, 0.05, 0.3], [0, FLOOR_Y + 0.03, half - 0.12], trim);
  box(group, [width - 0.3, 0.2, 0.14], [0, FLOOR_Y + 0.14, -half + 0.05], trim);
  // Front grille / intake.
  box(group, [width * 0.55, 0.16, 0.06], [0, FLOOR_Y + 0.27, half - 0.045], trim);

  // --- Lights ---
  const headlightMaterial = new THREE.MeshStandardMaterial({
    color: 0xffffff,
    emissive: 0xfff2d0,
    emissiveIntensity: 2.2,
    roughness: 0.2,
  });
  const taillightMaterial = new THREE.MeshStandardMaterial({
    color: 0x550000,
    emissive: 0xff1a10,
    emissiveIntensity: 2.6,
    roughness: 0.3,
  });
  for (const side of [-1, 1]) {
    const headlight = box(group, [0.44, 0.07, 0.1], [side * (width / 2 - 0.38), FLOOR_Y + 0.4, half - 0.075], headlightMaterial);
    headlight.rotation.x = -0.9;
    // Slim tail light blades wrapping toward the corners.
    box(group, [0.52, 0.065, 0.05], [side * (width / 2 - 0.36), deckY - 0.15, -half + 0.015], taillightMaterial);
  }
  if (body.style === 'coupe') {
    box(group, [width - 0.9, 0.03, 0.05], [0, deckY - 0.15, -half + 0.015], taillightMaterial);
  }

  // --- Exhausts ---
  const exhaustMaterial = new THREE.MeshStandardMaterial({ color: 0x2a2a2d, metalness: 0.95, roughness: 0.3 });
  const exhaustOffsets = body.exhausts === 4 ? [-0.62, -0.46, 0.46, 0.62] : [-0.55, 0.55];
  for (const offset of exhaustOffsets) {
    const tip = new THREE.Mesh(new THREE.CylinderGeometry(0.055, 0.055, 0.16, 14), exhaustMaterial);
    tip.rotation.x = Math.PI / 2;
    tip.position.set(offset, FLOOR_Y + 0.13, -half - 0.01);
    group.add(tip);
  }

  // --- Rear wing ---
  if (body.rearWing) {
    const wingY = deckY + 0.26;
    const wingZ = -half + 0.22;
    const blade = box(group, [width - 0.12, 0.025, 0.3], [0, wingY, wingZ], trim, true);
    blade.rotation.x = -0.14;
    for (const side of [-1, 1]) {
      box(group, [0.03, 0.26, 0.12], [side * (width * 0.3), deckY + 0.13, wingZ + 0.03], trim, true);
      box(group, [0.02, 0.12, 0.34], [side * (width / 2 - 0.06), wingY + 0.01, wingZ], trim, true);
    }
  }

  return group;
}

/** Low-profile tyre + multi-spoke alloy. Local X is the axle. */
export function buildWheel(config: VehicleConfig): THREE.Group {
  const group = new THREE.Group();
  const radius = config.wheelRadius;
  const tyreWidth = 0.29;
  const rimRadius = radius * 0.76;

  const tyreGeometry = new THREE.CylinderGeometry(radius, radius, tyreWidth, 32);
  tyreGeometry.rotateZ(Math.PI / 2);
  const tyre = new THREE.Mesh(tyreGeometry, new THREE.MeshStandardMaterial({ color: 0x0e0e0f, roughness: 0.92 }));
  tyre.castShadow = true;
  group.add(tyre);

  const barrelGeometry = new THREE.CylinderGeometry(rimRadius, rimRadius, tyreWidth + 0.008, 28);
  barrelGeometry.rotateZ(Math.PI / 2);
  group.add(new THREE.Mesh(barrelGeometry, new THREE.MeshStandardMaterial({ color: 0x101012, metalness: 0.7, roughness: 0.55 })));

  const rimMaterial = new THREE.MeshStandardMaterial({ color: config.body.wheelColor, metalness: 0.9, roughness: 0.32 });
  const lipGeometry = new THREE.TorusGeometry(rimRadius, 0.022, 8, 32);
  lipGeometry.rotateY(Math.PI / 2);
  for (const side of [-1, 1]) {
    const lip = new THREE.Mesh(lipGeometry, rimMaterial);
    lip.position.x = side * (tyreWidth / 2 + 0.004);
    group.add(lip);
  }

  // Each bar crosses the hub, so N bars read as 2N spokes.
  const barCount = 5;
  for (let i = 0; i < barCount; i++) {
    for (const split of [-0.09, 0.09]) {
      const spoke = new THREE.Mesh(new THREE.BoxGeometry(tyreWidth + 0.03, rimRadius * 1.96, 0.035), rimMaterial);
      spoke.rotation.x = (i / barCount) * Math.PI + split;
      group.add(spoke);
    }
  }
  const hubGeometry = new THREE.CylinderGeometry(radius * 0.17, radius * 0.17, tyreWidth + 0.05, 16);
  hubGeometry.rotateZ(Math.PI / 2);
  group.add(new THREE.Mesh(hubGeometry, rimMaterial));
  return group;
}
