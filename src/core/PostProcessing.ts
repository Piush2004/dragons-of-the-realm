import * as THREE from 'three';
import { EffectComposer } from 'three/examples/jsm/postprocessing/EffectComposer.js';
import { RenderPass } from 'three/examples/jsm/postprocessing/RenderPass.js';
import { UnrealBloomPass } from 'three/examples/jsm/postprocessing/UnrealBloomPass.js';
import { OutputPass } from 'three/examples/jsm/postprocessing/OutputPass.js';
import { ShaderPass } from 'three/examples/jsm/postprocessing/ShaderPass.js';
import { SMAAPass } from 'three/examples/jsm/postprocessing/SMAAPass.js';

/** Display-referred finishing: gentle S-curve contrast, colour grade, vignette and a touch of dither against banding. */
const GradeShader = {
  uniforms: {
    tDiffuse: { value: null as THREE.Texture | null },
    vignette: { value: 0.32 },
    saturation: { value: 1.1 },
    contrast: { value: 1.07 },
    warmth: { value: 0.035 },
    time: { value: 0 },
  },
  vertexShader: /* glsl */ `
    varying vec2 vUv;
    void main() {
      vUv = uv;
      gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
    }
  `,
  fragmentShader: /* glsl */ `
    uniform sampler2D tDiffuse;
    uniform float vignette;
    uniform float saturation;
    uniform float contrast;
    uniform float warmth;
    uniform float time;
    varying vec2 vUv;

    float hash(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }

    void main() {
      vec3 color = texture2D(tDiffuse, vUv).rgb;

      float luma = dot(color, vec3(0.2126, 0.7152, 0.0722));
      color = mix(vec3(luma), color, saturation);
      color = (color - 0.5) * contrast + 0.5;

      // Split toning: warm the highlights, cool the shadows.
      color += vec3(warmth, warmth * 0.35, -warmth * 0.9) * smoothstep(0.35, 0.95, luma);
      color += vec3(-warmth * 0.4, 0.0, warmth * 0.8) * (1.0 - smoothstep(0.0, 0.4, luma)) * 0.6;

      vec2 d = vUv - 0.5;
      float v = smoothstep(0.85, 0.25, length(d * vec2(1.15, 1.0)));
      color *= mix(1.0 - vignette, 1.0, v);

      color += (hash(vUv * vec2(1920.0, 1080.0) + time) - 0.5) / 255.0;
      gl_FragColor = vec4(max(color, 0.0), 1.0);
    }
  `,
};

export type AntialiasMode = 'smaa' | 'msaa' | 'none';

export interface PostProcessingOptions {
  /**
   * 'smaa' (default): cheap post-process edge smoothing, good on integrated GPUs.
   * 'msaa': multisampled HDR scene buffer, sharper but several times the cost on weak GPUs.
   */
  antialias?: AntialiasMode;
  /** Samples used when antialias is 'msaa'. */
  samples?: number;
}

/** HDR scene render with real MSAA, bloom, filmic tonemapping, then a colour grade and vignette. */
export class PostProcessing {
  private readonly composer: EffectComposer;
  private readonly bloomPass: UnrealBloomPass;
  private readonly grade: ShaderPass;
  private readonly samples: number;
  readonly mode: AntialiasMode;

  constructor(renderer: THREE.WebGLRenderer, scene: THREE.Scene, camera: THREE.PerspectiveCamera, options: PostProcessingOptions = {}) {
    renderer.toneMapping = THREE.ACESFilmicToneMapping;
    const maxSamples = renderer.capabilities.maxSamples;
    this.mode = options.antialias ?? 'smaa';
    this.samples = this.mode === 'msaa' ? Math.min(options.samples ?? 4, maxSamples) : 0;

    // The composer renders into its own buffer, which would silently drop the canvas's built-in antialiasing,
    // so give that buffer its own multisampling.
    const size = renderer.getDrawingBufferSize(new THREE.Vector2());
    const target = new THREE.WebGLRenderTarget(size.x, size.y, {
      type: THREE.HalfFloatType,
      samples: this.samples,
    });
    this.composer = new EffectComposer(renderer, target);
    this.composer.addPass(new RenderPass(scene, camera));

    this.bloomPass = new UnrealBloomPass(new THREE.Vector2(window.innerWidth, window.innerHeight), 0.22, 0.6, 0.95);
    this.composer.addPass(this.bloomPass);
    this.composer.addPass(new OutputPass());
    if (this.mode === 'smaa') this.composer.addPass(new SMAAPass(size.x, size.y));
    this.grade = new ShaderPass(GradeShader);
    this.composer.addPass(this.grade);
  }

  get msaaSamples(): number {
    return this.samples;
  }

  setSize(width: number, height: number): void {
    this.composer.setSize(width, height);
    this.bloomPass.setSize(width, height);
  }

  render(): void {
    this.grade.uniforms.time.value = (performance.now() * 0.001) % 100;
    this.composer.render();
  }
}
