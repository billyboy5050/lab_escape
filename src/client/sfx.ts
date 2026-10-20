import type { GameEvent } from '../state/types';
import { loadPref, savePref } from './telemetry';

/**
 * Sound effects, synthesised with Web Audio: no audio files and no dependencies. Which sounds a frame plays is a pure
 * function of its events (`sfxForEvents`, tested); the synth only plays them. A primed hazard makes no sound: the spec
 * gives hazards a subtle visual cue and nothing more, and a sound would be a second, louder cue.
 */
export type Sfx =
  | 'shot'
  | 'enemyShot'
  | 'gunFire'
  | 'acid'
  | 'zap'
  | 'blast'
  | 'flame'
  | 'gas'
  | 'hit'
  | 'hurt'
  | 'block'
  | 'death'
  | 'heal'
  | 'deploy'
  | 'wave'
  | 'place'
  | 'hatch'
  | 'charge'
  | 'step'
  | 'reload'
  | 'intercom'
  | 'win'
  | 'lose'
  | 'select'
  | 'endTurn'
  | 'ui';

/** Most important first: when a frame has more sounds than the cap, the ones at the end are dropped. */
const PRIORITY: Sfx[] = ['win', 'lose', 'hurt', 'blast', 'charge', 'gunFire', 'death', 'wave', 'zap', 'flame', 'gas', 'acid', 'enemyShot', 'shot', 'hit', 'block', 'hatch', 'heal', 'deploy', 'place', 'reload', 'intercom', 'step'];

/** At most this many sounds per animation frame, so a big chain reaction stays a sound and not a wall of noise. */
export const MAX_SOUNDS_PER_FRAME = 4;

export function sfxForEvents(events: readonly GameEvent[]): Sfx[] {
  const found = new Set<Sfx>();
  for (const e of events) {
    switch (e.t) {
      case 'ProjectileFired':
        if (e.source.side === 'hazard') found.add('gunFire');
        else if (e.source.via.includes('acid') || e.source.via.includes('parasite') || e.source.def === 'spitter') found.add('acid');
        else found.add(e.source.side === 'enemy' ? 'enemyShot' : 'shot');
        break;
      case 'ChainArc':
        found.add('zap');
        break;
      case 'AreaEffect':
        found.add(e.kind === 'blast' || e.kind === 'burst' ? 'blast' : e.kind === 'sweep' ? 'flame' : e.kind === 'panel' ? 'zap' : 'gas');
        break;
      case 'DamageDealt':
        if (e.amount > 0) found.add(e.target.kind === 'unit' && e.target.id === 0 ? 'hurt' : 'hit');
        else if (e.absorbed > 0) found.add('block');
        break;
      case 'UnitDied':
        found.add('death');
        break;
      case 'Healed':
        found.add('heal');
        break;
      case 'UnitSpawned':
        if (e.cause === 'egg' || e.cause === 'parasite') found.add('hatch');
        else if (!e.cause.startsWith('wave')) found.add('deploy');
        break;
      case 'WaveSpawned':
        found.add('wave');
        break;
      case 'ObjectPlaced':
        found.add('place');
        break;
      case 'EggHatched':
        found.add('hatch');
        break;
      case 'Charged':
        found.add('charge');
        break;
      case 'UnitMoved':
        found.add('step');
        break;
      case 'Reloaded':
        found.add('reload');
        break;
      case 'DroneRedeployed':
        found.add('deploy');
        break;
      case 'IntercomLine':
        found.add('intercom');
        break;
      case 'FightEnded':
        found.add(e.result === 'win' ? 'win' : 'lose');
        break;
    }
  }
  return PRIORITY.filter((s) => found.has(s)).slice(0, MAX_SOUNDS_PER_FRAME);
}

// ------------------------------------------------------------------------------------------ mute setting

const isBoolean = (v: unknown): v is boolean => typeof v === 'boolean';
let muted = loadPref('muted', false, isBoolean);
const listeners = new Set<(m: boolean) => void>();

export function isMuted(): boolean {
  return muted;
}

export function setMuted(m: boolean): void {
  muted = m;
  savePref('muted', m);
  if (m && ctx) void ctx.suspend().catch(() => undefined);
  for (const l of listeners) l(m);
}

export function onMutedChange(l: (m: boolean) => void): () => void {
  listeners.add(l);
  return () => listeners.delete(l);
}

// ------------------------------------------------------------------------------------------ synth

let ctx: AudioContext | null = null;
let master: GainNode | null = null;
let noiseBuf: AudioBuffer | null = null;

function audio(): AudioContext | null {
  if (muted || typeof window === 'undefined') return null;
  try {
    if (!ctx) {
      const AC = window.AudioContext ?? (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext;
      if (!AC) return null;
      ctx = new AC();
      master = ctx.createGain();
      master.gain.value = 0.3;
      master.connect(ctx.destination);
      // A fixed noise buffer: the sounds are decoration, so a pseudo-random fill is fine (it never touches the rules).
      noiseBuf = ctx.createBuffer(1, ctx.sampleRate, ctx.sampleRate);
      const d = noiseBuf.getChannelData(0);
      let x = 0x2545f491;
      for (let i = 0; i < d.length; i++) {
        x ^= x << 13;
        x ^= x >>> 17;
        x ^= x << 5;
        d[i] = ((x >>> 0) / 4294967296) * 2 - 1;
      }
    }
    if (ctx.state === 'suspended') void ctx.resume().catch(() => undefined);
    return ctx;
  } catch {
    return null;
  }
}

function env(a: AudioContext, vol: number, start: number, dur: number): GainNode {
  const g = a.createGain();
  g.gain.setValueAtTime(0.0001, start);
  g.gain.exponentialRampToValueAtTime(Math.max(0.0002, vol), start + Math.min(0.012, dur / 4));
  g.gain.exponentialRampToValueAtTime(0.0001, start + dur);
  g.connect(master!);
  return g;
}

function tone(type: OscillatorType, f0: number, f1: number, dur: number, vol: number, delay = 0): void {
  const a = audio();
  if (!a) return;
  const t = a.currentTime + delay;
  const o = a.createOscillator();
  o.type = type;
  o.frequency.setValueAtTime(f0, t);
  o.frequency.exponentialRampToValueAtTime(Math.max(1, f1), t + dur);
  o.connect(env(a, vol, t, dur));
  o.start(t);
  o.stop(t + dur + 0.02);
}

function noise(dur: number, vol: number, filter: BiquadFilterType, f0: number, f1: number, delay = 0, q = 1): void {
  const a = audio();
  if (!a || !noiseBuf) return;
  const t = a.currentTime + delay;
  const src = a.createBufferSource();
  src.buffer = noiseBuf;
  const bq = a.createBiquadFilter();
  bq.type = filter;
  bq.Q.value = q;
  bq.frequency.setValueAtTime(f0, t);
  bq.frequency.exponentialRampToValueAtTime(Math.max(1, f1), t + dur);
  src.connect(bq);
  bq.connect(env(a, vol, t, dur));
  src.start(t, (delay * 7919) % 0.5);
  src.stop(t + dur + 0.02);
}

const SYNTH: Record<Sfx, () => void> = {
  shot: () => {
    noise(0.08, 0.55, 'bandpass', 2600, 1100);
    tone('square', 900, 170, 0.09, 0.12);
  },
  enemyShot: () => {
    noise(0.1, 0.5, 'bandpass', 1500, 600);
    tone('square', 520, 110, 0.11, 0.12);
  },
  gunFire: () => {
    for (const d of [0, 0.07, 0.14]) {
      noise(0.07, 0.6, 'bandpass', 1800, 500, d);
      tone('square', 300, 80, 0.07, 0.1, d);
    }
  },
  acid: () => {
    noise(0.22, 0.45, 'lowpass', 1800, 260);
    tone('sine', 320, 90, 0.2, 0.25);
  },
  zap: () => {
    tone('sawtooth', 1500, 280, 0.18, 0.12);
    tone('square', 2300, 900, 0.12, 0.05, 0.03);
    noise(0.16, 0.18, 'highpass', 4200, 2600);
  },
  blast: () => {
    noise(0.7, 0.95, 'lowpass', 1700, 70);
    tone('sine', 130, 32, 0.55, 0.65);
  },
  flame: () => {
    noise(0.55, 0.55, 'bandpass', 700, 280, 0, 0.7);
    noise(0.55, 0.35, 'lowpass', 320, 180);
  },
  gas: () => {
    noise(0.8, 0.3, 'highpass', 3200, 1400);
  },
  hit: () => {
    tone('sine', 190, 60, 0.11, 0.38);
    noise(0.05, 0.28, 'lowpass', 2200, 500);
  },
  hurt: () => {
    tone('sawtooth', 230, 70, 0.26, 0.22);
    tone('sine', 115, 45, 0.32, 0.45);
    noise(0.1, 0.3, 'lowpass', 1400, 300);
  },
  block: () => {
    tone('triangle', 1250, 1150, 0.09, 0.14);
    tone('triangle', 1880, 1760, 0.12, 0.07, 0.02);
  },
  death: () => {
    tone('sawtooth', 520, 50, 0.42, 0.18);
    noise(0.38, 0.35, 'lowpass', 1300, 90);
  },
  heal: () => {
    tone('sine', 660, 660, 0.1, 0.16);
    tone('sine', 880, 880, 0.1, 0.16, 0.08);
    tone('sine', 1320, 1320, 0.18, 0.13, 0.16);
  },
  deploy: () => {
    tone('square', 300, 900, 0.18, 0.1);
    tone('sine', 900, 1250, 0.12, 0.12, 0.16);
  },
  wave: () => {
    for (const d of [0, 0.4]) {
      tone('square', 720, 720, 0.18, 0.1, d);
      tone('square', 540, 540, 0.18, 0.1, d + 0.2);
    }
  },
  place: () => {
    tone('triangle', 520, 380, 0.07, 0.2);
    noise(0.04, 0.2, 'highpass', 3000, 3000);
  },
  hatch: () => {
    noise(0.32, 0.4, 'lowpass', 950, 180);
    tone('sine', 190, 430, 0.26, 0.2);
  },
  charge: () => {
    noise(0.45, 0.6, 'bandpass', 280, 1600);
    tone('sine', 95, 38, 0.42, 0.55, 0.3);
    noise(0.25, 0.6, 'lowpass', 1200, 100, 0.32);
  },
  step: () => {
    noise(0.05, 0.12, 'lowpass', 650, 280);
  },
  reload: () => {
    noise(0.035, 0.4, 'highpass', 2600, 2600);
    noise(0.035, 0.4, 'highpass', 1800, 1800, 0.09);
  },
  intercom: () => {
    noise(0.09, 0.22, 'bandpass', 1800, 1800, 0, 3);
    tone('sine', 1000, 1000, 0.06, 0.07, 0.09);
    noise(0.06, 0.18, 'bandpass', 1800, 1800, 0.16, 3);
  },
  win: () => {
    [523, 659, 784, 1047].forEach((f, i) => tone('triangle', f, f, 0.28, 0.2, i * 0.11));
  },
  lose: () => {
    tone('sawtooth', 220, 52, 1.2, 0.2);
    tone('sine', 110, 38, 1.2, 0.32);
  },
  select: () => {
    tone('square', 1300, 1300, 0.035, 0.05);
  },
  endTurn: () => {
    tone('triangle', 660, 660, 0.08, 0.14);
    tone('triangle', 440, 440, 0.12, 0.14, 0.09);
  },
  ui: () => {
    tone('triangle', 900, 900, 0.045, 0.09);
  },
};

export function playSfx(list: readonly Sfx[]): void {
  if (muted) return;
  for (const s of list) {
    try {
      SYNTH[s]();
    } catch {
      // Audio is decoration: a failure here must never reach the game.
    }
  }
}
