export interface DriveInput {
  throttle: number; // 0..1
  brake: number; // 0..1
  steer: number; // -1..1 (left negative, right positive)
  handbrake: boolean;
}

const THROTTLE_KEYS = ['KeyW', 'ArrowUp'];
const BRAKE_KEYS = ['KeyS', 'ArrowDown'];
const LEFT_KEYS = ['KeyA', 'ArrowLeft'];
const RIGHT_KEYS = ['KeyD', 'ArrowRight'];
const HANDBRAKE_KEYS = ['Space'];

export class InputManager {
  private readonly pressed = new Set<string>();

  constructor() {
    window.addEventListener('keydown', (e) => this.pressed.add(e.code));
    window.addEventListener('keyup', (e) => this.pressed.delete(e.code));
    // Avoid stuck keys when the window loses focus mid-press.
    window.addEventListener('blur', () => this.pressed.clear());
  }

  isPressed(code: string): boolean {
    return this.pressed.has(code);
  }

  private anyPressed(codes: string[]): boolean {
    return codes.some((c) => this.pressed.has(c));
  }

  getDriveInput(): DriveInput {
    const left = this.anyPressed(LEFT_KEYS);
    const right = this.anyPressed(RIGHT_KEYS);
    let steer = 0;
    if (left && !right) steer = -1;
    if (right && !left) steer = 1;

    return {
      throttle: this.anyPressed(THROTTLE_KEYS) ? 1 : 0,
      brake: this.anyPressed(BRAKE_KEYS) ? 1 : 0,
      steer,
      handbrake: this.anyPressed(HANDBRAKE_KEYS),
    };
  }
}
