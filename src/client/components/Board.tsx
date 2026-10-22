import { memo, useEffect, useRef, useState, type CSSProperties, type PointerEvent, type ReactNode } from 'react';
import type { Content } from '../../content/types';
import type { CommandPreview, HitPreview, IntentReport, LinePreview } from '../../preview/preview';
import { boardFor } from '../../state/grid';
import type { AIOption, GameEvent, GameObject, GameState, Hazard, Pos, Unit } from '../../state/types';
import { tileName } from '../../util/tiles';
import { FxLayer } from '../art/fx';
import { FOOT, MG, TS, cx, cy, posKey as key, projection, type BoardView, type Projection, type XY } from '../art/geometry';
import { IconGlyph } from '../art/icons';
import { IsoBlock, IsoRoom, blockBox, muzzle, overlaps, type Box } from '../art/iso';
import { ObjectArt, isFlat } from '../art/objects';
import { BLOCK_TERRAIN, Room } from '../art/terrain';
import { UnitBase, UnitFigure, baseShapeFor, unitColor } from '../art/units';
import { useSlides } from '../useSlides';

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
  /** Top-down (the default) or isometric. */
  view?: BoardView;
}

/** The top-down projection, for the pieces drawn outside a board (the minion roster draws a unit token). */
const TOP = projection('top', 8, 8);

export function Board(p: BoardProps) {
  const { c, s } = p;
  const b = boardFor(c.map);
  const proj = projection(p.view ?? 'top', b.width, b.height);
  const iso = proj.view === 'iso';
  const W = proj.width;
  const H = proj.height;
  const lastPointer = useRef<'mouse' | 'touch' | 'pen'>('mouse');
  const tiles = b.allTiles();
  const hazardAt = new Map<string, Hazard>();
  for (const h of s.hazards) for (const t of h.tiles) hazardAt.set(key(t), h);
  const units = s.units.filter((u) => !u.dead && u.droneState !== 'docked');
  // Units hit in the frame on screen flash and flinch.
  const hit = new Set<number>();
  for (const e of p.frameEvents) if (e.t === 'DamageDealt' && e.target.kind === 'unit' && e.amount > 0) hit.add(e.target.id);
  const fxScale = p.speed === 2 ? 0.55 : 1;
  const slid = useSlides(units, iso);

  const input = (t: Pos) => ({
    onPointerDown: (e: PointerEvent) => (lastPointer.current = e.pointerType as 'mouse' | 'touch' | 'pen'),
    onMouseEnter: () => p.onHover(t),
    onClick: () => p.onClick(t, lastPointer.current === 'touch'),
  });

  return (
    <svg
      className={iso ? 'board-svg iso' : 'board-svg'}
      viewBox={`0 0 ${W} ${H}`}
      role="img"
      aria-label={`${c.map.name}, round ${s.round}${iso ? ', isometric view' : ''}`}
      onMouseLeave={() => p.onHover(null)}
      style={{ '--fx': fxScale } as CSSProperties}
    >
      {iso ? <IsoScene p={p} proj={proj} units={units} hit={hit} slid={slid} hazardAt={hazardAt} fxScale={fxScale} /> : <TopScene p={p} proj={proj} units={units} hit={hit} hazardAt={hazardAt} fxScale={fxScale} />}

      {/* Input layer */}
      {iso ? (
        <IsoInput p={p} proj={proj} units={units} input={input} />
      ) : (
        tiles.map((t) => (
          <rect key={`in${key(t)}`} x={MG + t.x * TS} y={MG + t.y * TS} width={TS} height={TS} fill="transparent" {...input(t)}>
            <title>{tileName(t)}</title>
          </rect>
        ))
      )}
      {p.dimmed && <rect x={0} y={0} width={W} height={H} fill="var(--bg)" opacity={0.35} pointerEvents="none" />}
    </svg>
  );
}

interface SceneProps {
  p: BoardProps;
  proj: Projection;
  units: Unit[];
  hit: Set<number>;
  hazardAt: Map<string, Hazard>;
  fxScale: number;
}

/** The top-down board, in the order it has always been drawn. */
function TopScene({ p, proj, units, hit, hazardAt, fxScale }: SceneProps) {
  const { c, s } = p;
  return (
    <>
      <Room map={c.map} />

      {/* Highlights */}
      {p.highlights.map((h, i) =>
        h.tiles.map((t) => <HighlightTile key={`h${i}-${key(t)}`} t={t} kind={h.kind} label={h.label?.(t) ?? null} />),
      )}
      {p.hover && <Brackets x={MG + p.hover.x * TS} y={MG + p.hover.y * TS} color="var(--ink)" inset={2} len={14} width={2.5} opacity={0.85} />}

      {/* Hazard cues: a cue and nothing more, never a danger zone */}
      {s.hazards.map((h) => (
        <HazardCue key={`hz${h.id}`} h={h} proj={proj} />
      ))}
      <HazardCooldowns c={c} s={s} debug={p.debug} proj={proj} />

      {/* Objects */}
      {s.objects.map((o) => (
        <ObjectGlyph key={`o${o.id}`} o={o} proj={proj} />
      ))}

      {/* Intents */}
      {p.showIntents && p.intents && !p.preview && <IntentLayer s={s} rep={p.intents} proj={proj} />}
      {p.preview?.intents && p.showIntents && <IntentLayer s={p.preview.state ?? s} rep={p.preview.intents} proj={proj} faint />}

      {/* Move path */}
      {p.movePath && p.movePath.length > 0 && (
        <g pointerEvents="none">
          <MovePathLine s={s} path={p.movePath} proj={proj} />
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
      {p.preview && p.preview.ok && <PreviewLayer c={c} s={s} pv={p.preview} proj={proj} />}

      {/* Combat effects for the frames on screen */}
      <FxLayer frameKey={p.frameKey} events={p.frameEvents} s={s} scale={fxScale} proj={proj} />

      {/* Debug */}
      {p.debug && <DebugLayer c={c} dbg={p.debug} hazardAt={hazardAt} proj={proj} />}
    </>
  );
}

/** The isometric room never changes during a fight; drawing it once keeps the slide's per-frame renders cheap. */
const IsoRoomMemo = memo(IsoRoom);

/**
 * The isometric board. Things on the floor (highlights, hatching, area tiles) are drawn in floor space through the
 * floor transform. Things that stand up (terrain blocks, objects, units) are drawn front-on, back to front. Numbers,
 * bars and chips go on a layer above all of them, so nothing standing in front can hide them.
 */
function IsoScene({ p, proj, units, hit, slid, hazardAt, fxScale }: SceneProps & { slid: Map<number, XY> }) {
  const { c, s } = p;
  const b = boardFor(c.map);
  const intentsShown = p.showIntents && p.intents && !p.preview ? p.intents : null;
  const previewIntents = p.preview?.intents && p.showIntents ? p.preview.intents : null;
  const previewOk = p.preview && p.preview.ok ? p.preview : null;
  const drawnAt = (u: Unit) => slid.get(u.id) ?? u.pos;

  // What the terrain blocks would hide: units, objects and the hovered tile behind them. A block with any of those
  // behind it fades, so the unit (and its base, which shows its side) stays readable.
  const probes: { depth: number; box: Box }[] = [];
  for (const u of units) probes.push({ depth: proj.depth(u.pos.x, u.pos.y), box: figureBox(proj, u.pos) });
  for (const o of s.objects) probes.push({ depth: proj.depth(o.pos.x, o.pos.y), box: objectBox(proj, o) });
  if (p.hover) {
    const h = proj.at(p.hover.x, p.hover.y);
    probes.push({ depth: proj.depth(p.hover.x, p.hover.y), box: { x0: h.x - 26, x1: h.x + 26, y0: h.y - 14, y1: h.y + 14 } });
  }

  const uprights: { depth: number; rank: number; x: number; id: number; node: ReactNode }[] = [];
  for (const t of b.allTiles()) {
    const id = b.terrainId(t);
    if (!BLOCK_TERRAIN.includes(id)) continue;
    const depth = proj.depth(t.x, t.y);
    const box = blockBox(proj, t, id);
    const faded = probes.some((pr) => pr.depth < depth && overlaps(pr.box, box));
    uprights.push({ depth, rank: 0, x: t.x, id: t.y, node: <IsoBlock key={`b${key(t)}`} map={c.map} t={t} id={id} proj={proj} faded={faded} /> });
  }
  for (const o of s.objects) {
    uprights.push({ depth: proj.depth(o.pos.x, o.pos.y), rank: isFlat(o) ? 1 : 2, x: o.pos.x, id: o.id, node: <ObjectGlyph key={`o${o.id}`} o={o} proj={proj} parts="art" /> });
  }
  for (const u of units) {
    const at = drawnAt(u);
    uprights.push({ depth: proj.depth(at.x, at.y), rank: 3, x: at.x, id: u.id, node: <UnitToken key={`u${u.id}`} c={c} u={u} hit={hit.has(u.id)} proj={proj} at={proj.at(at.x, at.y)} part="body" /> });
  }
  uprights.sort((a, z) => a.depth - z.depth || a.rank - z.rank || a.x - z.x || a.id - z.id);

  return (
    <>
      <IsoRoomMemo map={c.map} proj={proj} />

      {/* On the floor */}
      <g transform={proj.floor} pointerEvents="none">
        {p.highlights.map((h, i) =>
          h.tiles.map((t) => <HighlightTile key={`h${i}-${key(t)}`} t={t} kind={h.kind} label={null} />),
        )}
        {p.hover && <Brackets x={MG + p.hover.x * TS} y={MG + p.hover.y * TS} color="var(--ink)" inset={2} len={14} width={2.5} opacity={0.85} />}
        {intentsShown && <IntentLayer s={s} rep={intentsShown} proj={proj} part="floor" />}
        {previewIntents && <IntentLayer s={p.preview!.state ?? s} rep={previewIntents} proj={proj} part="floor" faint />}
        {previewOk && <PreviewLayer c={c} s={s} pv={previewOk} proj={proj} part="floor" />}
        {p.debug && <DebugLayer c={c} dbg={p.debug} hazardAt={hazardAt} proj={proj} part="floor" />}
        {p.movePath && p.movePath.length > 0 && <circle cx={cx(p.movePath[p.movePath.length - 1]!.x)} cy={cy(p.movePath[p.movePath.length - 1]!.y)} r={12} fill="none" stroke="var(--move)" strokeWidth={3} />}
      </g>
      {p.highlights.map((h, i) => h.tiles.map((t) => <HighlightLabel key={`hl${i}-${key(t)}`} t={t} label={h.label?.(t) ?? null} proj={proj} />))}

      {/* Paths and lines along the floor, under everything standing */}
      {intentsShown && <IntentLayer s={s} rep={intentsShown} proj={proj} part="lines" />}
      {previewIntents && <IntentLayer s={p.preview!.state ?? s} rep={previewIntents} proj={proj} part="lines" faint />}
      {p.movePath && p.movePath.length > 0 && (
        <g pointerEvents="none">
          <MovePathLine s={s} path={p.movePath} proj={proj} />
        </g>
      )}

      {/* Everything standing, back to front */}
      <g pointerEvents="none">{uprights.map((x) => x.node)}</g>

      {/* Hazard cues: a cue and nothing more, never a danger zone */}
      {s.hazards.map((h) => (
        <HazardCue key={`hz${h.id}`} h={h} proj={proj} />
      ))}

      {/* Bars, chips and tags, above everything standing */}
      <g pointerEvents="none">
        {s.objects.map((o) => (
          <ObjectGlyph key={`oc${o.id}`} o={o} proj={proj} parts="chips" />
        ))}
        {units.map((u) => {
          const at = drawnAt(u);
          return <UnitToken key={`uh${u.id}`} c={c} u={u} showId={!!p.debug?.ids} proj={proj} at={proj.at(at.x, at.y)} part="hud" />;
        })}
      </g>
      <HazardCooldowns c={c} s={s} debug={p.debug} proj={proj} />
      {intentsShown && <IntentLayer s={s} rep={intentsShown} proj={proj} part="tags" />}
      {previewIntents && <IntentLayer s={p.preview!.state ?? s} rep={previewIntents} proj={proj} part="tags" faint />}

      {/* Preview of the hovered action */}
      {previewOk && <PreviewLayer c={c} s={s} pv={previewOk} proj={proj} part="upright" />}

      {/* Combat effects for the frames on screen */}
      <FxLayer frameKey={p.frameKey} events={p.frameEvents} s={s} scale={fxScale} proj={proj} />

      {/* Debug */}
      {p.debug && <DebugLayer c={c} dbg={p.debug} hazardAt={hazardAt} proj={proj} part="text" />}
    </>
  );
}

/** The screen box a unit's figure covers, standing on tile p. */
function figureBox(proj: Projection, p: Pos): Box {
  const a = proj.near(p.x, p.y, 0, 0);
  const f = proj.k;
  return { x0: a.x - 22 * f, x1: a.x + 22 * f, y0: a.y - 28 * f, y1: a.y + FOOT * f };
}

function objectBox(proj: Projection, o: GameObject): Box {
  if (isFlat(o)) {
    const a = proj.at(o.pos.x, o.pos.y);
    return { x0: a.x - 16, x1: a.x + 16, y0: a.y - 8, y1: a.y + 8 };
  }
  return figureBox(proj, o.pos);
}

/**
 * Input on the isometric board: a diamond per tile, and over them a box per unit and standing object, so a click on a
 * figure picks the tile it stands on even where it rises over the tile behind.
 */
function IsoInput({ p, proj, units, input }: { p: BoardProps; proj: Projection; units: Unit[]; input: (t: Pos) => object }) {
  const b = boardFor(p.c.map);
  const figures: { pos: Pos; key: string }[] = [...p.s.objects.filter((o) => !isFlat(o)).map((o) => ({ pos: o.pos, key: `o${o.id}` })), ...units.map((u) => ({ pos: u.pos, key: `u${u.id}` }))];
  figures.sort((a, z) => proj.depth(a.pos.x, a.pos.y) - proj.depth(z.pos.x, z.pos.y) || a.pos.x - z.pos.x);
  return (
    <g>
      {b.allTiles().map((t) => (
        <polygon key={`in${key(t)}`} points={proj.outline(t.x, t.y)} fill="transparent" {...input(t)}>
          <title>{tileName(t)}</title>
        </polygon>
      ))}
      {figures.map((f) => {
        const r = figureBox(proj, f.pos);
        return (
          <rect key={`hit${f.key}`} x={r.x0} y={r.y0} width={r.x1 - r.x0} height={r.y1 - r.y0} fill="transparent" {...input(f.pos)}>
            <title>{tileName(f.pos)}</title>
          </rect>
        );
      })}
    </g>
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

/** A highlighted tile, drawn in floor space. */
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

/** A highlight's label on the isometric board: upright, at the tile's top corner. */
function HighlightLabel({ t, label, proj }: { t: Pos; label: string | null; proj: Projection }) {
  if (!label) return null;
  const a = proj.at(t.x, t.y);
  return (
    <text x={a.x} y={a.y - 8} textAnchor="middle" fontSize={11} fontFamily="var(--mono)" fill="var(--ink-2)" stroke="var(--chip-bg)" strokeWidth={2.5} paintOrder="stroke" pointerEvents="none">
      {label}
    </text>
  );
}

function MovePathLine({ s, path, proj }: { s: GameState; path: Pos[]; proj: Projection }) {
  return (
    <polyline
      points={[s.units.find((u) => u.id === 0)?.pos, ...path]
        .filter(Boolean)
        .map((q) => {
          const a = proj.at(q!.x, q!.y);
          return `${a.x},${a.y}`;
        })
        .join(' ')}
      fill="none"
      stroke="var(--move)"
      strokeWidth={4}
      strokeLinecap="round"
      strokeLinejoin="round"
      strokeDasharray="2 9"
    />
  );
}

// ------------------------------------------------------------------------------------------ hazards

function HazardCue({ h, proj }: { h: Hazard; proj: Projection }) {
  if (h.state !== 'primed') return null;
  return (
    <g pointerEvents="none" aria-label={`${h.id} cue`}>
      {h.tiles.map((t) => {
        const { x, y } = proj.at(t.x, t.y);
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
          const m = muzzle(proj, t, h.dir);
          return (
            <g key={key(t)}>
              <circle cx={m.x} cy={m.y} r={16} fill="url(#lx-glow-amber)">
                <animate attributeName="opacity" values="0.3;1;0.3" dur="1.1s" repeatCount="indefinite" />
              </circle>
              <circle cx={m.x} cy={m.y} r={4} fill="#ffe08a">
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

/** Cooldowns are hidden from players (spec, Hazard cues) unless rules.ui.showHazardCooldowns is set. */
function HazardCooldowns({ c, s, debug, proj }: { c: Content; s: GameState; debug: BoardDebug | null; proj: Projection }) {
  if (!c.rules.ui.showHazardCooldowns || debug?.hazards) return null;
  return (
    <>
      {s.hazards
        .filter((h) => h.state === 'cooldown')
        .map((h) => {
          const a = proj.pt(cx(h.tiles[0]!.x), MG + h.tiles[0]!.y * TS + 11);
          return (
            <text key={`hcd${h.id}`} x={a.x} y={a.y} textAnchor="middle" fontSize="9" fill="var(--ink-2)" fontFamily="var(--mono)" pointerEvents="none">
              {`cooling ${h.cooldown}`}
            </text>
          );
        })}
    </>
  );
}

// ------------------------------------------------------------------------------------------ objects

function ObjectGlyph({ o, proj, parts = 'all' }: { o: GameObject; proj: Projection; parts?: 'all' | 'art' | 'chips' }) {
  const title =
    o.kind === 'corpse' ? `${o.of} corpse, ${o.decay} round(s) left` : o.kind === 'mine' ? 'Your mine' : o.kind === 'egg' ? `Egg: hatches into ${o.hatchInto} in ${o.timer} round(s); ${o.hp} HP` : `Shield segment, ${o.hp} HP`;
  const a = proj.at(o.pos.x, o.pos.y);
  if (proj.view === 'iso') {
    // Flat objects lie on the floor, squashed like it; standing ones stand on the tile's centre. Chips are never squashed.
    const art = isFlat(o) && parts === 'art' ? `translate(${a.x},${a.y}) scale(${proj.k},${proj.k * 0.55})` : `translate(${a.x},${a.y}) ${proj.upright}`;
    return (
      <g pointerEvents="none" transform={art}>
        <ObjectArt o={o} parts={parts} />
        {parts !== 'chips' && <title>{title}</title>}
      </g>
    );
  }
  return (
    <g pointerEvents="none" transform={`translate(${a.x},${a.y})`}>
      <ObjectArt o={o} parts={parts} />
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

/** The base, squashed onto the isometric floor: a tile there is twice as wide as it is deep. */
const ISO_BASE = `translate(0,${FOOT}) scale(1,1.45) translate(0,${-FOOT})`;

/** True for the first moments after mounting, while the unit fades in. */
function useFresh(ms = 400): boolean {
  const [fresh, setFresh] = useState(true);
  useEffect(() => {
    const t = window.setTimeout(() => setFresh(false), ms);
    return () => window.clearTimeout(t);
  }, [ms]);
  return fresh;
}

/**
 * A unit on the board. `part` draws only its figure and base (`body`) or only its bars, chips and badges (`hud`): the
 * isometric board draws figures in depth order and the rest on a layer above them. `at` is where to draw it (a unit
 * mid-slide on the isometric board); it defaults to the unit's tile.
 */
export function UnitToken({ c, u, showId, ghost, hit, proj = TOP, at, part = 'all' }: { c: Content; u: Unit; showId?: boolean; ghost?: boolean; hit?: boolean; proj?: Projection; at?: XY; part?: 'all' | 'body' | 'hud' }) {
  // The fade-in plays once: the isometric board may move this element when units pass each other, which would replay it.
  const fresh = useFresh();
  const def = c.units[u.def]!;
  const iso = proj.view === 'iso';
  const pos = at ?? proj.at(u.pos.x, u.pos.y);
  const body = part !== 'hud';
  const hud = part !== 'body';
  const color = unitColor(u.def, u.team);
  const armor = Math.max(0, u.armor - (u.statuses.corrode?.stacks ?? 0));
  const badges: { icon: string; n: string; c: string }[] = [];
  if (u.statuses.poison) badges.push({ icon: 'poison', n: `${u.statuses.poison.remaining}`, c: STATUS_COLOR.poison! });
  if (u.statuses.parasite) badges.push({ icon: 'parasite', n: `${u.statuses.parasite.remaining}`, c: STATUS_COLOR.parasite! });
  if (u.statuses.corrode) badges.push({ icon: 'corrode', n: `${u.statuses.corrode.stacks}`, c: STATUS_COLOR.corrode! });
  if (u.statuses.pinned) badges.push({ icon: 'pinned', n: '', c: STATUS_COLOR.pinned! });
  if (u.statuses.slowed) badges.push({ icon: 'slowed', n: '', c: STATUS_COLOR.slowed! });
  const small = u.def === 'hatchling' || u.def === 'spitter' || u.def === 'burster';
  const content = (
    <>
      {body && (
        <>
          {iso ? (
            <g transform={ISO_BASE}>
              <UnitBase shape={baseShapeFor(u.def, u.team)} color={color} small={small} />
            </g>
          ) : (
            <UnitBase shape={baseShapeFor(u.def, u.team)} color={color} small={small} />
          )}
          <g transform={u.flying ? 'translate(0,-8)' : undefined}>
            <g className={ghost ? undefined : `art-idle${hit ? ' art-hit' : ''}`} style={ghost ? undefined : { animationDelay: `${-((u.id * 0.37) % 2.4)}s` }}>
              <UnitFigure def={u.def} />
            </g>
          </g>
        </>
      )}
      {hud && (
        <>
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
        </>
      )}
    </>
  );
  return (
    <g
      style={{ transform: `translate(${pos.x}px, ${pos.y}px)`, transition: ghost || iso ? undefined : 'transform 0.28s ease-in-out' }}
      opacity={ghost ? 0.45 : 1}
      pointerEvents="none"
      className={ghost || !fresh ? undefined : 'unit-in'}
    >
      {iso ? <g transform={proj.upright}>{content}</g> : content}
      {body && <title>{`${u.def === 'player' ? 'You' : `${def.name} ${u.id}`}: ${u.hp}/${u.maxHp} HP${def.armor ? `, armor ${armor}` : ''}`}</title>}
    </g>
  );
}

// ------------------------------------------------------------------------------------------ previews and intents

function lineEl(proj: Projection, l: LinePreview, i: number, color: string, marker: string, width = 2.5, dash?: string) {
  const a = proj.at(l.from.x, l.from.y);
  const b = proj.at(l.to.x, l.to.y);
  if (l.kind === 'chain') {
    const mx = (a.x + b.x) / 2 + 6;
    const my = (a.y + b.y) / 2 - 6;
    return <polyline key={i} points={`${a.x},${a.y} ${mx},${my} ${b.x},${b.y}`} fill="none" stroke="var(--target)" strokeWidth={2.5} strokeDasharray="4 2" />;
  }
  return (
    <line
      key={i}
      x1={a.x}
      y1={a.y}
      x2={b.x}
      y2={b.y}
      stroke={l.kind === 'reflect' ? 'var(--parasite)' : l.kind === 'charge' ? 'var(--danger)' : color}
      strokeWidth={l.kind === 'charge' ? 5 : width}
      strokeDasharray={dash}
      markerEnd={`url(#${marker})`}
      opacity={0.9}
    />
  );
}

function HitBadge({ h, x, y, small, k = 1 }: { h: HitPreview; x: number; y: number; small?: boolean; k?: number }) {
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
  const sc = (small ? 0.85 : 1) * k;
  if (k === 1) return <g transform={`translate(${x - off / 2},${y - (small ? 8 : 12)}) scale(${small ? 0.85 : 1})`}>{parts}</g>;
  return <g transform={`translate(${x - (off / 2) * sc},${y - (small ? 8 : 12) * k}) scale(${sc})`}>{parts}</g>;
}

function FriendlyMark({ x, y, k = 1 }: { x: number; y: number; k?: number }) {
  return (
    <g pointerEvents="none">
      <circle cx={x} cy={y} r={28 * k} fill="none" stroke="var(--danger)" strokeWidth={3.5} strokeDasharray="5 3" />
      <g transform={`translate(${x + 21 * k},${y + 19 * k})`}>
        <path d="M0,-10 L10,8 L-10,8 Z" fill="var(--danger)" stroke="var(--art-outline)" strokeWidth={1.5} />
        <text y={6} textAnchor="middle" fontSize="11" fontWeight={900} fill="#fff">
          !
        </text>
      </g>
    </g>
  );
}

/** The hovered action's preview. `part` splits it for the isometric board: area tiles on the floor, the rest upright. */
function PreviewLayer({ c, s, pv, proj, part = 'all' }: { c: Content; s: GameState; pv: CommandPreview; proj: Projection; part?: 'all' | 'floor' | 'upright' }) {
  const after = pv.state;
  const floor = part !== 'upright';
  const upright = part !== 'floor';
  return (
    <g pointerEvents="none">
      {floor &&
        pv.areaTiles.map((t) => (
          <rect key={`a${key(t)}`} x={MG + t.x * TS + 3} y={MG + t.y * TS + 3} width={TS - 6} height={TS - 6} rx={4} fill="var(--hazard)" fillOpacity={0.18} stroke="var(--hazard)" strokeWidth={2} strokeDasharray="5 3" />
        ))}
      {upright && (
        <>
          {pv.lines.map((l, i) => lineEl(proj, l, i, 'var(--ink)', 'arrow-preview', 2.5))}
          {pv.moves.map((m) => (
            <polyline
              key={`m${m.id}`}
              points={[m.from, ...m.path]
                .map((q) => {
                  const a = proj.at(q.x, q.y);
                  return `${a.x},${a.y}`;
                })
                .join(' ')}
              fill="none"
              stroke="var(--ink)"
              strokeWidth={2.5}
              strokeDasharray="3 5"
            />
          ))}
          {pv.moves.map((m) => {
            const u = after?.units.find((x) => x.id === m.id) ?? s.units.find((x) => x.id === m.id);
            return u ? <UnitToken key={`g${m.id}`} c={c} u={{ ...u, pos: m.to }} proj={proj} ghost /> : null;
          })}
          {pv.placed.map((o) => (
            <g key={`p${o.id}`} opacity={0.55}>
              <ObjectGlyph o={o} proj={proj} />
            </g>
          ))}
          {pv.spawned.map((u) => (
            <UnitToken key={`sp${u.id}`} c={c} u={u} proj={proj} ghost />
          ))}
          {pv.hits.map((h) => {
            const pos = h.ref.kind === 'unit' ? after?.units.find((u) => u.id === h.ref.id)?.pos ?? h.pos : h.pos;
            const mid = proj.near(pos.x, pos.y, 0, 0);
            const badge = proj.near(pos.x, pos.y, 0, 2);
            return (
              <g key={`hit${h.ref.kind}${h.ref.id}`}>
                {h.friendly && <FriendlyMark x={mid.x} y={mid.y} k={proj.k} />}
                <HitBadge h={h} x={badge.x} y={badge.y} k={proj.k} />
              </g>
            );
          })}
        </>
      )}
    </g>
  );
}

/**
 * Enemy and minion intents. `part` splits them for the isometric board: hatching on the floor, paths and lines along
 * it, and the incoming-damage tags on the top layer.
 */
function IntentLayer({ s, rep, faint, proj, part = 'all' }: { s: GameState; rep: IntentReport; faint?: boolean; proj: Projection; part?: 'all' | 'floor' | 'lines' | 'tags' }) {
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
  const floor = part === 'all' || part === 'floor';
  const lines = part === 'all' || part === 'lines';
  const tags = part === 'all' || part === 'tags';
  const at = (q: Pos) => proj.at(q.x, q.y);
  return (
    <g pointerEvents="none" opacity={faint ? 0.55 : 0.95}>
      {floor &&
        rep.intents.map((it) =>
          it.tiles.map((t) => <rect key={`it${it.id}${key(t)}`} x={MG + t.x * TS + 3} y={MG + t.y * TS + 3} width={TS - 6} height={TS - 6} fill="url(#lx-hatch)" />),
        )}
      {lines &&
        rep.intents.map((it) =>
          it.path.length ? (
            <g key={`ip${it.id}`}>
              <polyline
                points={[it.from, ...it.path].map((q) => `${at(q).x},${at(q).y}`).join(' ')}
                fill="none"
                stroke={it.team === 'enemy' ? 'var(--enemy)' : 'var(--player)'}
                strokeWidth={2.2}
                strokeDasharray="5 4"
                opacity={0.9}
              />
              <circle cx={at(it.to).x} cy={at(it.to).y} r={it.arrival ? 9 : 5} fill={it.arrival ? 'none' : it.team === 'enemy' ? 'var(--enemy)' : 'var(--player)'} stroke={it.team === 'enemy' ? 'var(--enemy)' : 'var(--player)'} strokeWidth={2} />
            </g>
          ) : null,
        )}
      {lines &&
        rep.intents.map((it) =>
          it.arrival
            ? null
            : it.lines.map((l, i) => (
                <g key={`il${it.id}-${i}`}>{lineEl(proj, l, i, it.team === 'enemy' ? 'var(--danger)' : 'var(--player)', it.team === 'enemy' ? 'arrow-enemy' : 'arrow-minion', 1.9, l.kind === 'shot' ? '1 0' : undefined)}</g>
              )),
        )}
      {tags &&
        rep.intents
          .filter((it) => it.diesFirst)
          .map((it) => {
            const a = proj.near(it.from.x, it.from.y, 14, -14);
            return (
              <text key={`df${it.id}`} x={a.x} y={a.y} fontSize={13 * proj.k} fontWeight={900} fill="var(--danger)">
                ✕
              </text>
            );
          })}
      {tags &&
        [...incoming.entries()].map(([k, v]) => {
          const u = s.units.find((x) => `unit${x.id}` === k);
          const pos = u?.pos ?? v.pos;
          const a = proj.near(pos.x, pos.y, -32, -24);
          return (
            <g key={`in${k}`} transform={proj.k === 1 ? `translate(${a.x},${a.y})` : `translate(${a.x},${a.y}) scale(${proj.k})`}>
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

/** The debug overlay on the board. `part` splits it for the isometric board: spawn tiles on the floor, text upright. */
function DebugLayer({ c, dbg, hazardAt, proj, part = 'all' }: { c: Content; dbg: BoardDebug; hazardAt: Map<string, Hazard>; proj: Projection; part?: 'all' | 'floor' | 'text' }) {
  const floor = part !== 'text';
  const text = part !== 'floor';
  const f = proj.k;
  return (
    <g pointerEvents="none">
      {text &&
        dbg.ids &&
        boardFor(c.map)
          .allTiles()
          .map((t) => {
            const a = proj.view === 'iso' ? proj.pt(MG + t.x * TS + 6, MG + (t.y + 1) * TS - 6) : proj.pt(MG + t.x * TS + 3, MG + (t.y + 1) * TS - 3);
            return (
              <text key={`d${key(t)}`} x={a.x} y={a.y} fontSize={8.5 * f} fill="var(--ink-2)" stroke="var(--chip-bg)" strokeWidth={2} paintOrder="stroke" fontFamily="var(--mono)">
                {tileName(t)}
              </text>
            );
          })}
      {dbg.spawns.map((sp, i) => {
        const a = proj.pt(MG + sp.tile.x * TS + TS - 8, MG + sp.tile.y * TS + 15);
        return (
          <g key={`sp${i}`}>
            {floor && <rect x={MG + sp.tile.x * TS + 5} y={MG + sp.tile.y * TS + 5} width={TS - 10} height={TS - 10} rx={4} fill="none" stroke="var(--enemy)" strokeWidth={1.5} strokeDasharray="3 3" />}
            {text && (
              <text x={a.x} y={a.y} textAnchor="end" fontSize={9 * f} fill="var(--enemy)" fontFamily="var(--mono)">
                W{sp.wave}
              </text>
            )}
          </g>
        );
      })}
      {text &&
        dbg.hazards &&
        [...hazardAt.entries()].map(([k, h]) => {
          const [x, y] = k.split(',').map(Number) as [number, number];
          const txt = h.state === 'cooldown' ? `cd${h.cooldown}` : h.state === 'primed' ? 'PRIMED' : 'ready';
          const a = proj.pt(MG + x * TS + TS / 2, MG + y * TS + 11);
          return (
            <text key={`hd${k}`} x={a.x} y={a.y} textAnchor="middle" fontSize={9 * f} fontWeight={700} fill="var(--hazard)" stroke="var(--chip-bg)" strokeWidth={2.5} paintOrder="stroke" fontFamily="var(--mono)">
              {txt}
            </text>
          );
        })}
      {text &&
        dbg.flamer?.map((o, i) => {
          const a = proj.pt(cx(o.tile.x) + o.dir.x * 18, cy(o.tile.y) + o.dir.y * 18);
          return (
            <text key={`fl${i}`} x={a.x} y={a.y + 3} textAnchor="middle" fontSize={10 * f} fontWeight={800} fill={o.score > 0 ? 'var(--danger)' : 'var(--ink-3)'} fontFamily="var(--mono)">
              {o.score}
            </text>
          );
        })}
    </g>
  );
}
