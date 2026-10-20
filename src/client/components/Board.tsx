import { useRef, type CSSProperties, type ReactNode } from 'react';
import type { Content } from '../../content/types';
import type { CommandPreview, HitPreview, IntentReport, LinePreview } from '../../preview/preview';
import { boardFor } from '../../state/grid';
import type { AIOption, GameEvent, GameObject, GameState, Hazard, Pos, Unit } from '../../state/types';
import { tileName } from '../../util/tiles';
import { FxLayer } from '../art/fx';
import { MG, TS, cx, cy, posKey as key } from '../art/geometry';
import { IconGlyph } from '../art/icons';
import { ObjectArt } from '../art/objects';
import { Room } from '../art/terrain';
import { UnitBase, UnitFigure, baseShapeFor, unitColor } from '../art/units';

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
  /** Animation speed: effects play faster at 2x. */
  speed?: 0 | 1 | 2;
  dimmed?: boolean;
  overlay?: ReactNode;
}

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
  // Units hit in the frame on screen flash and flinch.
  const hit = new Set<number>();
  for (const e of p.frameEvents) if (e.t === 'DamageDealt' && e.target.kind === 'unit' && e.amount > 0) hit.add(e.target.id);
  const fxScale = p.speed === 2 ? 0.55 : 1;

  return (
    <svg
      className="board-svg"
      viewBox={`0 0 ${W} ${H}`}
      role="img"
      aria-label={`${c.map.name}, round ${s.round}`}
      onMouseLeave={() => p.onHover(null)}
      style={{ '--fx': fxScale } as CSSProperties}
    >
      <Room map={c.map} />

      {/* Highlights */}
      {p.highlights.map((h, i) =>
        h.tiles.map((t) => <HighlightTile key={`h${i}-${key(t)}`} t={t} kind={h.kind} label={h.label?.(t) ?? null} />),
      )}
      {p.hover && <Brackets x={MG + p.hover.x * TS} y={MG + p.hover.y * TS} color="var(--ink)" inset={2} len={14} width={2.5} opacity={0.85} />}

      {/* Hazard cues: a cue and nothing more, never a danger zone */}
      {s.hazards.map((h) => (
        <HazardCue key={`hz${h.id}`} h={h} />
      ))}
      {/* Cooldowns are hidden from players (spec, Hazard cues) unless rules.ui.showHazardCooldowns is set */}
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
        <g pointerEvents="none">
          <polyline
            points={[s.units.find((u) => u.id === 0)?.pos, ...p.movePath].filter(Boolean).map((q) => `${cx(q!.x)},${cy(q!.y)}`).join(' ')}
            fill="none"
            stroke="var(--move)"
            strokeWidth={4}
            strokeLinecap="round"
            strokeLinejoin="round"
            strokeDasharray="2 9"
          />
          {(() => {
            const end = p.movePath[p.movePath.length - 1]!;
            return <circle cx={cx(end.x)} cy={cy(end.y) + 18} r={8} fill="none" stroke="var(--move)" strokeWidth={2.5} />;
          })()}
        </g>
      )}

      {/* Units, in a stable order: reordering the elements would cancel a moving unit's slide */}
      {units.map((u) => (
        <UnitToken key={`u${u.id}`} c={c} u={u} showId={!!p.debug?.ids} hit={hit.has(u.id)} />
      ))}

      {/* Preview of the hovered action */}
      {p.preview && p.preview.ok && <PreviewLayer c={c} s={s} pv={p.preview} />}

      {/* Combat effects for the frames on screen */}
      <FxLayer frameKey={p.frameKey} events={p.frameEvents} s={s} scale={fxScale} />

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

// ------------------------------------------------------------------------------------------ highlights

/** Four corner brackets around a tile, the targeting reticle used for hover and highlights. */
function Brackets({ x, y, color, inset = 4, len = 12, width = 2.5, opacity = 1, dash }: { x: number; y: number; color: string; inset?: number; len?: number; width?: number; opacity?: number; dash?: string }) {
  const a = inset;
  const z = TS - inset;
  const d = [
    `M${x + a} ${y + a + len} V${y + a} H${x + a + len}`,
    `M${x + z - len} ${y + a} H${x + z} V${y + a + len}`,
    `M${x + z} ${y + z - len} V${y + z} H${x + z - len}`,
    `M${x + a + len} ${y + z} H${x + a} V${y + z - len}`,
  ].join(' ');
  return <path d={d} fill="none" stroke={color} strokeWidth={width} strokeLinecap="square" opacity={opacity} strokeDasharray={dash} pointerEvents="none" />;
}

function HighlightTile({ t, kind, label }: { t: Pos; kind: Highlight['kind']; label: string | null }) {
  const x = MG + t.x * TS;
  const y = MG + t.y * TS;
  const style: Record<Highlight['kind'], { fill: string; stroke: string; dash?: string; op: number }> = {
    move: { fill: 'var(--move)', stroke: 'var(--move)', op: 0.14 },
    target: { fill: 'var(--target)', stroke: 'var(--target)', dash: '5 3', op: 0.2 },
    second: { fill: 'var(--minion)', stroke: 'var(--player)', dash: '5 3', op: 0.22 },
    chosen: { fill: 'var(--player)', stroke: 'var(--player)', op: 0.3 },
    spawn: { fill: 'none', stroke: 'var(--enemy)', dash: '3 3', op: 0 },
    debug: { fill: 'var(--hazard)', stroke: 'var(--hazard)', op: 0.18 },
  };
  const st = style[kind];
  return (
    <g pointerEvents="none" className={kind === 'target' ? 'hl-pulse' : undefined}>
      <rect x={x + 4} y={y + 4} width={TS - 8} height={TS - 8} rx={3} fill={st.fill} fillOpacity={st.op} stroke={st.stroke} strokeWidth={1} strokeOpacity={0.35} strokeDasharray={st.dash} />
      <Brackets x={x} y={y} color={st.stroke} inset={5} len={11} width={2.5} />
      {label && (
        <text x={x + 7} y={y + 16} fontSize="10" fontFamily="var(--mono)" fill="var(--ink-2)">
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
                  <animate attributeName="cy" values={`${y};${y - 30}`} dur="1.8s" begin={`${d}s`} repeatCount="indefinite" />
                  <animate attributeName="opacity" values="0.6;0" dur="1.8s" begin={`${d}s`} repeatCount="indefinite" />
                  <animate attributeName="r" values="5;12" dur="1.8s" begin={`${d}s`} repeatCount="indefinite" />
                </circle>
              ))}
            </g>
          );
        }
        if (h.def === 'wall_gun') {
          return (
            <g key={key(t)}>
              <circle cx={x - 26} cy={y} r={16} fill="url(#lx-glow-amber)">
                <animate attributeName="opacity" values="0.3;1;0.3" dur="1.1s" repeatCount="indefinite" />
              </circle>
              <circle cx={x - 26} cy={y} r={4} fill="#ffe08a">
                <animate attributeName="r" values="3;5.5;3" dur="1.1s" repeatCount="indefinite" />
              </circle>
            </g>
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
  const title =
    o.kind === 'corpse' ? `${o.of} corpse, ${o.decay} round(s) left` : o.kind === 'mine' ? 'Your mine' : o.kind === 'egg' ? `Egg: hatches into ${o.hatchInto} in ${o.timer} round(s); ${o.hp} HP` : `Shield segment, ${o.hp} HP`;
  return (
    <g pointerEvents="none" transform={`translate(${cx(o.pos.x)},${cy(o.pos.y)})`}>
      <ObjectArt o={o} />
      <title>{title}</title>
    </g>
  );
}

// ------------------------------------------------------------------------------------------ units

const STATUS_COLOR: Record<string, string> = { poison: 'var(--poison)', parasite: 'var(--parasite)', corrode: 'var(--corrode)', pinned: 'var(--status-neutral)', slowed: 'var(--status-neutral)' };

function HpBar({ hp, max }: { hp: number; max: number }) {
  const w = 40;
  const low = hp <= max / 3;
  const color = low ? 'var(--danger)' : 'var(--good)';
  const shown = Math.max(0, hp);
  return (
    <g transform="translate(0,28)">
      <rect x={-w / 2 - 1.5} y={-1.5} width={w + 3} height={7} rx={2} fill="var(--chip-bg)" stroke="var(--art-outline)" strokeWidth={1} />
      {max <= 10 ? (
        Array.from({ length: max }, (_, i) => {
          const sw = (w - (max - 1)) / max;
          return <rect key={i} x={-w / 2 + i * (sw + 1)} y={0} width={sw} height={4} rx={0.8} fill={i < shown ? color : 'var(--hp-empty)'} />;
        })
      ) : (
        <>
          <rect x={-w / 2} y={0} width={(w * shown) / max} height={4} rx={1} fill={color} />
          {Array.from({ length: Math.floor((max - 1) / 5) }, (_, i) => (
            <line key={i} x1={-w / 2 + (w * (i + 1) * 5) / max} x2={-w / 2 + (w * (i + 1) * 5) / max} y1={0} y2={4} stroke="var(--chip-bg)" strokeWidth={1} />
          ))}
        </>
      )}
      <text x={w / 2 + 4} y={4.6} fontSize="9" fontWeight={700} fill="var(--ink)" stroke="var(--chip-bg)" strokeWidth={2.5} paintOrder="stroke" fontFamily="var(--mono)">
        {hp}
      </text>
    </g>
  );
}

export function UnitToken({ c, u, showId, ghost, hit }: { c: Content; u: Unit; showId?: boolean; ghost?: boolean; hit?: boolean }) {
  const def = c.units[u.def]!;
  const color = unitColor(u.def, u.team);
  const armor = Math.max(0, u.armor - (u.statuses.corrode?.stacks ?? 0));
  const badges: { icon: string; n: string; c: string }[] = [];
  if (u.statuses.poison) badges.push({ icon: 'poison', n: `${u.statuses.poison.remaining}`, c: STATUS_COLOR.poison! });
  if (u.statuses.parasite) badges.push({ icon: 'parasite', n: `${u.statuses.parasite.remaining}`, c: STATUS_COLOR.parasite! });
  if (u.statuses.corrode) badges.push({ icon: 'corrode', n: `${u.statuses.corrode.stacks}`, c: STATUS_COLOR.corrode! });
  if (u.statuses.pinned) badges.push({ icon: 'pinned', n: '', c: STATUS_COLOR.pinned! });
  if (u.statuses.slowed) badges.push({ icon: 'slowed', n: '', c: STATUS_COLOR.slowed! });
  const small = u.def === 'hatchling' || u.def === 'spitter' || u.def === 'burster';
  return (
    <g
      style={{ transform: `translate(${cx(u.pos.x)}px, ${cy(u.pos.y)}px)`, transition: ghost ? undefined : 'transform 0.28s ease-in-out' }}
      opacity={ghost ? 0.45 : 1}
      pointerEvents="none"
      className={ghost ? undefined : 'unit-in'}
    >
      <UnitBase shape={baseShapeFor(u.def, u.team)} color={color} small={small} />
      <g transform={u.flying ? 'translate(0,-8)' : undefined}>
        <g className={ghost ? undefined : `art-idle${hit ? ' art-hit' : ''}`} style={ghost ? undefined : { animationDelay: `${-((u.id * 0.37) % 2.4)}s` }}>
          <UnitFigure def={u.def} />
        </g>
      </g>
      {!ghost && <HpBar hp={u.hp} max={u.maxHp} />}
      {/* Armor */}
      {(def.armor > 0 || armor > 0) && (
        <g transform="translate(23,-22)">
          <path d="M0,-9 L8,-5.5 L7,4 L0,10 L-7,4 L-8,-5.5 Z" fill="var(--chip-bg)" stroke="var(--ink)" strokeWidth={1.5} />
          <text y={3.8} textAnchor="middle" fontSize="10" fontWeight={800} fill="var(--ink)" fontFamily="var(--mono)">
            {armor}
          </text>
        </g>
      )}
      {/* Ammo and charges */}
      {u.maxAmmo !== undefined && (
        <g transform="translate(-24,-28)">
          {Array.from({ length: u.maxAmmo }, (_, i) => (
            <rect key={i} x={i * 6} y={0} width={4} height={7} rx={1} fill={i < (u.ammo ?? 0) ? '#e8c35a' : 'var(--chip-bg)'} stroke="var(--art-outline)" strokeWidth={1} />
          ))}
        </g>
      )}
      {u.maxCharges !== undefined && (
        <g transform="translate(-24,-28)">
          {Array.from({ length: u.maxCharges }, (_, i) => (
            <path key={i} d={`M${i * 8 + 4} 0 L${i * 8} 5 L${i * 8 + 3} 5 L${i * 8 + 2} 9 L${i * 8 + 7} 3.5 L${i * 8 + 4} 3.5 Z`} fill={i < (u.charges ?? 0) ? 'var(--target)' : 'var(--chip-bg)'} stroke="var(--art-outline)" strokeWidth={0.9} />
          ))}
          {u.droneState === 'returning' && (
            <text x={0} y={-3} fontSize="8" fontWeight={700} fill="var(--ink)" stroke="var(--chip-bg)" strokeWidth={2.5} paintOrder="stroke" fontFamily="var(--mono)">
              RTN
            </text>
          )}
        </g>
      )}
      {/* Statuses: an icon and its count, never colour alone */}
      {badges.length > 0 && (
        <g transform={`translate(${-((badges.length - 1) * 25) / 2},-37)`}>
          {badges.map((bd, i) => (
            <g key={bd.icon} transform={`translate(${i * 25},0)`}>
              <rect x={-12} y={-8} width={24} height={15} rx={4} fill="var(--chip-bg)" stroke={bd.c} strokeWidth={1.5} />
              <g transform={`translate(${bd.n ? -10.5 : -6},-6) scale(0.5)`} style={{ color: bd.c }}>
                <IconGlyph name={bd.icon} strokeWidth={2.6} />
              </g>
              {bd.n && (
                <text x={5} y={3.4} textAnchor="middle" fontSize="9.5" fontWeight={800} fill="var(--ink)" fontFamily="var(--mono)">
                  {bd.n}
                </text>
              )}
            </g>
          ))}
        </g>
      )}
      {showId && (
        <text x={-28} y={-14} fontSize="10" fontWeight={700} fill="var(--ink)" stroke="var(--chip-bg)" strokeWidth={2.5} paintOrder="stroke" fontFamily="var(--mono)">
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
    const w = text.length * 6.8 + 9;
    const el = (
      <g key={text + off} transform={`translate(${off},0)`}>
        <rect x={0} y={-8.5} width={w} height={16} rx={3} fill={fill} stroke="var(--art-outline)" strokeWidth={1.2} />
        <text x={w / 2} y={3.6} textAnchor="middle" fontSize="11" fontWeight={800} fill={ink} fontFamily="var(--mono)">
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
      <circle cx={x} cy={y} r={28} fill="none" stroke="var(--danger)" strokeWidth={3.5} strokeDasharray="5 3" />
      <g transform={`translate(${x + 21},${y + 19})`}>
        <path d="M0,-10 L10,8 L-10,8 Z" fill="var(--danger)" stroke="var(--art-outline)" strokeWidth={1.5} />
        <text y={6} textAnchor="middle" fontSize="11" fontWeight={900} fill="#fff">
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
        <rect key={`a${key(t)}`} x={MG + t.x * TS + 3} y={MG + t.y * TS + 3} width={TS - 6} height={TS - 6} rx={4} fill="var(--hazard)" fillOpacity={0.18} stroke="var(--hazard)" strokeWidth={2} strokeDasharray="5 3" />
      ))}
      {pv.lines.map((l, i) => lineEl(l, i, 'var(--ink)', 'arrow-preview', 2.5))}
      {pv.moves.map((m) => (
        <polyline key={`m${m.id}`} points={[m.from, ...m.path].map((q) => `${cx(q.x)},${cy(q.y)}`).join(' ')} fill="none" stroke="var(--ink)" strokeWidth={2.5} strokeDasharray="3 5" />
      ))}
      {pv.moves.map((m) => {
        const u = after?.units.find((x) => x.id === m.id) ?? s.units.find((x) => x.id === m.id);
        return u ? <UnitToken key={`g${m.id}`} c={c} u={{ ...u, pos: m.to }} ghost /> : null;
      })}
      {pv.placed.map((o) => (
        <g key={`p${o.id}`} opacity={0.55}>
          <ObjectGlyph o={o} />
        </g>
      ))}
      {pv.spawned.map((u) => (
        <UnitToken key={`sp${u.id}`} c={c} u={u} ghost />
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
        it.tiles.map((t) => <rect key={`it${it.id}${key(t)}`} x={MG + t.x * TS + 3} y={MG + t.y * TS + 3} width={TS - 6} height={TS - 6} fill="url(#lx-hatch)" />),
      )}
      {rep.intents.map((it) =>
        it.path.length ? (
          <g key={`ip${it.id}`}>
            <polyline
              points={[it.from, ...it.path].map((q) => `${cx(q.x)},${cy(q.y)}`).join(' ')}
              fill="none"
              stroke={it.team === 'enemy' ? 'var(--enemy)' : 'var(--player)'}
              strokeWidth={2.2}
              strokeDasharray="5 4"
              opacity={0.9}
            />
            <circle cx={cx(it.to.x)} cy={cy(it.to.y)} r={it.arrival ? 9 : 5} fill={it.arrival ? 'none' : it.team === 'enemy' ? 'var(--enemy)' : 'var(--player)'} stroke={it.team === 'enemy' ? 'var(--enemy)' : 'var(--player)'} strokeWidth={2} />
          </g>
        ) : null,
      )}
      {rep.intents.map((it) =>
        it.arrival
          ? null
          : it.lines.map((l, i) => (
              <g key={`il${it.id}-${i}`}>{lineEl(l, i, it.team === 'enemy' ? 'var(--danger)' : 'var(--player)', it.team === 'enemy' ? 'arrow-enemy' : 'arrow-minion', 1.9, l.kind === 'shot' ? '1 0' : undefined)}</g>
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
          <g key={`in${k}`} transform={`translate(${cx(pos.x) - 32},${cy(pos.y) - 24})`}>
            <rect x={0} y={-8.5} width={v.killed ? 40 : 27} height={16} rx={3} fill="var(--chip-bg)" stroke="var(--danger)" strokeWidth={1.6} />
            <text x={4} y={3.6} fontSize="11" fontWeight={800} fill="var(--danger)" fontFamily="var(--mono)">
              {`▼${v.dmg}${v.killed ? ' KO' : ''}`}
            </text>
            <title>{`Incoming this round: ${v.dmg} damage${v.killed ? ', lethal' : ''}`}</title>
          </g>
        );
      })}
    </g>
  );
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
            <text key={`d${key(t)}`} x={MG + t.x * TS + 3} y={MG + (t.y + 1) * TS - 3} fontSize="8.5" fill="var(--ink-2)" stroke="var(--chip-bg)" strokeWidth={2} paintOrder="stroke" fontFamily="var(--mono)">
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
            <text key={`hd${k}`} x={MG + x * TS + TS / 2} y={MG + y * TS + 11} textAnchor="middle" fontSize="9" fontWeight={700} fill="var(--hazard)" stroke="var(--chip-bg)" strokeWidth={2.5} paintOrder="stroke" fontFamily="var(--mono)">
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
