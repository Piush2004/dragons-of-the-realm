import * as THREE from 'three';
import type { Combatant } from '../combat/Combatant';

const FONT = "'Segoe UI', system-ui, sans-serif";

function el(tag: string, css: string, parent?: HTMLElement): HTMLDivElement {
  const node = document.createElement(tag) as HTMLDivElement;
  node.style.cssText = css;
  parent?.appendChild(node);
  return node;
}

interface Tag {
  root: HTMLDivElement;
  name: HTMLDivElement;
  fill: HTMLDivElement;
}

/** Health, cooldowns, enemy name plates, kill feed and the damage / death overlays. */
export class CombatHud {
  private readonly root: HTMLDivElement;
  private readonly nameEl: HTMLDivElement;
  private readonly titleEl: HTMLDivElement;
  private readonly healthFill: HTMLDivElement;
  private readonly healthText: HTMLDivElement;
  private readonly pips: Record<'bite' | 'breath' | 'buffet', HTMLDivElement>;
  private readonly feed: HTMLDivElement;
  private readonly flash: HTMLDivElement;
  private readonly deathOverlay: HTMLDivElement;
  private readonly score: HTMLDivElement;
  private readonly tags = new Map<Combatant, Tag>();
  private readonly tagLayer: HTMLDivElement;
  private flashLevel = 0;
  private readonly projected = new THREE.Vector3();

  constructor(container: HTMLElement) {
    this.root = el('div', 'position:absolute;inset:0;pointer-events:none;font-family:' + FONT + ';color:#fff;user-select:none;', container);
    this.tagLayer = el('div', 'position:absolute;inset:0;overflow:hidden;', this.root);
    this.flash = el('div', 'position:absolute;inset:0;opacity:0;background:radial-gradient(ellipse at center,rgba(160,0,0,0) 45%,rgba(190,10,0,0.55) 100%);', this.root);

    const panel = el('div', 'position:absolute;left:24px;bottom:24px;width:340px;text-shadow:0 2px 6px rgba(0,0,0,.7);', this.root);
    this.nameEl = el('div', 'font-size:22px;font-weight:700;letter-spacing:.04em;', panel);
    this.titleEl = el('div', 'font-size:12px;opacity:.75;letter-spacing:.12em;text-transform:uppercase;margin-bottom:8px;', panel);
    const bar = el('div', 'height:14px;background:rgba(0,0,0,.55);border:1px solid rgba(255,255,255,.35);border-radius:3px;overflow:hidden;', panel);
    this.healthFill = el('div', 'height:100%;width:100%;background:linear-gradient(#e8452a,#a31d10);transition:width .15s;', bar);
    this.healthText = el('div', 'font-size:12px;margin-top:3px;opacity:.85;', panel);
    const pipRow = el('div', 'display:flex;gap:8px;margin-top:10px;', panel);
    const makePip = (label: string, key: string) => {
      const pip = el('div', 'position:relative;flex:1;height:30px;border:1px solid rgba(255,255,255,.35);border-radius:3px;background:rgba(0,0,0,.5);overflow:hidden;font-size:12px;display:flex;align-items:center;justify-content:center;', pipRow);
      el('div', 'position:absolute;inset:0;background:rgba(255,160,40,.35);transform-origin:left;transform:scaleX(0);', pip).dataset.role = 'cool';
      const text = el('div', 'position:relative;', pip);
      text.textContent = `${label} [${key}]`;
      return pip;
    };
    this.pips = { bite: makePip('Bite', 'J'), breath: makePip('Fire', 'F'), buffet: makePip('Buffet', 'K') };

    this.score = el('div', 'position:absolute;right:24px;top:20px;text-align:right;font-size:14px;text-shadow:0 2px 6px rgba(0,0,0,.7);', this.root);
    this.feed = el('div', 'position:absolute;left:50%;top:14%;transform:translateX(-50%);text-align:center;font-size:22px;font-weight:700;letter-spacing:.06em;text-shadow:0 2px 8px rgba(0,0,0,.8);', this.root);
    this.deathOverlay = el('div', 'position:absolute;inset:0;display:none;align-items:center;justify-content:center;flex-direction:column;background:rgba(10,0,0,.55);text-shadow:0 3px 10px #000;', this.root);
    el('div', 'font-size:54px;font-weight:800;letter-spacing:.12em;', this.deathOverlay).textContent = 'YOU HAVE FALLEN';
    el('div', 'font-size:18px;margin-top:10px;opacity:.85;', this.deathOverlay).textContent = 'Press R to rise again';
  }

  showMessage(text: string, tone: 'info' | 'kill' | 'death' = 'info'): void {
    const line = el('div', `opacity:1;transition:opacity 1.2s;color:${tone === 'kill' ? '#ffd27a' : tone === 'death' ? '#ff6a5a' : '#fff'};`, this.feed);
    line.textContent = text;
    window.setTimeout(() => {
      line.style.opacity = '0';
    }, 1800);
    window.setTimeout(() => line.remove(), 3200);
    while (this.feed.childElementCount > 3) this.feed.firstElementChild?.remove();
  }

  /** Brief red vignette when the player takes damage. */
  hurt(amount: number): void {
    this.flashLevel = Math.min(1, this.flashLevel + amount / 220);
  }

  update(dt: number, camera: THREE.Camera, player: Combatant, combatants: Combatant[], kills: number): void {
    this.nameEl.textContent = player.name;
    this.titleEl.textContent = player.profile.title;
    this.healthFill.style.width = `${player.healthFraction * 100}%`;
    this.healthText.textContent = `${Math.ceil(player.health)} / ${player.maxHealth}`;
    const set = (pip: HTMLDivElement, remaining: number, total: number) => {
      const cool = pip.querySelector('[data-role="cool"]') as HTMLDivElement;
      cool.style.transform = `scaleX(${Math.min(1, remaining / total)})`;
      pip.style.borderColor = remaining <= 0 ? 'rgba(255,200,90,.9)' : 'rgba(255,255,255,.35)';
    };
    set(this.pips.bite, player.biteCooldown, player.profile.biteCooldown);
    set(this.pips.breath, player.fireCooldown, player.profile.fireCooldown);
    set(this.pips.buffet, player.buffetCooldown, player.profile.buffetCooldown);
    this.score.textContent = `Dragons slain: ${kills}`;

    this.flashLevel = Math.max(0, this.flashLevel - dt * 1.6);
    this.flash.style.opacity = String(this.flashLevel);
    this.deathOverlay.style.display = player.alive ? 'none' : 'flex';

    // Name plates over the other dragons.
    const seen = new Set<Combatant>();
    for (const c of combatants) {
      if (c === player || !c.alive) continue;
      seen.add(c);
      let tag = this.tags.get(c);
      if (!tag) {
        const root = el('div', 'position:absolute;width:150px;margin-left:-75px;text-align:center;text-shadow:0 1px 4px #000;font-size:13px;font-weight:600;', this.tagLayer);
        const name = el('div', '', root);
        const bar = el('div', 'height:6px;margin-top:3px;background:rgba(0,0,0,.6);border:1px solid rgba(255,255,255,.4);border-radius:2px;overflow:hidden;', root);
        const fill = el('div', 'height:100%;background:#d6402a;', bar);
        tag = { root, name, fill };
        this.tags.set(c, tag);
      }
      this.projected.copy(c.position);
      this.projected.y += c.controller.tuning.bodyRadius * 1.8;
      const distance = this.projected.distanceTo(camera.position);
      this.projected.project(camera);
      const visible = this.projected.z < 1 && Math.abs(this.projected.x) < 1.15 && Math.abs(this.projected.y) < 1.15;
      tag.root.style.display = visible ? 'block' : 'none';
      if (visible) {
        tag.root.style.left = `${(this.projected.x * 0.5 + 0.5) * 100}%`;
        tag.root.style.top = `${(-this.projected.y * 0.5 + 0.5) * 100}%`;
        tag.name.textContent = `${c.name} · ${Math.round(distance)} m`;
        tag.fill.style.width = `${c.healthFraction * 100}%`;
      }
    }
    for (const [combatant, tag] of this.tags) {
      if (!seen.has(combatant)) {
        tag.root.remove();
        this.tags.delete(combatant);
      }
    }
  }
}
