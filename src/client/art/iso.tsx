import type { DirectionName, MapFile } from '../../content/types';
import { boardFor } from '../../state/grid';
import type { Pos } from '../../state/types';
import { tileName } from '../../util/tiles';
import { ISO_WALL, MG, TS, posKey, tx, ty, type Projection, type XY } from './geometry';
import { BLOCK_TERRAIN, FloorBase, FloorPanel, Vent, WallGun, gunDirections } from './terrain';

/**
 * The room on the isometric board: two back walls, the floor drawn through the projection's floor transform, a front
 * lip with the coordinates, and the terrain that stands up from the floor (walls, pillars, shutters, the wall gun) as
 * blocks. Blocks are drawn by the board in depth order with the units, so a unit in front of a pillar covers it and a
 * unit behind it is covered (and the pillar fades, so the unit stays readable).
 */

/** How tall each kind of block stands, and how far its footprint is inset from the tile's edges, in floor pixels. */
const BLOCK: Record<string, { h: number; inset: number }> = {
  wall: { h: 58, inset: 1 },
  pillar: { h: 70, inset: 13 },
  shutter: { h: 54, inset: 0 },
  gun: { h: 16, inset: 5 },
};

/** The front lip's thickness under the floor's near edges. */
const LIP = 16;

const DIR: Record<DirectionName, Pos> = { N: { x: 0, y: -1 }, E: { x: 1, y: 0 }, S: { x: 0, y: 1 }, W: { x: -1, y: 0 } };

/** The muzzle of a wall gun on tile t facing `dir`, where its primed cue glows. */
export function muzzle(proj: Projection, t: Pos, dir: Pos | undefined): XY {
  const d = dir ?? DIR.W;
  const p = proj.pt(MG + t.x * TS + TS / 2 + d.x * 26, MG + t.y * TS + TS / 2 + d.y * 26);
  return proj.view === 'iso' ? { x: p.x, y: p.y - BLOCK.gun!.h } : p;
}

export interface Box {
  x0: number;
  x1: number;
  y0: number;
  y1: number;
}

/** The screen box a block covers, for deciding what it hides. */
export function blockBox(proj: Projection, t: Pos, id: string): Box {
  const c = proj.at(t.x, t.y);
  const { h, inset } = BLOCK[id] ?? { h: 0, inset: 0 };
  const hw = TS - 2 * inset;
  const hh = TS / 2 - inset;
  return { x0: c.x - hw, x1: c.x + hw, y0: c.y - hh - h, y1: c.y + hh };
}

export const overlaps = (a: Box, b: Box, margin = 3) => a.x0 + margin < b.x1 && b.x0 + margin < a.x1 && a.y0 + margin < b.y1 && b.y0 + margin < a.y1;

// ------------------------------------------------------------------------------------------ room

export function IsoRoom({ map, proj }: { map: MapFile; proj: Projection }) {
  const b = boardFor(map);
  const W = b.width;
  const H = b.height;
  // Corners of the floor diamond.
  const T = proj.pt(MG, MG);
  const R = proj.pt(MG + W * TS, MG);
  const L = proj.pt(MG, MG + H * TS);
  const B = proj.pt(MG + W * TS, MG + H * TS);
  const tiles = b.allTiles();
  const shutterCols = tiles.filter((t) => t.y === 0 && b.terrainId(t) === 'shutter').map((t) => t.x);
  const label = { fontSize: 15, fill: 'var(--frame-ink)', fontFamily: 'var(--display)', letterSpacing: '0.05em', textAnchor: 'middle' } as const;

  return (
    <g className="room" pointerEvents="none">
      <rect x={0} y={0} width={proj.width} height={proj.height} fill="var(--frame)" />
      <ellipse cx={T.x} cy={(T.y + B.y) / 2} rx={(R.x - L.x) / 1.6} ry={(B.y - T.y) / 1.2} fill="url(#lx-iso-halo)" />

      {/* Back walls: the left one rises above column A, the right one above row 1 */}
      <g transform={`matrix(1 -0.5 0 1 ${L.x} ${L.y - ISO_WALL})`}>
        <WallFace length={H * TS} labels={Array.from({ length: H }, (_, i) => ({ at: (H - i - 0.5) * TS, text: `${i + 1}` }))} lamps={[1.5, 5.5]} />
      </g>
      <g transform={`matrix(1 0.5 0 1 ${T.x} ${T.y - ISO_WALL})`}>
        <WallFace length={W * TS} labels={Array.from({ length: W }, (_, i) => ({ at: (i + 0.5) * TS, text: String.fromCharCode(65 + i) }))} lamps={[1.5, 6.5]} door={shutterCols.length ? { from: Math.min(...shutterCols), to: Math.max(...shutterCols) + 1 } : undefined} shade />
      </g>
      {/* The walls' top edges and the corner post where they meet */}
      <path d={`M${L.x} ${L.y - ISO_WALL} L${T.x} ${T.y - ISO_WALL} L${T.x - 12} ${T.y - ISO_WALL - 6} L${L.x - 12} ${L.y - ISO_WALL - 6} Z`} fill="var(--frame-hi)" />
      <path d={`M${T.x} ${T.y - ISO_WALL} L${R.x} ${R.y - ISO_WALL} L${R.x + 12} ${R.y - ISO_WALL - 6} L${T.x + 12} ${T.y - ISO_WALL - 6} Z`} fill="var(--frame-hi)" opacity={0.8} />
      <path d={`M${T.x - 12} ${T.y - ISO_WALL - 6} L${T.x} ${T.y - ISO_WALL - 12} L${T.x + 12} ${T.y - ISO_WALL - 6} L${T.x} ${T.y - ISO_WALL} Z`} fill="var(--wall-hi)" />

      {/* Floor, in floor space */}
      <g transform={proj.floor}>
        <FloorBase map={map} />
        {tiles.map((t) => {
          const id = b.terrainId(t);
          if (id === 'vent') return <Vent key={posKey(t)} t={t} />;
          if (id === 'panel') return <FloorPanel key={posKey(t)} t={t} />;
          return null;
        })}
        <rect x={MG} y={MG} width={W * TS} height={H * TS} fill="url(#lx-vignette)" />
      </g>

      {/* Front lip, with hazard trim, under the two near edges */}
      <path d={`M${L.x} ${L.y} L${B.x} ${B.y} L${B.x} ${B.y + LIP} L${L.x} ${L.y + LIP} Z`} fill="var(--wall-face)" />
      <path d={`M${B.x} ${B.y} L${R.x} ${R.y} L${R.x} ${R.y + LIP} L${B.x} ${B.y + LIP} Z`} fill="var(--wall-face)" />
      <path d={`M${B.x} ${B.y} L${R.x} ${R.y} L${R.x} ${R.y + LIP} L${B.x} ${B.y + LIP} Z`} fill="#000" opacity={0.25} />
      <path d={`M${L.x} ${L.y} L${B.x} ${B.y} L${B.x} ${B.y + 5} L${L.x} ${L.y + 5} Z`} fill="url(#lx-stripes-fine)" opacity={0.85} />
      <path d={`M${B.x} ${B.y} L${R.x} ${R.y} L${R.x} ${R.y + 5} L${B.x} ${B.y + 5} Z`} fill="url(#lx-stripes-fine)" opacity={0.7} />

      {/* Coordinates along the near edges: columns A to H on the left, rows 1 to 8 on the right */}
      <g className="coord">
        {Array.from({ length: W }, (_, x) => {
          const p = proj.pt(MG + x * TS + TS / 2, MG + H * TS);
          return (
            <text key={`cx${x}`} x={p.x - 10} y={p.y + LIP + 20} {...label}>
              {String.fromCharCode(65 + x)}
            </text>
          );
        })}
        {Array.from({ length: H }, (_, y) => {
          const p = proj.pt(MG + W * TS, MG + y * TS + TS / 2);
          return (
            <text key={`cy${y}`} x={p.x + 10} y={p.y + LIP + 20} {...label}>
              {y + 1}
            </text>
          );
        })}
      </g>
    </g>
  );
}

/**
 * One back wall, drawn flat in its own coordinates (x along the wall, y down from its top) and sheared into place by
 * the caller. Seams fall on the tile boundaries, so the wall itself reads as a ruler for the grid.
 */
function WallFace({ length, labels, lamps, door, shade }: { length: number; labels: { at: number; text: string }[]; lamps: number[]; door?: { from: number; to: number }; shade?: boolean }) {
  const h = ISO_WALL;
  const n = Math.round(length / TS);
  return (
    <g>
      <rect width={length} height={h} fill="var(--wall-face)" />
      <rect width={length} height={20} fill="var(--frame)" />
      <path d={`M0 7H${length}M0 13H${length}`} stroke="var(--frame-hi)" strokeWidth={3} />
      {Array.from({ length: n }, (_, i) => (
        <g key={i}>
          <rect x={i * TS + 5} y={26} width={TS - 10} height={h - 52} rx={2} fill="var(--wall-top)" opacity={0.55} stroke="var(--wall-hi)" strokeOpacity={0.5} strokeWidth={1} />
          {i % 3 === 1 && <rect x={i * TS + 14} y={32} width={TS - 28} height={14} fill="url(#lx-grille)" opacity={0.8} />}
        </g>
      ))}
      {Array.from({ length: n + 1 }, (_, i) => (
        <path key={`s${i}`} d={`M${i * TS} 20V${h}`} stroke="var(--frame)" strokeWidth={2} />
      ))}
      {lamps.map((at) => (
        <g key={`l${at}`}>
          <rect x={at * TS - 12} y={22} width={24} height={6} rx={2} fill="#e9f3f6" />
          <ellipse cx={at * TS} cy={34} rx={34} ry={16} fill="url(#lx-glow-white)" opacity={0.18} />
        </g>
      ))}
      {door && (
        <g>
          <rect x={door.from * TS + 6} y={14} width={(door.to - door.from) * TS - 12} height={h - 14} fill="#0b0e10" />
          <rect x={door.from * TS + 12} y={20} width={(door.to - door.from) * TS - 24} height={h - 20} fill="url(#lx-stripes)" opacity={0.8} />
          <circle cx={((door.from + door.to) / 2) * TS} cy={8} r={10} fill="url(#lx-glow-red)" className="lx-lamp" />
          <circle cx={((door.from + door.to) / 2) * TS} cy={8} r={3} fill="#ff4a3a" />
        </g>
      )}
      <rect y={h - 24} width={length} height={14} fill="var(--frame)" opacity={0.85} />
      <rect y={h - 10} width={length} height={10} fill="url(#lx-stripes)" />
      {labels.map((l) => (
        <text key={l.text} x={l.at} y={h - 12.5} textAnchor="middle" fontSize={12} fontFamily="var(--display)" letterSpacing="0.05em" fill="var(--frame-ink)">
          {l.text}
        </text>
      ))}
      {shade && <rect width={length} height={h} fill="#000" opacity={0.18} />}
    </g>
  );
}

// ------------------------------------------------------------------------------------------ blocks

/** The two visible side faces of a block, each in its own flat coordinates (x along the face, y down from its top). */
function faceTransforms(c: XY, hw: number, hh: number, h: number) {
  return {
    left: `matrix(1 0.5 0 1 ${c.x - hw} ${c.y - h})`,
    right: `matrix(1 -0.5 0 1 ${c.x} ${c.y + hh - h})`,
  };
}

/** A terrain block on tile t, standing up from the floor. Drawn in depth order with units and objects. */
export function IsoBlock({ map, t, id, proj, faded }: { map: MapFile; t: Pos; id: string; proj: Projection; faded: boolean }) {
  const spec = BLOCK[id];
  if (!spec || !BLOCK_TERRAIN.includes(id)) return null;
  const c = proj.at(t.x, t.y);
  const { h, inset } = spec;
  const hw = TS - 2 * inset;
  const hh = TS / 2 - inset;
  const f = faceTransforms(c, hw, hh, h);
  // The top face is the floor-space drawing of the footprint, lifted by the block's height.
  const top = `translate(0,${-h}) ${proj.floor}`;
  const x = tx(t.x) + inset;
  const y = ty(t.y) + inset;
  const s = TS - 2 * inset;
  const shade = <rect width={hw} height={h} fill="#000" opacity={0.28} />;
  let body;
  switch (id) {
    case 'wall':
      body = (
        <>
          <g transform={f.left}>
            <rect width={hw} height={h} fill="var(--wall-face)" />
            <rect x={6} y={8} width={hw - 12} height={h - 26} fill="none" stroke="var(--wall-hi)" strokeOpacity={0.45} strokeWidth={1.5} />
            <rect y={h - 12} width={hw} height={7} fill="url(#lx-stripes)" />
          </g>
          <g transform={f.right}>
            <rect width={hw} height={h} fill="var(--wall-face)" />
            <rect x={6} y={8} width={hw - 12} height={h - 26} fill="none" stroke="var(--wall-hi)" strokeOpacity={0.45} strokeWidth={1.5} />
            <rect y={h - 12} width={hw} height={7} fill="url(#lx-stripes)" />
            {shade}
          </g>
          <g transform={top}>
            <rect x={x} y={y} width={s} height={s} rx={2} fill="var(--wall-top)" stroke="var(--wall-hi)" strokeWidth={1.5} />
            <path d={`M${x + 6} ${y + 6} L${x + s - 6} ${y + s - 6} M${x + s - 6} ${y + 6} L${x + 6} ${y + s - 6}`} stroke="var(--wall-face)" strokeWidth={4} opacity={0.75} />
            <rect x={x + 6} y={y + 6} width={s - 12} height={s - 12} fill="none" stroke="var(--wall-face)" strokeWidth={2} opacity={0.75} />
          </g>
        </>
      );
      break;
    case 'pillar':
      body = (
        <>
          <g transform={f.left}>
            <rect width={hw} height={h} fill="var(--pillar-face)" />
            <path d={`M0 1H${hw}`} stroke="var(--pillar-hi)" strokeWidth={2} />
            <path d={`M${hw / 2} 8V${h - 16}`} stroke="var(--pillar-top)" strokeOpacity={0.35} strokeWidth={1.5} />
            <rect y={h - 14} width={hw} height={7} fill="url(#lx-stripes)" opacity={0.85} />
          </g>
          <g transform={f.right}>
            <rect width={hw} height={h} fill="var(--pillar-face)" />
            <path d={`M${hw / 2} 8V${h - 16}`} stroke="var(--pillar-top)" strokeOpacity={0.35} strokeWidth={1.5} />
            <rect y={h - 14} width={hw} height={7} fill="url(#lx-stripes)" opacity={0.85} />
            {shade}
          </g>
          <g transform={top}>
            <rect x={x} y={y} width={s} height={s} rx={4} fill="url(#lx-pillar-top)" stroke="var(--pillar-hi)" strokeWidth={1} />
            <rect x={x + 6} y={y + 6} width={s - 12} height={s - 12} rx={3} fill="none" stroke="var(--pillar-face)" strokeWidth={1.5} opacity={0.6} />
            {[
              [x + 5, y + 5],
              [x + s - 5, y + 5],
              [x + 5, y + s - 5],
              [x + s - 5, y + s - 5],
            ].map(([a, b]) => (
              <circle key={`${a}-${b}`} cx={a} cy={b} r={1.6} fill="var(--pillar-face)" />
            ))}
          </g>
        </>
      );
      break;
    case 'shutter':
      body = (
        <>
          {/* The face toward the room is the closed shutter itself */}
          <g transform={f.left}>
            <rect width={hw} height={h} fill="var(--wall-face)" />
            <rect x={3} y={6} width={hw - 6} height={h - 14} fill="url(#lx-steel)" />
            {Array.from({ length: Math.floor((h - 22) / 6) }, (_, i) => (
              <path key={i} d={`M3 ${12 + i * 6}H${hw - 3}`} stroke="#1b2125" strokeWidth={1.5} opacity={0.8} />
            ))}
            <rect x={3} y={h - 14} width={hw - 6} height={8} fill="url(#lx-stripes)" />
            <rect x={3} y={6} width={hw - 6} height={h - 12} fill="none" stroke="#0b0e10" strokeWidth={2} />
            <text x={hw / 2} y={h / 2 + 2} textAnchor="middle" fontFamily="var(--display)" fontSize={10} letterSpacing="0.14em" fill="#e8ecee" opacity={0.8}>
              SHUTTER
            </text>
          </g>
          <g transform={f.right}>
            <rect width={hw} height={h} fill="var(--wall-face)" />
            {shade}
          </g>
          <g transform={top}>
            <rect x={x} y={y} width={s} height={s} fill="var(--wall-face)" />
            <rect x={x} y={y + s - 6} width={s} height={6} fill="url(#lx-stripes-fine)" opacity={0.7} />
          </g>
          <circle cx={c.x} cy={c.y - h - 4} r={10} fill="url(#lx-glow-red)" className="lx-lamp" />
          <circle cx={c.x} cy={c.y - h - 4} r={2.8} fill="#ff4a3a" />
        </>
      );
      break;
    case 'gun': {
      const dir = gunDirections(map).get(posKey(t)) ?? 'W';
      body = (
        <>
          <g transform={f.left}>
            <rect width={hw} height={h} fill="var(--wall-face)" />
            <rect y={h - 6} width={hw} height={4} fill="url(#lx-stripes-fine)" />
          </g>
          <g transform={f.right}>
            <rect width={hw} height={h} fill="var(--wall-face)" />
            <rect y={h - 6} width={hw} height={4} fill="url(#lx-stripes-fine)" />
            {shade}
          </g>
          {/* The gun itself lies on its plinth, drawn as on the top-down board */}
          <g transform={top}>
            <rect x={x} y={y} width={s} height={s} fill="var(--wall-top)" />
            <WallGun t={t} dir={dir} />
          </g>
        </>
      );
      break;
    }
  }
  return (
    <g className="iso-block" data-tile={tileName(t)} style={{ opacity: faded ? 0.3 : 1 }}>
      {body}
    </g>
  );
}
