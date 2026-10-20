import { useEffect, useRef, useState, type CSSProperties, type ReactNode } from 'react';
import type { GameEvent, GameState, Pos, SourceRef } from '../../state/types';
import { MG, TS, cx, cy, tileNoise } from './geometry';
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

export function FxLayer({ frameKey, events, s, scale }: { frameKey: number; events: GameEvent[]; s: GameState; scale: number }) {
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
        <g key={f.key}>{effectsFor(f.events, f.s)}</g>
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

const P = (p: Pos) => ({ x: cx(p.x), y: cy(p.y) });

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

function Shot({ from, to, kind, seed }: { from: Pos; to: Pos; kind: ShotKind; seed: number }) {
  const a = P(from);
  const b = P(to);
  const len = Math.hypot(b.x - a.x, b.y - a.y);
  const color = COLORS[kind];
  if (kind === 'acid' || kind === 'parasite') {
    return (
      <g>
        <circle cx={a.x} cy={a.y} r={kind === 'acid' ? 5 : 4} fill={color} stroke="#0b0f12" strokeWidth={1} className="fx-move" style={css({ '--dx': `${b.x - a.x}px`, '--dy': `${b.y - a.y}px` })} />
        <Glow at={b} glow={kind === 'acid' ? 'green' : 'magenta'} r={18} delay={0.2} />
        <Sparks at={b} color={color} n={6} len={14} seed={seed} />
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
      <Glow at={a} glow={kind === 'enemy' || kind === 'hazard' ? 'amber' : 'blue'} r={14} />
      <Glow at={b} glow="white" r={12} delay={0.12} />
      <Ring at={b} color={color} r={12} width={2} delay={0.12} />
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

function centre(tiles: Pos[]): { x: number; y: number } {
  const x = tiles.reduce((s, t) => s + cx(t.x), 0) / tiles.length;
  const y = tiles.reduce((s, t) => s + cy(t.y), 0) / tiles.length;
  return { x, y };
}

function Area({ kind, tiles, seed }: { kind: Extract<GameEvent, { t: 'AreaEffect' }>['kind']; tiles: Pos[]; seed: number }) {
  if (!tiles.length) return null;
  const c = centre(tiles);
  switch (kind) {
    case 'blast':
    case 'burst':
      return (
        <g>
          {tiles.map((t, i) => (
            <circle key={i} cx={cx(t.x)} cy={cy(t.y)} r={30} fill={kind === 'burst' && i % 2 ? 'url(#lx-acid)' : 'url(#lx-fireball)'} className="fx-boom" style={css({ '--d': (i % 3) * 0.04 })} />
          ))}
          <Glow at={c} glow="white" r={40} />
          <Ring at={c} color={kind === 'burst' ? '#c8f25a' : '#ffb23d'} r={TS * 1.6} width={4} cls="fx-ring-big" />
          <Sparks at={c} color={COLORS.spark} n={12} len={TS * 1.4} seed={seed} />
          {tiles.slice(0, 5).map((t, i) => (
            <circle key={`sm${i}`} cx={cx(t.x)} cy={cy(t.y)} r={18} fill="#2b2f33" className="fx-smoke" style={css({ '--d': 0.15 + i * 0.05 })} />
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
                cx={cx(t.x) + (tileNoise(t.x, t.y, j) - 0.5) * 30}
                cy={cy(t.y) + (tileNoise(t.y, t.x, j) - 0.5) * 30}
                r={9 + j * 3}
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
              <circle cx={cx(t.x) - 8} cy={cy(t.y) + 4} r={30} fill="url(#lx-gas)" className="fx-cloud" />
              <circle cx={cx(t.x) + 10} cy={cy(t.y) - 6} r={26} fill="url(#lx-gas)" className="fx-cloud" style={css({ '--d': 0.1 })} />
            </g>
          ))}
        </g>
      );
    case 'panel':
      return (
        <g>
          {tiles.map((t, i) => (
            <g key={i}>
              <rect x={MG + t.x * TS + 3} y={MG + t.y * TS + 3} width={TS - 6} height={TS - 6} rx={4} fill="#ffd24a" className="fx-flash" />
              <Bolt a={{ x: cx(t.x) - 24, y: cy(t.y) - 18 }} b={{ x: cx(t.x) + 22, y: cy(t.y) + 20 }} seed={seed + i * 3} />
              <Bolt a={{ x: cx(t.x) + 20, y: cy(t.y) - 22 }} b={{ x: cx(t.x) - 20, y: cy(t.y) + 18 }} seed={seed + i * 3 + 1} />
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
                d={`M${cx(t.x) - 22} ${cy(t.y) + 18} q4 -28 14 -34 q-2 14 8 18 q2 -16 14 -22 q-4 20 8 38 z`}
                fill="url(#lx-fireball)"
                stroke="#ff6a1a"
                strokeWidth={1}
              />
            </g>
          ))}
          <Glow at={c} glow="amber" r={TS} cls="fx-flash" />
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

export function effectsFor(events: GameEvent[], s: GameState): ReactNode[] {
  const out: ReactNode[] = [];
  const unitPos = (id: number): Pos | null => s.units.find((u) => u.id === id)?.pos ?? null;
  // Several hits on one unit in a frame stack their numbers instead of drawing over each other.
  const hitsAt = new Map<string, number>();
  events.forEach((e, i) => {
    const seed = i * 7 + 3;
    switch (e.t) {
      case 'ProjectileFired': {
        const end = e.hitPos ?? e.path[e.path.length - 1];
        if (end) out.push(<Shot key={i} from={e.from} to={end} kind={shotKind(e.source, e.reflected)} seed={seed} />);
        break;
      }
      case 'ChainArc':
        out.push(<Bolt key={i} a={P(e.from)} b={P(e.to)} seed={seed} />);
        out.push(<Glow key={`${i}g`} at={P(e.to)} glow="white" r={14} />);
        break;
      case 'AreaEffect':
        out.push(<Area key={i} kind={e.kind} tiles={e.tiles} seed={seed} />);
        break;
      case 'DamageDealt': {
        const k = `${e.pos.x},${e.pos.y}`;
        const n = hitsAt.get(k) ?? 0;
        hitsAt.set(k, n + 1);
        const at = { x: cx(e.pos.x), y: cy(e.pos.y) - 10 - n * 16 };
        out.push(<FloatText key={i} at={at} text={e.amount > 0 ? `-${e.amount}` : e.absorbed > 0 ? 'BLOCK' : '0'} color={e.amount > 0 ? '#ff5a4a' : '#b9c4c9'} size={e.amount >= 3 ? 24 : 20} delay={n * 0.05} />);
        if (e.amount > 0) out.push(<Glow key={`${i}h`} at={P(e.pos)} glow={e.targetSide === 'enemy' ? 'amber' : 'red'} r={20} />);
        break;
      }
      case 'Healed': {
        const p = unitPos(e.target);
        if (!p) break;
        out.push(<FloatText key={i} at={{ x: cx(p.x), y: cy(p.y) - 10 }} text={`+${e.amount}`} color="#5ff0a0" />);
        out.push(<Ring key={`${i}r`} at={P(p)} color="#5ff0a0" r={24} />);
        [-10, 4, 14].forEach((dx, j) =>
          out.push(
            <text key={`${i}p${j}`} x={cx(p.x) + dx} y={cy(p.y) + 8} fontSize={12} fontWeight={900} fill="#5ff0a0" className="fx-rise" style={css({ '--d': j * 0.1 })}>
              +
            </text>,
          ),
        );
        break;
      }
      case 'UnitDied': {
        const at = P(e.pos);
        const color = e.def === 'player' ? '#ff3b2f' : e.team === 'enemy' ? '#ffb23d' : e.def === 'hatchling' || e.def === 'spitter' || e.def === 'burster' ? '#8be03a' : '#7fd4ff';
        out.push(<Glow key={`${i}g`} at={at} glow={e.def === 'player' ? 'red' : e.team === 'enemy' ? 'amber' : 'blue'} r={e.def === 'warden' ? 46 : 30} />);
        out.push(<Ring key={`${i}r`} at={at} color={color} r={e.def === 'warden' ? 44 : 30} width={3} />);
        out.push(<Sparks key={`${i}s`} at={at} color={color} n={10} len={30} seed={seed} />);
        out.push(<circle key={`${i}m`} cx={at.x} cy={at.y} r={20} fill="#2b2f33" className="fx-smoke" />);
        break;
      }
      case 'Charged': {
        const a = P(e.from);
        const b = P(e.to);
        out.push(<line key={i} x1={a.x} y1={a.y} x2={b.x} y2={b.y} stroke="#ff3b2f" strokeWidth={22} strokeLinecap="round" opacity={0.35} className="fx-fade" />);
        out.push(<line key={`${i}c`} x1={a.x} y1={a.y} x2={b.x} y2={b.y} stroke="#ffd0c0" strokeWidth={4} strokeLinecap="round" className="fx-fade" />);
        out.push(<Ring key={`${i}r`} at={b} color="#ff6a4a" r={36} width={4} cls="fx-ring-big" />);
        break;
      }
      case 'UnitSpawned': {
        const at = P(e.unit.pos);
        if (e.cause.startsWith('wave')) {
          out.push(<rect key={i} x={at.x - 14} y={at.y - 90} width={28} height={92} fill="url(#lx-glow-red)" className="fx-beam" />);
          out.push(<Ring key={`${i}r`} at={{ x: at.x, y: at.y + 18 }} color="#ff6a4a" r={24} />);
        } else if (e.cause === 'egg' || e.cause === 'parasite') {
          out.push(<Glow key={i} at={at} glow="green" r={26} />);
          out.push(<Sparks key={`${i}s`} at={at} color="#8be03a" n={8} len={22} seed={seed} />);
        } else {
          out.push(<Ring key={i} at={{ x: at.x, y: at.y + 18 }} color="#7fd4ff" r={26} />);
          out.push(<rect key={`${i}b`} x={at.x - 18} y={at.y - 40} width={36} height={58} fill="url(#lx-glow-blue)" className="fx-beam" />);
        }
        break;
      }
      case 'ObjectPlaced': {
        const at = P(e.object.pos);
        out.push(<Ring key={i} at={at} color={e.object.kind === 'egg' ? '#8be03a' : '#7fd4ff'} r={22} />);
        break;
      }
      case 'ObjectDestroyed':
        out.push(<Sparks key={i} at={P(e.pos)} color="#c8d0d4" n={8} len={22} seed={seed} />);
        out.push(<Ring key={`${i}r`} at={P(e.pos)} color="#c8d0d4" r={20} />);
        break;
      case 'ObjectTriggered':
        out.push(<Glow key={i} at={P(e.pos)} glow="white" r={28} />);
        break;
      case 'EggHatched': {
        const at = P(e.pos);
        out.push(<Glow key={i} at={at} glow="green" r={30} />);
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
              style={css({ '--dx': `${(Math.cos(a) * 26).toFixed(1)}px`, '--dy': `${(Math.sin(a) * 26).toFixed(1)}px` })}
            />,
          );
        }
        break;
      }
      case 'StatusApplied': {
        const p = unitPos(e.target);
        if (!p) break;
        out.push(
          <g key={i} transform={`translate(${cx(p.x) - 9},${cy(p.y) - 46}) scale(0.75)`}>
            <g className="fx-pop-up" style={{ color: STATUS_COLOR[e.status] ?? 'var(--ink)' }}>
              <IconGlyph name={e.status} strokeWidth={2.4} />
            </g>
          </g>,
        );
        break;
      }
      case 'HazardFired': {
        const t = s.hazards.find((h) => h.id === e.hazard);
        if (t?.def === 'wall_gun' && t.tiles[0]) out.push(<Glow key={i} at={{ x: cx(t.tiles[0].x) - 26, y: cy(t.tiles[0].y) }} glow="amber" r={30} />);
        break;
      }
      case 'DroneDocked':
      case 'DroneRedeployed':
        out.push(<Ring key={i} at={P(e.pos)} color="#7fd4ff" r={18} />);
        break;
      case 'Reloaded': {
        const p = unitPos(e.id);
        if (p) out.push(<FloatText key={i} at={{ x: cx(p.x), y: cy(p.y) - 14 }} text="RELOAD" color="#7fd4ff" size={13} />);
        break;
      }
      case 'Purged': {
        const p = unitPos(e.id);
        if (p) {
          out.push(<Ring key={i} at={P(p)} color="#ffffff" r={30} />);
          out.push(<FloatText key={`${i}t`} at={{ x: cx(p.x), y: cy(p.y) - 18 }} text="PURGE" color="#ffffff" size={13} />);
        }
        break;
      }
    }
  });
  return out;
}
