import { useEffect, useRef, useState, type CSSProperties, type ReactNode } from 'react';
import type { GameEvent, GameState, Pos, SourceRef } from '../../state/types';
import { MG, TS, projection, tileNoise, type Projection } from './geometry';
import { muzzle } from './iso';
import { IconGlyph } from './icons';

/**
 * Combat effects, drawn from the engine's events for each animation frame. Effects are decoration only: they read the
 * events and never the rules. Every effect is a CSS animation that starts when its element mounts (SMIL begin times
 * count from the document's start, so they misfire on elements added later). Durations scale with `--fx` on the board,
 * so 2x speed plays them faster, and the reduced-motion setting shortens them to almost nothing.
 */

/** How long a frame's effects stay mounted, in seconds at 1x. Longer than any effect below. */
const LIFETIME = 1.7;

interface FxFrame {
  key: number;
  events: GameEvent[];
  s: GameState;
}

/** Effects default to the top-down board, whose projection does not depend on the board's size. */
const TOP = projection('top', 8, 8);

export function FxLayer({ frameKey, events, s, scale, proj = TOP }: { frameKey: number; events: GameEvent[]; s: GameState; scale: number; proj?: Projection }) {
  const [frames, setFrames] = useState<FxFrame[]>([]);
  const timers = useRef<number[]>([]);
  useEffect(() => {
    if (!events.length) return;
    const f = { key: frameKey, events, s };
    setFrames((old) => [...old.slice(-5), f]);
    const t = window.setTimeout(() => {
      timers.current = timers.current.filter((x) => x !== t);
      setFrames((old) => old.filter((x) => x.key !== f.key));
    }, LIFETIME * scale * 1000);
    timers.current.push(t);
    // Only a new frame adds effects; the state and scale are read as they were when it arrived.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [frameKey]);
  useEffect(() => () => timers.current.forEach((t) => window.clearTimeout(t)), []);
  return (
    <g pointerEvents="none" className="fx-layer">
      {frames.map((f) => (
        <g key={f.key}>{effectsFor(f.events, f.s, proj)}</g>
      ))}
    </g>
  );
}

// ------------------------------------------------------------------------------------------ board reactions

export interface FrameMood {
  /** Shake the board: a light jolt, or a heavy one for blasts and charges. */
  shake: 'light' | 'heavy' | null;
  /** A lab hazard fired, or a new wave arrived: flash the alarm. */
  alarm: boolean;
  /** The player was hurt: flash the edges red. */
  hurt: boolean;
}

/** What a frame's events do to the board as a whole. */
export function frameMood(events: GameEvent[]): FrameMood {
  let shake: FrameMood['shake'] = null;
  let alarm = false;
  let hurt = false;
  for (const e of events) {
    if (e.t === 'AreaEffect' && (e.kind === 'blast' || e.kind === 'burst')) shake = 'heavy';
    else if (e.t === 'Charged') shake = 'heavy';
    else if (e.t === 'HazardFired') {
      alarm = true;
      shake ??= 'light';
    } else if (e.t === 'WaveSpawned' && e.wave > 1) alarm = true;
    else if (e.t === 'DamageDealt' && e.target.kind === 'unit' && e.target.id === 0 && e.amount > 0) {
      hurt = true;
      shake ??= 'light';
    }
  }
  return { shake, alarm, hurt };
}

// ------------------------------------------------------------------------------------------ effects

const COLORS = {
  player: '#7fd4ff',
  minion: '#a6ecff',
  enemy: '#ffb36a',
  hazard: '#ffd24a',
  acid: '#b6f04a',
  parasite: '#ff7ad9',
  spark: '#fff1a8',
};

type ShotKind = keyof typeof COLORS;

function shotKind(src: SourceRef, reflected?: boolean): ShotKind {
  if (reflected) return 'parasite';
  if (src.via.includes('acid') || src.def === 'spitter') return 'acid';
  if (src.via.includes('parasite')) return 'parasite';
  if (src.side === 'hazard') return 'hazard';
  if (src.side === 'enemy') return 'enemy';
  return src.category === 'minion' ? 'minion' : 'player';
}


function css(vars: Record<string, string | number>): CSSProperties {
  return vars as CSSProperties;
}

function Ring({ at, color, r = 22, width = 3, cls = 'fx-ring', delay = 0 }: { at: { x: number; y: number }; color: string; r?: number; width?: number; cls?: string; delay?: number }) {
  return <circle cx={at.x} cy={at.y} r={r} fill="none" stroke={color} strokeWidth={width} className={cls} style={css({ '--d': delay })} />;
}

function Glow({ at, glow, r = 16, cls = 'fx-pop', delay = 0 }: { at: { x: number; y: number }; glow: string; r?: number; cls?: string; delay?: number }) {
  return <circle cx={at.x} cy={at.y} r={r} fill={`url(#lx-glow-${glow})`} className={cls} style={css({ '--d': delay })} />;
}

function Sparks({ at, color, n = 7, len = 16, seed = 0 }: { at: { x: number; y: number }; color: string; n?: number; len?: number; seed?: number }) {
  return (
    <g>
      {Array.from({ length: n }, (_, i) => {
        const a = (i / n) * Math.PI * 2 + tileNoise(seed, i, 5) * 0.8;
        const l = len * (0.6 + tileNoise(seed, i, 6) * 0.7);
        return (
          <line
            key={i}
            x1={at.x}
            y1={at.y}
            x2={at.x + Math.cos(a) * 3}
            y2={at.y + Math.sin(a) * 3}
            stroke={color}
            strokeWidth={2}
            strokeLinecap="round"
            className="fx-spark"
            style={css({ '--dx': `${(Math.cos(a) * l).toFixed(1)}px`, '--dy': `${(Math.sin(a) * l).toFixed(1)}px` })}
          />
        );
      })}
    </g>
  );
}

function Shot({ from, to, kind, seed, g }: { from: Pos; to: Pos; kind: ShotKind; seed: number; g: Projection }) {
  const a = g.near(from.x, from.y, 0, 0);
  const b = g.near(to.x, to.y, 0, 0);
  const len = Math.hypot(b.x - a.x, b.y - a.y);
  const color = COLORS[kind];
  if (kind === 'acid' || kind === 'parasite') {
    return (
      <g>
        <circle cx={a.x} cy={a.y} r={kind === 'acid' ? 5 : 4} fill={color} stroke="#0b0f12" strokeWidth={1} className="fx-move" style={css({ '--dx': `${b.x - a.x}px`, '--dy': `${b.y - a.y}px` })} />
        <Glow at={b} glow={kind === 'acid' ? 'green' : 'magenta'} r={18 * g.k} delay={0.2} />
        <Sparks at={b} color={color} n={6} len={14 * g.k} seed={seed} />
      </g>
    );
  }
  return (
    <g>
      <line x1={a.x} y1={a.y} x2={b.x} y2={b.y} stroke={color} strokeWidth={1.5} opacity={0.35} className="fx-fade" />
      <line
        x1={a.x}
        y1={a.y}
        x2={b.x}
        y2={b.y}
        stroke={color}
        strokeWidth={4}
        strokeLinecap="round"
        className="fx-tracer"
        style={css({ '--len': len.toFixed(0), '--seg': 22, strokeDasharray: `22 ${(len + 30).toFixed(0)}` })}
      />
      <Glow at={a} glow={kind === 'enemy' || kind === 'hazard' ? 'amber' : 'blue'} r={14 * g.k} />
      <Glow at={b} glow="white" r={12 * g.k} delay={0.12} />
      <Ring at={b} color={color} r={12 * g.k} width={2} delay={0.12} />
    </g>
  );
}

/** A jagged bolt between two tiles. The kinks are fixed per pair of tiles, so a replay draws the same bolt. */
export function boltPoints(a: { x: number; y: number }, b: { x: number; y: number }, seed: number, steps = 6, amp = 7): string {
  const pts: string[] = [];
  const dx = b.x - a.x;
  const dy = b.y - a.y;
  const len = Math.hypot(dx, dy) || 1;
  const nx = -dy / len;
  const ny = dx / len;
  for (let i = 0; i <= steps; i++) {
    const t = i / steps;
    const off = i === 0 || i === steps ? 0 : (tileNoise(seed, i, 9) - 0.5) * 2 * amp;
    pts.push(`${(a.x + dx * t + nx * off).toFixed(1)},${(a.y + dy * t + ny * off).toFixed(1)}`);
  }
  return pts.join(' ');
}

function Bolt({ a, b, seed, color = '#fff27a' }: { a: { x: number; y: number }; b: { x: number; y: number }; seed: number; color?: string }) {
  const pts = boltPoints(a, b, seed);
  return (
    <g className="fx-flicker">
      <polyline points={pts} fill="none" stroke={color} strokeWidth={5} strokeLinejoin="round" opacity={0.45} />
      <polyline points={pts} fill="none" stroke="#ffffff" strokeWidth={1.8} strokeLinejoin="round" />
    </g>
  );
}

function centre(tiles: Pos[], g: Projection): { x: number; y: number } {
  const pts = tiles.map((t) => g.near(t.x, t.y, 0, 0));
  const x = pts.reduce((s, p) => s + p.x, 0) / pts.length;
  const y = pts.reduce((s, p) => s + p.y, 0) / pts.length;
  return { x, y };
}

function Area({ kind, tiles, seed, g }: { kind: Extract<GameEvent, { t: 'AreaEffect' }>['kind']; tiles: Pos[]; seed: number; g: Projection }) {
  if (!tiles.length) return null;
  const c = centre(tiles, g);
  // Each tile's centre at body height, and its centre on the floor.
  const B = (t: Pos) => g.near(t.x, t.y, 0, 0);
  const F = (t: Pos) => g.at(t.x, t.y);
  const k = g.k;
  switch (kind) {
    case 'blast':
    case 'burst':
      return (
        <g>
          {tiles.map((t, i) => (
            <circle key={i} cx={B(t).x} cy={B(t).y} r={30 * k} fill={kind === 'burst' && i % 2 ? 'url(#lx-acid)' : 'url(#lx-fireball)'} className="fx-boom" style={css({ '--d': (i % 3) * 0.04 })} />
          ))}
          <Glow at={c} glow="white" r={40 * k} />
          <Ring at={c} color={kind === 'burst' ? '#c8f25a' : '#ffb23d'} r={TS * 1.6 * k} width={4} cls="fx-ring-big" />
          <Sparks at={c} color={COLORS.spark} n={12} len={TS * 1.4 * k} seed={seed} />
          {tiles.slice(0, 5).map((t, i) => (
            <circle key={`sm${i}`} cx={B(t).x} cy={B(t).y} r={18 * k} fill="#2b2f33" className="fx-smoke" style={css({ '--d': 0.15 + i * 0.05 })} />
          ))}
        </g>
      );
    case 'spore':
      return (
        <g>
          {tiles.flatMap((t, i) =>
            [0, 1, 2].map((j) => (
              <circle
                key={`${i}-${j}`}
                cx={B(t).x + (tileNoise(t.x, t.y, j) - 0.5) * 30 * k}
                cy={B(t).y + (tileNoise(t.y, t.x, j) - 0.5) * 30}
                r={(9 + j * 3) * k}
                fill="#7fd36a"
                opacity={0.55}
                className="fx-puff"
                style={css({ '--d': j * 0.08 })}
              />
            )),
          )}
        </g>
      );
    case 'gas':
      return (
        <g>
          {tiles.map((t, i) => (
            <g key={i}>
              <circle cx={B(t).x - 8 * k} cy={B(t).y + 4} r={30 * k} fill="url(#lx-gas)" className="fx-cloud" />
              <circle cx={B(t).x + 10 * k} cy={B(t).y - 6} r={26 * k} fill="url(#lx-gas)" className="fx-cloud" style={css({ '--d': 0.1 })} />
            </g>
          ))}
        </g>
      );
    case 'panel':
      return (
        <g>
          {tiles.map((t, i) => (
            <g key={i}>
              <g transform={g.floor}>
                <rect x={MG + t.x * TS + 3} y={MG + t.y * TS + 3} width={TS - 6} height={TS - 6} rx={4} fill="#ffd24a" className="fx-flash" />
              </g>
              <Bolt a={{ x: F(t).x - 24, y: F(t).y - 18 }} b={{ x: F(t).x + 22, y: F(t).y + 20 }} seed={seed + i * 3} />
              <Bolt a={{ x: F(t).x + 20, y: F(t).y - 22 }} b={{ x: F(t).x - 20, y: F(t).y + 18 }} seed={seed + i * 3 + 1} />
            </g>
          ))}
        </g>
      );
    case 'sweep':
      return (
        <g>
          {tiles.map((t, i) => (
            <g key={i} className="fx-flame" style={css({ '--d': i * 0.06 })}>
              <path
                d={`M${B(t).x - 22} ${B(t).y + 18} q4 -28 14 -34 q-2 14 8 18 q2 -16 14 -22 q-4 20 8 38 z`}
                fill="url(#lx-fireball)"
                stroke="#ff6a1a"
                strokeWidth={1}
              />
            </g>
          ))}
          <Glow at={c} glow="amber" r={TS * k} cls="fx-flash" />
        </g>
      );
  }
}

function FloatText({ at, text, color, size = 20, delay = 0 }: { at: { x: number; y: number }; text: string; color: string; size?: number; delay?: number }) {
  return (
    <text
      x={at.x}
      y={at.y}
      textAnchor="middle"
      fontSize={size}
      fontWeight={800}
      fontFamily="var(--display)"
      fill={color}
      stroke="#0b0f12"
      strokeWidth={4}
      paintOrder="stroke"
      className="fx-float"
      style={css({ '--d': delay })}
    >
      {text}
    </text>
  );
}

const STATUS_COLOR: Record<string, string> = { poison: 'var(--poison)', parasite: 'var(--parasite)', corrode: 'var(--corrode)', pinned: 'var(--ink)', slowed: 'var(--ink)' };

/**
 * The effects for one frame's events. `g` places them: on the top-down board a unit's effects centre on its tile, on the
 * isometric board on its body, a little above the floor, and they are as much larger as the figures are.
 */
export function effectsFor(events: GameEvent[], s: GameState, g: Projection = TOP): ReactNode[] {
  const out: ReactNode[] = [];
  const unitPos = (id: number): Pos | null => s.units.find((u) => u.id === id)?.pos ?? null;
  /** A point in a unit's own drawing units, for a unit on tile p (its centre when dx and dy are 0). */
  const N = (p: Pos, dx = 0, dy = 0) => g.near(p.x, p.y, dx, dy);
  const k = g.k;
  // Several hits on one unit in a frame stack their numbers instead of drawing over each other.
  const hitsAt = new Map<string, number>();
  events.forEach((e, i) => {
    const seed = i * 7 + 3;
    switch (e.t) {
      case 'ProjectileFired': {
        const end = e.hitPos ?? e.path[e.path.length - 1];
        if (end) out.push(<Shot key={i} from={e.from} to={end} kind={shotKind(e.source, e.reflected)} seed={seed} g={g} />);
        break;
      }
      case 'ChainArc':
        out.push(<Bolt key={i} a={N(e.from)} b={N(e.to)} seed={seed} />);
        out.push(<Glow key={`${i}g`} at={N(e.to)} glow="white" r={14 * k} />);
        break;
      case 'AreaEffect':
        out.push(<Area key={i} kind={e.kind} tiles={e.tiles} seed={seed} g={g} />);
        break;
      case 'DamageDealt': {
        const key = `${e.pos.x},${e.pos.y}`;
        const n = hitsAt.get(key) ?? 0;
        hitsAt.set(key, n + 1);
        const at = N(e.pos, 0, -10 - n * 16);
        out.push(<FloatText key={i} at={at} text={e.amount > 0 ? `-${e.amount}` : e.absorbed > 0 ? 'BLOCK' : '0'} color={e.amount > 0 ? '#ff5a4a' : '#b9c4c9'} size={(e.amount >= 3 ? 24 : 20) * k} delay={n * 0.05} />);
        if (e.amount > 0) out.push(<Glow key={`${i}h`} at={N(e.pos)} glow={e.targetSide === 'enemy' ? 'amber' : 'red'} r={20 * k} />);
        break;
      }
      case 'Healed': {
        const p = unitPos(e.target);
        if (!p) break;
        out.push(<FloatText key={i} at={N(p, 0, -10)} text={`+${e.amount}`} color="#5ff0a0" size={20 * k} />);
        out.push(<Ring key={`${i}r`} at={N(p)} color="#5ff0a0" r={24 * k} />);
        [-10, 4, 14].forEach((dx, j) => {
          const at = N(p, dx, 8);
          out.push(
            <text key={`${i}p${j}`} x={at.x} y={at.y} fontSize={12 * k} fontWeight={900} fill="#5ff0a0" className="fx-rise" style={css({ '--d': j * 0.1 })}>
              +
            </text>,
          );
        });
        break;
      }
      case 'UnitDied': {
        const at = N(e.pos);
        const color = e.def === 'player' ? '#ff3b2f' : e.team === 'enemy' ? '#ffb23d' : e.def === 'hatchling' || e.def === 'spitter' || e.def === 'burster' ? '#8be03a' : '#7fd4ff';
        out.push(<Glow key={`${i}g`} at={at} glow={e.def === 'player' ? 'red' : e.team === 'enemy' ? 'amber' : 'blue'} r={(e.def === 'warden' ? 46 : 30) * k} />);
        out.push(<Ring key={`${i}r`} at={at} color={color} r={(e.def === 'warden' ? 44 : 30) * k} width={3} />);
        out.push(<Sparks key={`${i}s`} at={at} color={color} n={10} len={30 * k} seed={seed} />);
        out.push(<circle key={`${i}m`} cx={at.x} cy={at.y} r={20 * k} fill="#2b2f33" className="fx-smoke" />);
        break;
      }
      case 'Charged': {
        const a = N(e.from);
        const b = N(e.to);
        out.push(<line key={i} x1={a.x} y1={a.y} x2={b.x} y2={b.y} stroke="#ff3b2f" strokeWidth={22} strokeLinecap="round" opacity={0.35} className="fx-fade" />);
        out.push(<line key={`${i}c`} x1={a.x} y1={a.y} x2={b.x} y2={b.y} stroke="#ffd0c0" strokeWidth={4} strokeLinecap="round" className="fx-fade" />);
        out.push(<Ring key={`${i}r`} at={b} color="#ff6a4a" r={36 * k} width={4} cls="fx-ring-big" />);
        break;
      }
      case 'UnitSpawned': {
        const p = e.unit.pos;
        const at = N(p);
        if (e.cause.startsWith('wave')) {
          const top = N(p, -14, -90);
          out.push(<rect key={i} x={top.x} y={top.y} width={28 * k} height={92 * k} fill="url(#lx-glow-red)" className="fx-beam" />);
          out.push(<Ring key={`${i}r`} at={N(p, 0, 18)} color="#ff6a4a" r={24 * k} />);
        } else if (e.cause === 'egg' || e.cause === 'parasite') {
          out.push(<Glow key={i} at={at} glow="green" r={26 * k} />);
          out.push(<Sparks key={`${i}s`} at={at} color="#8be03a" n={8} len={22 * k} seed={seed} />);
        } else {
          const top = N(p, -18, -40);
          out.push(<Ring key={i} at={N(p, 0, 18)} color="#7fd4ff" r={26 * k} />);
          out.push(<rect key={`${i}b`} x={top.x} y={top.y} width={36 * k} height={58 * k} fill="url(#lx-glow-blue)" className="fx-beam" />);
        }
        break;
      }
      case 'ObjectPlaced': {
        const at = N(e.object.pos);
        out.push(<Ring key={i} at={at} color={e.object.kind === 'egg' ? '#8be03a' : '#7fd4ff'} r={22 * k} />);
        break;
      }
      case 'ObjectDestroyed':
        out.push(<Sparks key={i} at={N(e.pos)} color="#c8d0d4" n={8} len={22 * k} seed={seed} />);
        out.push(<Ring key={`${i}r`} at={N(e.pos)} color="#c8d0d4" r={20 * k} />);
        break;
      case 'ObjectTriggered':
        out.push(<Glow key={i} at={N(e.pos)} glow="white" r={28 * k} />);
        break;
      case 'EggHatched': {
        const at = N(e.pos);
        out.push(<Glow key={i} at={at} glow="green" r={30 * k} />);
        for (let j = 0; j < 7; j++) {
          const a = (j / 7) * Math.PI * 2;
          out.push(
            <ellipse
              key={`${i}s${j}`}
              cx={at.x}
              cy={at.y}
              rx={4}
              ry={2.5}
              fill="#4fc890"
              stroke="#0f4a35"
              strokeWidth={0.8}
              className="fx-spark"
              style={css({ '--dx': `${(Math.cos(a) * 26 * k).toFixed(1)}px`, '--dy': `${(Math.sin(a) * 26 * k).toFixed(1)}px` })}
            />,
          );
        }
        break;
      }
      case 'StatusApplied': {
        const p = unitPos(e.target);
        if (!p) break;
        const at = N(p, -9, -46);
        out.push(
          <g key={i} transform={`translate(${at.x},${at.y}) scale(${0.75 * k})`}>
            <g className="fx-pop-up" style={{ color: STATUS_COLOR[e.status] ?? 'var(--ink)' }}>
              <IconGlyph name={e.status} strokeWidth={2.4} />
            </g>
          </g>,
        );
        break;
      }
      case 'HazardFired': {
        const t = s.hazards.find((h) => h.id === e.hazard);
        if (t?.def === 'wall_gun' && t.tiles[0]) out.push(<Glow key={i} at={muzzle(g, t.tiles[0], t.dir)} glow="amber" r={30 * k} />);
        break;
      }
      case 'DroneDocked':
      case 'DroneRedeployed':
        out.push(<Ring key={i} at={N(e.pos)} color="#7fd4ff" r={18 * k} />);
        break;
      case 'Reloaded': {
        const p = unitPos(e.id);
        if (p) out.push(<FloatText key={i} at={N(p, 0, -14)} text="RELOAD" color="#7fd4ff" size={13 * k} />);
        break;
      }
      case 'Purged': {
        const p = unitPos(e.id);
        if (p) {
          out.push(<Ring key={i} at={N(p)} color="#ffffff" r={30 * k} />);
          out.push(<FloatText key={`${i}t`} at={N(p, 0, -18)} text="PURGE" color="#ffffff" size={13 * k} />);
        }
        break;
      }
    }
  });
  return out;
}
