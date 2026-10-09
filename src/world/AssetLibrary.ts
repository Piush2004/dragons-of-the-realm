import * as THREE from 'three';
import { GLTFLoader } from 'three/examples/jsm/loaders/GLTFLoader.js';

/** One drawable piece of a model: a geometry with a single material. */
export interface ModelPart {
  geometry: THREE.BufferGeometry;
  material: THREE.Material;
}

const ALPHA_CUTOUT_MATERIALS = /^(leaves|needles|grass)/;

/** Loads the Blender-built GLB files and hands out their meshes by object name. */
export class AssetLibrary {
  private readonly models = new Map<string, ModelPart[]>();

  static async load(urls: string[], anisotropy: number): Promise<AssetLibrary> {
    const library = new AssetLibrary();
    const loader = new GLTFLoader();
    const files = await Promise.all(urls.map((url) => loader.loadAsync(url)));
    for (const file of files) {
      for (const node of file.scene.children) library.register(node, anisotropy);
    }
    return library;
  }

  private register(node: THREE.Object3D, anisotropy: number): void {
    const parts: ModelPart[] = [];
    node.updateMatrixWorld(true);
    node.traverse((child) => {
      if (!(child instanceof THREE.Mesh)) return;
      const material = child.material as THREE.MeshStandardMaterial;
      if (ALPHA_CUTOUT_MATERIALS.test(material.name)) {
        // Cut-out foliage: hard alpha edge (no sorting needed), visible from both sides.
        material.alphaTest = 0.42;
        material.transparent = false;
        material.side = THREE.DoubleSide;
        material.depthWrite = true;
      }
      for (const map of [material.map, material.normalMap]) {
        if (map) map.anisotropy = anisotropy;
      }
      const geometry = child.geometry.clone();
      geometry.applyMatrix4(child.matrixWorld);
      parts.push({ geometry, material });
    });
    if (parts.length > 0) this.models.set(node.name, parts);
  }

  get(name: string): ModelPart[] {
    const parts = this.models.get(name);
    if (!parts) throw new Error(`Model "${name}" not found. Available: ${[...this.models.keys()].join(', ')}`);
    return parts;
  }

  has(name: string): boolean {
    return this.models.has(name);
  }

  /** Builds a regular (non-instanced) object from a model. */
  instantiate(name: string): THREE.Group {
    const group = new THREE.Group();
    for (const part of this.get(name)) {
      const mesh = new THREE.Mesh(part.geometry, part.material);
      mesh.castShadow = true;
      mesh.receiveShadow = true;
      group.add(mesh);
    }
    return group;
  }
}
