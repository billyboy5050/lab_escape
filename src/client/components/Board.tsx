import { useRef, type ReactNode } from 'react';
import type { Content } from '../../content/types';
import type { CommandPreview, HitPreview, IntentReport, LinePreview } from '../../preview/preview';
import { boardFor } from '../../state/grid';
import type { AIOption, GameEvent, GameObject, GameState, Hazard, Pos, Unit } from '../../state/types';
import { tileName } from '../../util/tiles';

export const TS = 64;
export const MG = 24;
const cx = (x: number) => MG + x * TS + TS / 2;
const cy = (y: number) => MG + y * TS + TS / 2;
const key = (p: Pos) => `${p.x},${p.y}`;

export interface Highlight {
  tiles: Pos[];
  kind: 'move' | 'target' | 'second' | 'spawn' | 'chosen' | 'debug';
  label?: (p: Pos) => string | null;
}

export interface BoardDebug {
  ids: boolean;
  spawns: { tile: Pos; wave: number }[];
  flamer: AIOption[] | null;
  hazards: boolean;
}

export interface BoardProps {
  c: Content;
  s: GameState;
  frameEvents: GameEvent[];
  frameKey: number;
  highlights: Highlight[];
  hover: Pos | null;
  movePath: Pos[] | null;
  onHover: (p: Pos | null) => void;
  onClick: (p: Pos, touch: boolean) => void;
  preview: CommandPreview | null;
  intents: IntentReport | null;
  showIntents: boolean;
  debug: BoardDebug | null;
  dimmed?: boolean;
  overlay?: ReactNode;
}

const UNIT_COLOR: Record<string, string> = {
  player: 'var(--player)',
  turret: 'var(--minion)',
  drone: 'var(--minion)',
  hatchling: 'var(--hatch)',
  spitter: 'var(--hatch)',
  burster: 'var(--hatch)',
  guard: 'var(--enemy)',
  medic: 'var(--medic)',
  flamer: 'var(--hazard)',
  warden: 'var(--elite)',
};
const UNIT_LETTER: Record<string, string> = { player: '@', turret: 'T', drone: 'D', hatchling: 'h', spitter: 's', burster: 'b', guard: 'G', medic: '+', flamer: 'F', warden: 'W' };

export function Board(p: BoardProps) {
  const { c, s } = p;
  const b = boardFor(c.map);
  const W = MG * 2 + b.width * TS;
  const H = MG * 2 + b.height * TS;
  const lastPointer = useRef<'mouse' | 'touch' | 'pen'>('mouse');
  const tiles = b.allTiles();
  const hazardAt = new Map<string, Hazard>();
  for (const h of s.hazards) for (const t of h.tiles) hazardAt.set(key(t), h);
  const units = s.units.filter((u) => !u.dead && u.droneState !== 'docked');

  return (
    <svg className="board-svg" viewBox={`0 0 ${W} ${H}`} role="img" aria-label={`Armory Lockdown, round ${s.round}`} onMouseLeave={() => p.onHover(null)}>
      <defs>
        <pattern id="stripes" width="8" height="8" patternUnits="userSpaceOnUse" patternTransform="rotate(45)">
          <rect width="8" height="8" fill="var(--wall)" />
          <rect width="4" height="8" fill="var(--hazard)" opacity="0.75" />
        </pattern>
        <pattern id="hatch" width="7" height="7" patternUnits="userSpaceOnUse" patternTransform="rotate(45)">
          <rect width="2.5" height="7" fill="var(--danger)" opacity="0.45" />
        </pattern>
        <marker id="arrow-enemy" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="5" markerHeight="5" orient="auto-start-reverse">
          <path d="M0,0 L10,5 L0,10 z" fill="var(--danger)" />
        </marker>
        <marker id="arrow-minion" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="5" markerHeight="5" orient="auto-start-reverse">
          <path d="M0,0 L10,5 L0,10 z" fill="var(--player)" />
        </marker>
        <marker id="arrow-preview" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="5" markerHeight="5" orient="auto-start-reverse">
          <path d="M0,0 L10,5 L0,10 z" fill="var(--ink)" />
        </marker>
      </defs>

      {/* Coordinates */}
      {Array.from({ length: b.width }, (_, x) => (
        <g key={`cx${x}`} className="coord">
          <text x={cx(x)} y={MG - 7} textAnchor="middle" fontSize="12" fill="var(--ink-3)" fontFamily="var(--mono)">{String.fromCharCode(65 + x)}</text>
          <text x={cx(x)} y={H - 7} textAnchor="middle" fontSize="12" fill="var(--ink-3)" fontFamily="var(--mono)">{String.fromCharCode(65 + x)}</text>
        </g>
      ))}
      {Array.from({ length: b.height }, (_, y) => (
        <g key={`cy${y}`}>
          <text x={MG / 2} y={cy(y) + 4} textAnchor="middle" fontSize="12" fill="var(--ink-3)" fontFamily="var(--mono)">{y + 1}</text>
          <text x={W - MG / 2} y={cy(y) + 4} textAnchor="middle" fontSize="12" fill="var(--ink-3)" fontFamily="var(--mono)">{y + 1}</text>
        </g>
      ))}

      {/* Terrain */}
      {tiles.map((t) => (
        <Terrain key={`t${key(t)}`} id={b.terrainId(t)} t={t} />
      ))}

      {/* Highlights */}
      {p.highlights.map((h, i) =>
        h.tiles.map((t) => <HighlightTile key={`h${i}-${key(t)}`} t={t} kind={h.kind} label={h.label?.(t) ?? null} />),
      )}
      {p.hover && <rect x={MG + p.hover.x * TS + 1.5} y={MG + p.hover.y * TS + 1.5} width={TS - 3} height={TS - 3} rx={6} fill="none" stroke="var(--ink)" strokeWidth={2} opacity={0.55} pointerEvents="none" />}

      {/* Hazard cues: a cue and nothing more, never a danger zone */}
      {s.hazards.map((h) => (
        <HazardCue key={`hz${h.id}`} h={h} />
      ))}
      {/* Open decision: cooldowns are hidden from players unless rules.ui.showHazardCooldowns is set */}
      {c.rules.ui.showHazardCooldowns &&
        !p.debug?.hazards &&
        s.hazards
          .filter((h) => h.state === 'cooldown')
          .map((h) => (
            <text key={`hcd${h.id}`} x={cx(h.tiles[0]!.x)} y={MG + h.tiles[0]!.y * TS + 11} textAnchor="middle" fontSize="9" fill="var(--ink-2)" fontFamily="var(--mono)" pointerEvents="none">
              {`cooling ${h.cooldown}`}
            </text>
          ))}

      {/* Objects */}
      {s.objects.map((o) => (
        <ObjectGlyph key={`o${o.id}`} o={o} />
      ))}

      {/* Intents */}
      {p.showIntents && p.intents && !p.preview && <IntentLayer c={c} s={s} rep={p.intents} />}
      {p.preview?.intents && p.showIntents && <IntentLayer c={c} s={p.preview.state ?? s} rep={p.preview.intents} faint />}

      {/* Move path */}
      {p.movePath && p.movePath.length > 0 && (
        <polyline
          points={[s.units.find((u) => u.id === 0)?.pos, ...p.movePath].filter(Boolean).map((q) => `${cx(q!.x)},${cy(q!.y)}`).join(' ')}
          fill="none"
          stroke="var(--hatch)"
          strokeWidth={4}
          strokeLinecap="round"
          strokeLinejoin="round"
          strokeDasharray="2 8"
          pointerEvents="none"
        />
      )}

      {/* Units */}
      {units.map((u) => (
        <UnitGlyph key={`u${u.id}`} c={c} u={u} showId={!!p.debug?.ids} />
      ))}

      {/* Preview of the hovered action */}
      {p.preview && p.preview.ok && <PreviewLayer c={c} s={s} pv={p.preview} />}

      {/* Animation effects for the frame on screen */}
      <EffectLayer key={`fx${p.frameKey}`} events={p.frameEvents} s={s} />

      {/* Debug */}
      {p.debug && <DebugLayer c={c} s={s} dbg={p.debug} hazardAt={hazardAt} />}

      {/* Input layer */}
      {tiles.map((t) => (
        <rect
          key={`in${key(t)}`}
          x={MG + t.x * TS}
          y={MG + t.y * TS}
          width={TS}
          height={TS}
          fill="transparent"
          onPointerDown={(e) => (lastPointer.current = e.pointerType as 'mouse' | 'touch' | 'pen')}
          onMouseEnter={() => p.onHover(t)}
          onClick={() => p.onClick(t, lastPointer.current === 'touch')}
        >
          <title>{tileName(t)}</title>
        </rect>
      ))}
      {p.dimmed && <rect x={0} y={0} width={W} height={H} fill="var(--bg)" opacity={0.35} pointerEvents="none" />}
    </svg>
  );
}

// ------------------------------------------------------------------------------------------ terrain

function Terrain({ id, t }: { id: string; t: Pos }) {
  const x = MG + t.x * TS;
  const y = MG + t.y * TS;
  const base = <rect x={x} y={y} width={TS} height={TS} fill={(t.x + t.y) % 2 ? 'var(--tile-alt)' : 'var(--tile)'} />;
  switch (id) {
    case 'wall':
      return (
        <g>
          {base}
          <rect x={x} y={y} width={TS} height={TS} fill="var(--wall)" />
        </g>
      );
    case 'pillar':
      return (
        <g>
          {base}
          <rect x={x + 7} y={y + 7} width={TS - 14} height={TS - 14} rx={8} fill="var(--pillar)" />
          <rect x={x + 12} y={y + 12} width={TS - 24} height={TS - 24} rx={5} fill="none" stroke="var(--tile)" strokeOpacity={0.35} strokeWidth={2} />
        </g>
      );
    case 'shutter':
      return (
        <g>
          {base}
          <rect x={x + 2} y={y + 10} width={TS - 4} height={TS - 20} fill="url(#stripes)" />
          <text x={x + TS / 2} y={y + TS - 3} textAnchor="middle" fontSize="9" fill="var(--ink-3)" fontFamily="var(--mono)">SHUTTER</text>
        </g>
      );
    case 'gun':
      return (
        <g>
          {base}
          <rect x={x + 26} y={y + 12} width={TS - 30} height={TS - 24} rx={6} fill="var(--wall)" />
          <rect x={x + 6} y={y + TS / 2 - 5} width={26} height={10} rx={3} fill="var(--wall)" />
          <text x={x + TS - 6} y={y + TS - 4} textAnchor="end" fontSize="9" fill="var(--ink-3)" fontFamily="var(--mono)">GUN</text>
        </g>
      );
    case 'vent':
      return (
        <g>
          {base}
          <circle cx={x + TS / 2} cy={y + TS / 2} r={17} fill="none" stroke="var(--ink-3)" strokeWidth={2} />
          {[-8, -3, 2, 7].map((d) => (
            <line key={d} x1={x + TS / 2 - 12} x2={x + TS / 2 + 12} y1={y + TS / 2 + d} y2={y + TS / 2 + d} stroke="var(--ink-3)" strokeWidth={2} />
          ))}
        </g>
      );
    case 'panel':
      return (
        <g>
          {base}
          <rect x={x + 6} y={y + 6} width={TS - 12} height={TS - 12} rx={4} fill="none" stroke="var(--ink-3)" strokeWidth={2} strokeDasharray="6 3" />
          <path d={`M${x + 36} ${y + 15} L${x + 26} ${y + 33} L${x + 34} ${y + 33} L${x + 28} ${y + 49}`} fill="none" stroke="var(--ink-3)" strokeWidth={2} />
        </g>
      );
    default:
      return base;
  }
}

function HighlightTile({ t, kind, label }: { t: Pos; kind: Highlight['kind']; label: string | null }) {
  const x = MG + t.x * TS;
  const y = MG + t.y * TS;
  const style: Record<Highlight['kind'], { fill: string; stroke: string; dash?: string; op: number }> = {
    move: { fill: 'var(--hatch)', stroke: 'var(--hatch)', op: 0.16 },
    target: { fill: 'var(--target)', stroke: 'var(--hazard)', dash: '6 4', op: 0.22 },
    second: { fill: 'var(--minion)', stroke: 'var(--player)', dash: '6 4', op: 0.22 },
    chosen: { fill: 'var(--player)', stroke: 'var(--player)', op: 0.28 },
    spawn: { fill: 'none', stroke: 'var(--enemy)', dash: '3 3', op: 0 },
    debug: { fill: 'var(--hazard)', stroke: 'var(--hazard)', op: 0.18 },
  };
  const st = style[kind];
  return (
    <g pointerEvents="none">
      <rect x={x + 3} y={y + 3} width={TS - 6} height={TS - 6} rx={6} fill={st.fill} fillOpacity={st.op} stroke={st.stroke} strokeWidth={2} strokeDasharray={st.dash} strokeOpacity={0.85} />
      {label && (
        <text x={x + 6} y={y + 14} fontSize="10" fontFamily="var(--mono)" fill="var(--ink-2)">
          {label}
        </text>
      )}
    </g>
  );
}

// ------------------------------------------------------------------------------------------ hazards

function HazardCue({ h }: { h: Hazard }) {
  if (h.state !== 'primed') return null;
  return (
    <g pointerEvents="none" aria-label={`${h.id} cue`}>
      {h.tiles.map((t) => {
        const x = cx(t.x);
        const y = cy(t.y);
        if (h.def === 'gas_vent') {
          return (
            <g key={key(t)}>
              {[0, 0.6, 1.2].map((d) => (
                <circle key={d} cx={x} cy={y} r={6} fill="var(--poison)" opacity={0}>
                  <animate attributeName="cy" values={`${y};${y - 26}`} dur="1.8s" begin={`${d}s`} repeatCount="indefinite" />
                  <animate attributeName="opacity" values="0.55;0" dur="1.8s" begin={`${d}s`} repeatCount="indefinite" />
                  <animate attributeName="r" values="5;11" dur="1.8s" begin={`${d}s`} repeatCount="indefinite" />
                </circle>
              ))}
            </g>
          );
        }
        if (h.def === 'wall_gun') {
          return (
            <circle key={key(t)} cx={x - 24} cy={y} r={7} fill="var(--hazard)">
              <animate attributeName="opacity" values="0.25;1;0.25" dur="1.1s" repeatCount="indefinite" />
              <animate attributeName="r" values="5;9;5" dur="1.1s" repeatCount="indefinite" />
            </circle>
          );
        }
        return (
          <polyline key={key(t)} points={`${x - 20},${y + 6} ${x - 10},${y - 8} ${x - 2},${y + 4} ${x + 8},${y - 10} ${x + 18},${y + 6}`} fill="none" stroke="var(--target)" strokeWidth={3} strokeLinejoin="round">
            <animate attributeName="opacity" values="1;0.1;0.8;0;1" dur="0.9s" repeatCount="indefinite" />
          </polyline>
        );
      })}
    </g>
  );
}

// ------------------------------------------------------------------------------------------ objects

function ObjectGlyph({ o }: { o: GameObject }) {
  const x = cx(o.pos.x);
  const y = cy(o.pos.y);
  switch (o.kind) {
    case 'corpse':
      return (
        <g pointerEvents="none" opacity={0.75}>
          <path d={`M${x - 12} ${y - 12} L${x + 12} ${y + 12} M${x + 12} ${y - 12} L${x - 12} ${y + 12}`} stroke="var(--ink-3)" strokeWidth={5} strokeLinecap="round" />
          <text x={x + 20} y={y + 24} textAnchor="end" fontSize="11" fontFamily="var(--mono)" fill="var(--ink-2)">
            {o.decay}
          </text>
          <title>{`${o.of} corpse, ${o.decay} round(s) left`}</title>
        </g>
      );
    case 'mine':
      return (
        <g pointerEvents="none">
          <circle cx={x} cy={y} r={10} fill="#222" stroke="var(--target)" strokeWidth={3} />
          <circle cx={x} cy={y} r={3} fill="var(--danger)" />
          <title>Your mine</title>
        </g>
      );
    case 'egg':
      return (
        <g pointerEvents="none">
          <ellipse cx={x} cy={y} rx={15} ry={19} fill="color-mix(in srgb, var(--hatch) 55%, var(--panel))" stroke="var(--hatch)" strokeWidth={2.5} />
          <text x={x} y={y + 5} textAnchor="middle" fontSize="14" fontWeight={700} fill="var(--ink)" fontFamily="var(--mono)">
            {o.timer}
          </text>
          <text x={x + 26} y={y + 27} textAnchor="end" fontSize="10" fill="var(--ink-2)" fontFamily="var(--mono)">
            {o.hp}hp
          </text>
          <title>{`Egg: hatches into ${o.hatchInto} in ${o.timer} round(s); ${o.hp} HP`}</title>
        </g>
      );
    case 'shield':
      return (
        <g pointerEvents="none">
          <rect x={x - 26} y={y - 26} width={52} height={52} rx={6} fill="var(--player)" fillOpacity={0.22} stroke="var(--player)" strokeWidth={3} />
          <path d={`M${x} ${y - 13} L${x + 11} ${y - 8} L${x + 9} ${y + 6} L${x} ${y + 13} L${x - 9} ${y + 6} L${x - 11} ${y - 8} Z`} fill="var(--player)" opacity={0.7} />
          <text x={x + 23} y={y + 23} textAnchor="end" fontSize="11" fontWeight={700} fill="var(--ink)" fontFamily="var(--mono)">
            {o.hp}
          </text>
          <title>{`Shield segment, ${o.hp} HP`}</title>
        </g>
      );
  }
}

// ------------------------------------------------------------------------------------------ units

function Shape({ def, r, color, stroke }: { def: string; r: number; color: string; stroke?: string }) {
  if (def === 'guard' || def === 'medic' || def === 'flamer') return <polygon points={`0,${-r} ${r},0 0,${r} ${-r},0`} fill={color} stroke={stroke} strokeWidth={2} />;
  if (def === 'warden') {
    const pts = Array.from({ length: 6 }, (_, i) => `${Math.cos((Math.PI / 3) * i) * r},${Math.sin((Math.PI / 3) * i) * r}`).join(' ');
    return <polygon points={pts} fill={color} stroke={stroke} strokeWidth={2.5} />;
  }
  if (def === 'turret') return <rect x={-r} y={-r} width={r * 2} height={r * 2} rx={7} fill={color} stroke={stroke} strokeWidth={2} />;
  if (def === 'drone')
    return (
      <g>
        {[
          [-1, -1],
          [1, -1],
          [-1, 1],
          [1, 1],
        ].map(([a, b2]) => (
          <circle key={`${a}${b2}`} cx={a! * r * 0.95} cy={b2! * r * 0.95} r={r * 0.35} fill="none" stroke={color} strokeWidth={2} />
        ))}
        <circle r={r * 0.75} fill={color} stroke={stroke} strokeWidth={2} />
      </g>
    );
  if (def === 'burster') return <circle r={r} fill={color} stroke="var(--danger)" strokeWidth={3} strokeDasharray="4 3" />;
  return <circle r={r} fill={color} stroke={stroke} strokeWidth={def === 'player' ? 3 : 2} />;
}

export function UnitGlyph({ c, u, showId, ghost }: { c: Content; u: Unit; showId?: boolean; ghost?: boolean }) {
  const def = c.units[u.def]!;
  const r = u.def === 'warden' ? 25 : u.def === 'player' ? 21 : u.def === 'hatchling' ? 14 : u.def === 'drone' ? 17 : 18;
  const color = UNIT_COLOR[u.def] ?? (u.team === 'enemy' ? 'var(--enemy)' : 'var(--minion)');
  const armor = Math.max(0, u.armor - (u.statuses.corrode?.stacks ?? 0));
  const badges: { t: string; c: string }[] = [];
  if (u.statuses.poison) badges.push({ t: `P${u.statuses.poison.remaining}`, c: 'var(--poison)' });
  if (u.statuses.parasite) badges.push({ t: `X${u.statuses.parasite.remaining}`, c: 'var(--parasite)' });
  if (u.statuses.corrode) badges.push({ t: `C${u.statuses.corrode.stacks}`, c: 'var(--corrode)' });
  if (u.statuses.pinned) badges.push({ t: 'PIN', c: 'var(--ink-2)' });
  if (u.statuses.slowed) badges.push({ t: 'SLW', c: 'var(--ink-2)' });
  const hpw = 42;
  return (
    <g
      style={{ transform: `translate(${cx(u.pos.x)}px, ${cy(u.pos.y)}px)`, transition: ghost ? undefined : 'transform 0.28s ease-in-out' }}
      opacity={ghost ? 0.45 : 1}
      pointerEvents="none"
      className={ghost ? undefined : 'unit-in'}
    >
      {u.flying && <ellipse cx={0} cy={22} rx={13} ry={3.5} fill="#000" opacity={0.18} />}
      <g transform={u.flying ? 'translate(0,-4)' : undefined}>
        <Shape def={u.def} r={r} color={color} stroke={u.team === 'player' ? 'var(--panel)' : 'var(--panel)'} />
        <text y={u.def === 'medic' ? 7 : 6} textAnchor="middle" fontSize={u.def === 'medic' ? 22 : 17} fontWeight={800} fill="#fff" fontFamily="var(--mono)">
          {UNIT_LETTER[u.def] ?? u.def[0]}
        </text>
      </g>
      {/* HP bar */}
      <rect x={-hpw / 2} y={24} width={hpw} height={5} rx={2.5} fill="var(--panel)" stroke="var(--line)" strokeWidth={0.5} />
      <rect x={-hpw / 2} y={24} width={(hpw * Math.max(0, u.hp)) / u.maxHp} height={5} rx={2.5} fill={u.hp <= u.maxHp / 3 ? 'var(--danger)' : 'var(--good)'} />
      <text x={hpw / 2 + 2} y={30} fontSize="9" fill="var(--ink-2)" fontFamily="var(--mono)">
        {u.hp}
      </text>
      {/* Armor */}
      {(def.armor > 0 || armor > 0) && (
        <g transform="translate(22,-22)">
          <path d="M0,-8 L7,-5 L6,4 L0,9 L-6,4 L-7,-5 Z" fill="var(--panel)" stroke="var(--ink)" strokeWidth={1.5} />
          <text y={3.5} textAnchor="middle" fontSize="9" fontWeight={700} fill="var(--ink)" fontFamily="var(--mono)">
            {armor}
          </text>
        </g>
      )}
      {/* Ammo and charges */}
      {u.maxAmmo !== undefined && (
        <g transform="translate(-22,-27)">
          {Array.from({ length: u.maxAmmo }, (_, i) => (
            <rect key={i} x={i * 6} y={0} width={4} height={6} rx={1} fill={i < (u.ammo ?? 0) ? 'var(--ink)' : 'none'} stroke="var(--ink)" strokeWidth={1} />
          ))}
        </g>
      )}
      {u.maxCharges !== undefined && (
        <g transform="translate(-22,-27)">
          {Array.from({ length: u.maxCharges }, (_, i) => (
            <circle key={i} cx={i * 7 + 3} cy={3} r={2.6} fill={i < (u.charges ?? 0) ? 'var(--target)' : 'none'} stroke="var(--ink)" strokeWidth={1} />
          ))}
          {u.droneState === 'returning' && (
            <text x={0} y={-3} fontSize="8" fill="var(--ink-2)" fontFamily="var(--mono)">
              RTN
            </text>
          )}
        </g>
      )}
      {/* Statuses */}
      {badges.length > 0 && (
        <g transform={`translate(${-((badges.length - 1) * 24) / 2},-34)`}>
          {badges.map((bd, i) => (
            <g key={bd.t} transform={`translate(${i * 24},0)`}>
              <rect x={-11} y={-7} width={22} height={13} rx={4} fill={bd.c} />
              <text y={3} textAnchor="middle" fontSize="8.5" fontWeight={700} fill="#fff" fontFamily="var(--mono)">
                {bd.t}
              </text>
            </g>
          ))}
        </g>
      )}
      {showId && (
        <text x={-27} y={-14} fontSize="10" fontWeight={700} fill="var(--ink)" fontFamily="var(--mono)">
          #{u.id}
        </text>
      )}
      <title>{`${u.def === 'player' ? 'You' : `${def.name} ${u.id}`}: ${u.hp}/${u.maxHp} HP${def.armor ? `, armor ${armor}` : ''}`}</title>
    </g>
  );
}

// ------------------------------------------------------------------------------------------ previews and intents

function lineEl(l: LinePreview, i: number, color: string, marker: string, width = 2.5, dash?: string) {
  if (l.kind === 'chain') {
    const mx = (cx(l.from.x) + cx(l.to.x)) / 2 + 6;
    const my = (cy(l.from.y) + cy(l.to.y)) / 2 - 6;
    return <polyline key={i} points={`${cx(l.from.x)},${cy(l.from.y)} ${mx},${my} ${cx(l.to.x)},${cy(l.to.y)}`} fill="none" stroke="var(--target)" strokeWidth={2.5} strokeDasharray="4 2" />;
  }
  return (
    <line
      key={i}
      x1={cx(l.from.x)}
      y1={cy(l.from.y)}
      x2={cx(l.to.x)}
      y2={cy(l.to.y)}
      stroke={l.kind === 'reflect' ? 'var(--parasite)' : l.kind === 'charge' ? 'var(--danger)' : color}
      strokeWidth={l.kind === 'charge' ? 5 : width}
      strokeDasharray={dash}
      markerEnd={`url(#${marker})`}
      opacity={0.9}
    />
  );
}

function HitBadge({ h, x, y, small }: { h: HitPreview; x: number; y: number; small?: boolean }) {
  const parts: ReactNode[] = [];
  let off = 0;
  const pill = (text: string, fill: string, ink = '#fff') => {
    const w = text.length * 6.6 + 8;
    const el = (
      <g key={text + off} transform={`translate(${off},0)`}>
        <rect x={0} y={-8} width={w} height={15} rx={5} fill={fill} stroke="var(--panel)" strokeWidth={1} />
        <text x={w / 2} y={3.5} textAnchor="middle" fontSize="10.5" fontWeight={800} fill={ink} fontFamily="var(--mono)">
          {text}
        </text>
      </g>
    );
    off += w + 2;
    parts.push(el);
  };
  if (h.damage > 0 || h.absorbed > 0) pill(h.damage > 0 ? `-${h.damage}` : '0', h.damage > 0 ? 'var(--danger)' : 'var(--ink-3)');
  if (h.healed > 0) pill(`+${h.healed}`, 'var(--good)');
  for (const st of h.statuses) pill(st === 'poison' ? '+PSN' : st === 'parasite' ? '+PAR' : st === 'corrode' ? '+COR' : st === 'pinned' ? 'PIN' : 'SLW', st === 'poison' ? 'var(--poison)' : st === 'parasite' ? 'var(--parasite)' : st === 'corrode' ? 'var(--corrode)' : 'var(--ink-2)');
  if (h.killed) pill('KO', 'var(--ink)', 'var(--bg)');
  if (!parts.length) return null;
  return <g transform={`translate(${x - off / 2},${y - (small ? 8 : 12)}) scale(${small ? 0.85 : 1})`}>{parts}</g>;
}

function FriendlyMark({ x, y }: { x: number; y: number }) {
  return (
    <g pointerEvents="none">
      <circle cx={x} cy={y} r={27} fill="none" stroke="var(--danger)" strokeWidth={3.5} strokeDasharray="5 3" />
      <g transform={`translate(${x + 20},${y + 18})`}>
        <path d="M0,-9 L9,7 L-9,7 Z" fill="var(--danger)" stroke="var(--panel)" strokeWidth={1.5} />
        <text y={5} textAnchor="middle" fontSize="10" fontWeight={900} fill="#fff">
          !
        </text>
      </g>
    </g>
  );
}

function PreviewLayer({ c, s, pv }: { c: Content; s: GameState; pv: CommandPreview }) {
  const after = pv.state;
  return (
    <g pointerEvents="none">
      {pv.areaTiles.map((t) => (
        <rect key={`a${key(t)}`} x={MG + t.x * TS + 2} y={MG + t.y * TS + 2} width={TS - 4} height={TS - 4} rx={5} fill="var(--hazard)" fillOpacity={0.16} stroke="var(--hazard)" strokeWidth={2} strokeDasharray="5 3" />
      ))}
      {pv.lines.map((l, i) => lineEl(l, i, 'var(--ink)', 'arrow-preview', 2.5))}
      {pv.moves.map((m) => (
        <polyline key={`m${m.id}`} points={[m.from, ...m.path].map((q) => `${cx(q.x)},${cy(q.y)}`).join(' ')} fill="none" stroke="var(--ink)" strokeWidth={2.5} strokeDasharray="3 5" />
      ))}
      {pv.moves.map((m) => {
        const u = after?.units.find((x) => x.id === m.id) ?? s.units.find((x) => x.id === m.id);
        return u ? <UnitGlyph key={`g${m.id}`} c={c} u={{ ...u, pos: m.to }} ghost /> : null;
      })}
      {pv.placed.map((o) => (
        <g key={`p${o.id}`} opacity={0.5}>
          <ObjectGlyph o={o} />
        </g>
      ))}
      {pv.spawned.map((u) => (
        <UnitGlyph key={`sp${u.id}`} c={c} u={u} ghost />
      ))}
      {pv.hits.map((h) => {
        const pos = h.ref.kind === 'unit' ? after?.units.find((u) => u.id === h.ref.id)?.pos ?? h.pos : h.pos;
        return (
          <g key={`hit${h.ref.kind}${h.ref.id}`}>
            {h.friendly && <FriendlyMark x={cx(pos.x)} y={cy(pos.y)} />}
            <HitBadge h={h} x={cx(pos.x)} y={cy(pos.y) + 2} />
          </g>
        );
      })}
    </g>
  );
}

function IntentLayer({ c, s, rep, faint }: { c: Content; s: GameState; rep: IntentReport; faint?: boolean }) {
  void c;
  const incoming = new Map<string, { pos: Pos; dmg: number; killed: boolean }>();
  for (const it of rep.intents) {
    for (const h of it.hits) {
      if (h.damage <= 0 && !h.killed) continue;
      const k = `${h.ref.kind}${h.ref.id}`;
      const cur = incoming.get(k) ?? { pos: h.pos, dmg: 0, killed: false };
      cur.dmg += h.damage;
      cur.killed ||= h.killed;
      incoming.set(k, cur);
    }
  }
  return (
    <g pointerEvents="none" opacity={faint ? 0.55 : 0.95}>
      {rep.intents.map((it) =>
        it.tiles.map((t) => <rect key={`it${it.id}${key(t)}`} x={MG + t.x * TS + 3} y={MG + t.y * TS + 3} width={TS - 6} height={TS - 6} fill="url(#hatch)" />),
      )}
      {rep.intents.map((it) =>
        it.path.length ? (
          <g key={`ip${it.id}`}>
            <polyline
              points={[it.from, ...it.path].map((q) => `${cx(q.x)},${cy(q.y)}`).join(' ')}
              fill="none"
              stroke={it.team === 'enemy' ? 'var(--enemy)' : 'var(--player)'}
              strokeWidth={2}
              strokeDasharray="5 4"
              opacity={0.85}
            />
            <circle cx={cx(it.to.x)} cy={cy(it.to.y)} r={it.arrival ? 9 : 5} fill={it.arrival ? 'none' : it.team === 'enemy' ? 'var(--enemy)' : 'var(--player)'} stroke={it.team === 'enemy' ? 'var(--enemy)' : 'var(--player)'} strokeWidth={2} />
          </g>
        ) : null,
      )}
      {rep.intents.map((it) =>
        it.arrival
          ? null
          : it.lines.map((l, i) => (
              <g key={`il${it.id}-${i}`}>{lineEl(l, i, it.team === 'enemy' ? 'var(--danger)' : 'var(--player)', it.team === 'enemy' ? 'arrow-enemy' : 'arrow-minion', 1.8, l.kind === 'shot' ? '1 0' : undefined)}</g>
            )),
      )}
      {rep.intents
        .filter((it) => it.diesFirst)
        .map((it) => (
          <text key={`df${it.id}`} x={cx(it.from.x) + 14} y={cy(it.from.y) - 14} fontSize="13" fontWeight={900} fill="var(--danger)">
            ✕
          </text>
        ))}
      {[...incoming.entries()].map(([k, v]) => {
        const u = s.units.find((x) => `unit${x.id}` === k);
        const pos = u?.pos ?? v.pos;
        return (
          <g key={`in${k}`} transform={`translate(${cx(pos.x) - 31},${cy(pos.y) - 23})`}>
            <rect x={0} y={-8} width={v.killed ? 38 : 26} height={15} rx={5} fill="var(--panel)" stroke="var(--danger)" strokeWidth={1.5} />
            <text x={4} y={3.5} fontSize="10.5" fontWeight={800} fill="var(--danger)" fontFamily="var(--mono)">
              {`▼${v.dmg}${v.killed ? ' KO' : ''}`}
            </text>
            <title>{`Incoming this round: ${v.dmg} damage${v.killed ? ', lethal' : ''}`}</title>
          </g>
        );
      })}
    </g>
  );
}

// ------------------------------------------------------------------------------------------ animation effects

function EffectLayer({ events, s }: { events: GameEvent[]; s: GameState }) {
  const out: ReactNode[] = [];
  events.forEach((e, i) => {
    switch (e.t) {
      case 'ProjectileFired': {
        const end = e.path[e.path.length - 1];
        if (!end) break;
        out.push(<line key={i} className="fx-shot" x1={cx(e.from.x)} y1={cy(e.from.y)} x2={cx(end.x)} y2={cy(end.y)} stroke={e.source.side === 'enemy' ? 'var(--danger)' : e.source.side === 'hazard' ? 'var(--hazard)' : 'var(--player)'} strokeWidth={3} />);
        break;
      }
      case 'ChainArc':
        out.push(<line key={i} className="fx-shot" x1={cx(e.from.x)} y1={cy(e.from.y)} x2={cx(e.to.x)} y2={cy(e.to.y)} stroke="var(--target)" strokeWidth={3} strokeDasharray="4 2" />);
        break;
      case 'AreaEffect': {
        const fill = e.kind === 'blast' ? 'var(--hazard)' : e.kind === 'sweep' ? 'var(--danger)' : e.kind === 'panel' ? 'var(--target)' : 'var(--poison)';
        e.tiles.forEach((t, j) => out.push(<rect key={`${i}-${j}`} className="fx-flash" x={MG + t.x * TS + 2} y={MG + t.y * TS + 2} width={TS - 4} height={TS - 4} rx={6} fill={fill} />));
        break;
      }
      case 'DamageDealt':
        out.push(
          <text key={i} className="fx-float" x={cx(e.pos.x)} y={cy(e.pos.y) - 6} textAnchor="middle" fontSize="18" fontWeight={900} fill={e.amount > 0 ? 'var(--danger)' : 'var(--ink-3)'} stroke="var(--panel)" strokeWidth={3} paintOrder="stroke" fontFamily="var(--mono)">
            {e.amount > 0 ? `-${e.amount}` : '0'}
          </text>,
        );
        break;
      case 'Healed': {
        const u = s.units.find((x) => x.id === e.target);
        if (!u) break;
        out.push(
          <text key={i} className="fx-float" x={cx(u.pos.x)} y={cy(u.pos.y) - 6} textAnchor="middle" fontSize="18" fontWeight={900} fill="var(--good)" stroke="var(--panel)" strokeWidth={3} paintOrder="stroke" fontFamily="var(--mono)">
            {`+${e.amount}`}
          </text>,
        );
        break;
      }
      case 'UnitDied':
        out.push(
          <text key={i} className="fx-fade" x={cx(e.pos.x)} y={cy(e.pos.y) + 7} textAnchor="middle" fontSize="24" fontWeight={900} fill="var(--ink-2)">
            ✕
          </text>,
        );
        break;
      case 'Charged':
        out.push(<line key={i} className="fx-shot" x1={cx(e.from.x)} y1={cy(e.from.y)} x2={cx(e.to.x)} y2={cy(e.to.y)} stroke="var(--danger)" strokeWidth={8} strokeLinecap="round" />);
        break;
    }
  });
  return <g pointerEvents="none">{out}</g>;
}

// ------------------------------------------------------------------------------------------ debug

function DebugLayer({ c, dbg, hazardAt }: { c: Content; s: GameState; dbg: BoardDebug; hazardAt: Map<string, Hazard> }) {
  void c;
  return (
    <g pointerEvents="none">
      {dbg.ids &&
        boardFor(c.map)
          .allTiles()
          .map((t) => (
            <text key={`d${key(t)}`} x={MG + t.x * TS + 3} y={MG + (t.y + 1) * TS - 3} fontSize="8.5" fill="var(--ink-3)" fontFamily="var(--mono)">
              {tileName(t)}
            </text>
          ))}
      {dbg.spawns.map((sp, i) => (
        <g key={`sp${i}`}>
          <rect x={MG + sp.tile.x * TS + 5} y={MG + sp.tile.y * TS + 5} width={TS - 10} height={TS - 10} rx={4} fill="none" stroke="var(--enemy)" strokeWidth={1.5} strokeDasharray="3 3" />
          <text x={MG + sp.tile.x * TS + TS - 8} y={MG + sp.tile.y * TS + 15} textAnchor="end" fontSize="9" fill="var(--enemy)" fontFamily="var(--mono)">
            W{sp.wave}
          </text>
        </g>
      ))}
      {dbg.hazards &&
        [...hazardAt.entries()].map(([k, h]) => {
          const [x, y] = k.split(',').map(Number) as [number, number];
          const txt = h.state === 'cooldown' ? `cd${h.cooldown}` : h.state === 'primed' ? 'PRIMED' : 'ready';
          return (
            <text key={`hd${k}`} x={MG + x * TS + TS / 2} y={MG + y * TS + 11} textAnchor="middle" fontSize="9" fontWeight={700} fill="var(--hazard)" fontFamily="var(--mono)">
              {txt}
            </text>
          );
        })}
      {dbg.flamer?.map((o, i) => (
        <text key={`fl${i}`} x={cx(o.tile.x) + o.dir.x * 18} y={cy(o.tile.y) + o.dir.y * 18 + 3} textAnchor="middle" fontSize="10" fontWeight={800} fill={o.score > 0 ? 'var(--danger)' : 'var(--ink-3)'} fontFamily="var(--mono)">
          {o.score}
        </text>
      ))}
    </g>
  );
}
