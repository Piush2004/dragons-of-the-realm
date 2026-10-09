/** Minimal DOM-based race HUD: speed, gear, RPM bar. Replaced by richer UI in later milestones. */
export class HUD {
  private readonly root: HTMLDivElement;
  private readonly speedEl: HTMLDivElement;
  private readonly gearEl: HTMLDivElement;
  private readonly rpmFillEl: HTMLDivElement;
  private readonly helpEl: HTMLDivElement;
  private readonly messageEl: HTMLDivElement;
  private messageTimer = 0;

  constructor(container: HTMLElement) {
    this.root = document.createElement('div');
    this.root.style.cssText = `
      position: absolute; bottom: 24px; right: 24px; color: #fff;
      font-family: 'Segoe UI', sans-serif; text-align: right; user-select: none;
      text-shadow: 0 2px 6px rgba(0,0,0,0.6);
      pointer-events: none;
    `;

    this.speedEl = document.createElement('div');
    this.speedEl.style.cssText = 'font-size: 48px; font-weight: 700; line-height: 1;';
    this.root.appendChild(this.speedEl);

    const speedLabel = document.createElement('div');
    speedLabel.textContent = 'km/h';
    speedLabel.style.cssText = 'font-size: 13px; opacity: 0.8; margin-bottom: 8px;';
    this.root.appendChild(speedLabel);

    this.gearEl = document.createElement('div');
    this.gearEl.style.cssText = 'font-size: 22px; font-weight: 600;';
    this.root.appendChild(this.gearEl);

    const rpmTrack = document.createElement('div');
    rpmTrack.style.cssText = 'width: 160px; height: 8px; background: rgba(255,255,255,0.25); border-radius: 4px; margin-top: 6px; overflow: hidden;';
    this.rpmFillEl = document.createElement('div');
    this.rpmFillEl.style.cssText = 'height: 100%; width: 0%; background: #ffb020; transition: width 0.05s linear;';
    rpmTrack.appendChild(this.rpmFillEl);
    this.root.appendChild(rpmTrack);

    container.appendChild(this.root);

    this.helpEl = document.createElement('div');
    this.helpEl.style.cssText = `
      position: absolute; top: 20px; left: 20px; color: #fff; font-family: 'Segoe UI', sans-serif;
      font-size: 14px; background: rgba(0,0,0,0.35); padding: 10px 14px; border-radius: 8px;
      line-height: 1.5; pointer-events: none; text-shadow: 0 1px 3px rgba(0,0,0,0.6);
    `;
    this.helpEl.textContent = 'WASD / Arrows — drive   ·   Space — handbrake   ·   C — camera   ·   B — look back   ·   R — reset';
    container.appendChild(this.helpEl);

    this.messageEl = document.createElement('div');
    this.messageEl.style.cssText = `
      position: absolute; top: 18%; left: 50%; transform: translateX(-50%); color: #fff;
      font-family: 'Segoe UI', sans-serif; font-size: 20px; font-weight: 600; letter-spacing: 0.04em;
      background: rgba(0,0,0,0.45); padding: 8px 18px; border-radius: 6px; opacity: 0;
      transition: opacity 0.25s; pointer-events: none;
    `;
    container.appendChild(this.messageEl);
  }

  /** Brief centred toast, e.g. for camera changes. */
  showMessage(text: string, durationMs = 1400): void {
    this.messageEl.textContent = text;
    this.messageEl.style.opacity = '1';
    window.clearTimeout(this.messageTimer);
    this.messageTimer = window.setTimeout(() => {
      this.messageEl.style.opacity = '0';
    }, durationMs);
  }

  update(speedKmh: number, gear: number, rpm: number): void {
    this.speedEl.textContent = Math.round(speedKmh).toString();
    this.gearEl.textContent = gear === 0 ? 'GEAR R' : `GEAR ${gear}`;
    const rpmRatio = Math.min(1, rpm / 7200);
    this.rpmFillEl.style.width = `${rpmRatio * 100}%`;
    this.rpmFillEl.style.background = rpmRatio > 0.85 ? '#ff3b30' : '#ffb020';
  }
}
