import type { DirectionName, MapFile } from '../../content/types';
import { boardFor, parseTile } from '../../state/grid';
import type { Pos } from '../../state/types';
import { MG, TS, posKey, tileNoise, tx, ty } from './geometry';

const ROT: Record<DirectionName, number> = { W: 0, N: 90, E: 180, S: 270 };

/**
 * The room itself: the wall frame with its coordinates, the riveted floor, wear and lighting, and the terrain features
 * (walls, pillars, shutters, the wall gun, vents and floor panels). Drawn from the map file, so a different layout
 * draws itself. Hazard cues are not drawn here: they depend on the fight state and are the board's job.
 */
export function Room({ map, coords = true }: { map: MapFile; coords?: boolean }) {
  const b = boardFor(map);
  const W = MG * 2 + b.width * TS;
  const H = MG * 2 + b.height * TS;
  const tiles = b.allTiles();
  const gunDir = gunDirections(map);

  return (
    <g className="room" pointerEvents="none">
      {/* Frame: the room's outer walls, with hazard trim and stencilled coordinates */}
      <rect x={0} y={0} width={W} height={H} rx={10} fill="var(--frame)" />
      <rect x={3} y={3} width={W - 6} height={H - 6} rx={8} fill="none" stroke="var(--frame-hi)" strokeWidth={1.5} />
      <rect x={MG - 5} y={MG - 5} width={b.width * TS + 10} height={b.height * TS + 10} rx={3} fill="url(#lx-stripes-fine)" opacity={0.85} />
      {[
        [8, 8],
        [W - 8, 8],
        [8, H - 8],
        [W - 8, H - 8],
      ].map(([x, y]) => (
        <circle key={`${x}-${y}`} cx={x} cy={y} r={2.4} fill="var(--frame-hi)" />
      ))}
      {coords && <Coords w={b.width} h={b.height} W={W} H={H} />}

      <FloorBase map={map} />

      {tiles.map((t) => {
        const id = b.terrainId(t);
        switch (id) {
          case 'wall':
            return <WallBlock key={posKey(t)} t={t} />;
          case 'pillar':
            return <Pillar key={posKey(t)} t={t} />;
          case 'shutter':
            return <Shutter key={posKey(t)} t={t} />;
          case 'gun':
            return <WallGun key={posKey(t)} t={t} dir={gunDir.get(posKey(t)) ?? 'W'} />;
          case 'vent':
            return <Vent key={posKey(t)} t={t} />;
          case 'panel':
            return <FloorPanel key={posKey(t)} t={t} />;
          default:
            return null;
        }
      })}

      {/* Edge shading where the floor meets the frame */}
      <rect x={MG} y={MG} width={b.width * TS} height={b.height * TS} fill="url(#lx-vignette)" />
    </g>
  );
}

/** Terrain drawn as a solid block on the isometric board; everything else lies on the floor. */
export const BLOCK_TERRAIN: readonly string[] = ['wall', 'pillar', 'shutter', 'gun'];

/** The direction each wall gun faces, by tile. */
export function gunDirections(map: MapFile): Map<string, DirectionName> {
  const out = new Map<string, DirectionName>();
  for (const h of map.hazards) if (h.direction) for (const t of h.tiles) out.set(posKey(parseTile(t)), h.direction);
  return out;
}

/**
 * The floor in floor space: plates, tread, grime, painted markings, the ceiling lamps' pools of light and the soft
 * shadows of walls and pillars. Shared by both views: the isometric board draws it through its floor transform.
 */
export function FloorBase({ map }: { map: MapFile }) {
  const b = boardFor(map);
  const tiles = b.allTiles();
  const floorTiles = tiles.filter((t) => !BLOCK_TERRAIN.includes(b.terrainId(t)));
  return (
    <>
      {/* Floor */}
      <g transform={`translate(${MG},${MG})`}>
        <rect width={b.width * TS} height={b.height * TS} fill="url(#lx-plate)" />
        {floorTiles
          .filter((t) => (t.x + t.y) % 2 === 1)
          .map((t) => (
            <rect key={`tr${posKey(t)}`} x={t.x * TS + 4} y={t.y * TS + 4} width={TS - 8} height={TS - 8} fill="url(#lx-tread)" opacity={0.6} />
          ))}
      </g>
      {floorTiles.map((t) => (
        <Grime key={`gr${posKey(t)}`} t={t} />
      ))}
      <FloorMarkings map={map} />

      {/* Ceiling lamps throw soft pools of light */}
      {[
        [2, 2],
        [5.5, 3.5],
        [2.5, 6.5],
      ].map(([x, y]) => (
        <ellipse key={`lp${x}-${y}`} cx={MG + x! * TS} cy={MG + y! * TS} rx={TS * 2.2} ry={TS * 1.8} fill="url(#lx-pool)" />
      ))}

      {/* Shadows first, so every feature casts onto the floor beneath its neighbours */}
      {tiles.map((t) => {
        const id = b.terrainId(t);
        if (id !== 'wall' && id !== 'pillar') return null;
        return <rect key={`sh${posKey(t)}`} x={tx(t.x) + 8} y={ty(t.y) + 12} width={TS - 4} height={TS - 6} rx={8} fill="#000" opacity={0.4} filter="url(#lx-soft)" />;
      })}
    </>
  );
}

function Coords({ w, h, W, H }: { w: number; h: number; W: number; H: number }) {
  const label = { fontSize: 13, fill: 'var(--frame-ink)', fontFamily: 'var(--display)', letterSpacing: '0.05em' } as const;
  return (
    <g className="coord">
      {Array.from({ length: w }, (_, x) => (
        <g key={`cx${x}`}>
          <text x={MG + x * TS + TS / 2} y={MG - 10} textAnchor="middle" {...label}>
            {String.fromCharCode(65 + x)}
          </text>
          <text x={MG + x * TS + TS / 2} y={H - 9} textAnchor="middle" {...label}>
            {String.fromCharCode(65 + x)}
          </text>
        </g>
      ))}
      {Array.from({ length: h }, (_, y) => (
        <g key={`cy${y}`}>
          <text x={MG / 2 - 1} y={MG + y * TS + TS / 2 + 5} textAnchor="middle" {...label}>
            {y + 1}
          </text>
          <text x={W - MG / 2 + 1} y={MG + y * TS + TS / 2 + 5} textAnchor="middle" {...label}>
            {y + 1}
          </text>
        </g>
      ))}
    </g>
  );
}

function Grime({ t }: { t: Pos }) {
  const n = tileNoise(t.x, t.y);
  if (n > 0.45) return null;
  const ox = tileNoise(t.x, t.y, 1) * 36 + 14;
  const oy = tileNoise(t.x, t.y, 2) * 36 + 14;
  const r = 8 + tileNoise(t.x, t.y, 3) * 14;
  return (
    <g opacity={0.32}>
      <ellipse cx={tx(t.x) + ox} cy={ty(t.y) + oy} rx={r} ry={r * 0.7} fill="var(--grime)" opacity={0.35} transform={`rotate(${n * 360} ${tx(t.x) + ox} ${ty(t.y) + oy})`} />
      {n < 0.18 && <path d={`M${tx(t.x) + 10} ${ty(t.y) + oy} l${18 + n * 40} ${n * 30 - 4}`} stroke="var(--floor-hi)" strokeWidth={1} opacity={0.6} />}
    </g>
  );
}

/** Painted floor markings: decoration only, nothing that reveals what the lab hides (spawn tiles, hazard reach). */
function FloorMarkings({ map }: { map: MapFile }) {
  const b = boardFor(map);
  const shutters = b.allTiles().filter((t) => b.terrainId(t) === 'shutter');
  const start = parseTile(map.playerStart);
  return (
    <g>
      {/* A threshold line on the floor in front of each shutter */}
      {shutters.map((t) => (
        <rect key={`th${posKey(t)}`} x={tx(t.x) + 2} y={ty(t.y + 1) + 2} width={TS - 4} height={5} fill="url(#lx-stripes-fine)" opacity={0.55} />
      ))}
      {/* The bay the intruder wakes in */}
      <rect x={tx(start.x) + 5} y={ty(start.y) + 5} width={TS - 10} height={TS - 10} rx={3} fill="none" stroke="var(--paint)" strokeWidth={2} strokeDasharray="8 5" opacity={0.45} />
      <text
        x={MG + b.width * TS - 12}
        y={MG + b.height * TS - 14}
        textAnchor="end"
        fontFamily="var(--display)"
        fontSize={30}
        letterSpacing="0.12em"
        fill="var(--paint)"
        opacity={0.13}
      >
        {map.name.toUpperCase()}
      </text>
    </g>
  );
}

function WallBlock({ t }: { t: Pos }) {
  const x = tx(t.x);
  const y = ty(t.y);
  return (
    <g>
      {/* A load-bearing block: the front face below, the braced top seen from above */}
      <rect x={x} y={y + 8} width={TS} height={TS - 8} fill="var(--wall-face)" />
      <rect x={x} y={y + TS - 10} width={TS} height={6} fill="url(#lx-stripes)" />
      <rect x={x + 1} y={y - 2} width={TS - 2} height={TS - 12} rx={2} fill="var(--wall-top)" stroke="var(--wall-hi)" strokeWidth={1.5} />
      <path d={`M${x + 6} ${y + 4} L${x + TS - 6} ${y + TS - 20} M${x + TS - 6} ${y + 4} L${x + 6} ${y + TS - 20}`} stroke="var(--wall-face)" strokeWidth={4} opacity={0.75} />
      <rect x={x + 6} y={y + 4} width={TS - 12} height={TS - 24} fill="none" stroke="var(--wall-face)" strokeWidth={2} opacity={0.75} />
      {[
        [x + 6, y + 4],
        [x + TS - 6, y + 4],
        [x + 6, y + TS - 20],
        [x + TS - 6, y + TS - 20],
      ].map(([a, c]) => (
        <circle key={`${a}-${c}`} cx={a} cy={c} r={2} fill="var(--wall-hi)" />
      ))}
    </g>
  );
}

function Pillar({ t }: { t: Pos }) {
  const x = tx(t.x);
  const y = ty(t.y);
  return (
    <g>
      {/* Front face, then the top seen from above */}
      <rect x={x + 8} y={y + 18} width={TS - 16} height={TS - 22} rx={4} fill="var(--pillar-face)" />
      <rect x={x + 8} y={y + TS - 16} width={TS - 16} height={6} fill="url(#lx-stripes)" opacity={0.85} />
      <rect x={x + 8} y={y + 4} width={TS - 16} height={TS - 22} rx={5} fill="url(#lx-pillar-top)" stroke="var(--pillar-hi)" strokeWidth={1} />
      <rect x={x + 14} y={y + 10} width={TS - 28} height={TS - 34} rx={3} fill="none" stroke="var(--pillar-face)" strokeWidth={1.5} opacity={0.6} />
      {[
        [x + 13, y + 9],
        [x + TS - 13, y + 9],
        [x + 13, y + TS - 23],
        [x + TS - 13, y + TS - 23],
      ].map(([a, c]) => (
        <circle key={`${a}-${c}`} cx={a} cy={c} r={1.6} fill="var(--pillar-face)" />
      ))}
    </g>
  );
}

function Shutter({ t }: { t: Pos }) {
  const x = tx(t.x);
  const y = ty(t.y);
  return (
    <g>
      <rect x={x} y={y} width={TS} height={TS} fill="var(--wall-face)" />
      <rect x={x + 3} y={y + 6} width={TS - 6} height={TS - 16} fill="url(#lx-steel)" />
      {[0, 1, 2, 3, 4, 5].map((i) => (
        <line key={i} x1={x + 3} x2={x + TS - 3} y1={y + 12 + i * 7} y2={y + 12 + i * 7} stroke="#1b2125" strokeWidth={1.5} opacity={0.8} />
      ))}
      <rect x={x + 3} y={y + TS - 14} width={TS - 6} height={8} fill="url(#lx-stripes)" />
      <rect x={x + 3} y={y + 6} width={TS - 6} height={TS - 12} fill="none" stroke="#0b0e10" strokeWidth={2} />
      <text x={x + TS / 2} y={y + 33} textAnchor="middle" fontFamily="var(--display)" fontSize={10} letterSpacing="0.14em" fill="#e8ecee" opacity={0.75}>
        SHUTTER
      </text>
      <circle cx={x + TS / 2} cy={y + 4} r={9} fill="url(#lx-glow-red)" className="lx-lamp" />
      <circle cx={x + TS / 2} cy={y + 4} r={2.6} fill="#ff4a3a" />
    </g>
  );
}

export function WallGun({ t, dir }: { t: Pos; dir: DirectionName }) {
  const x = tx(t.x);
  const y = ty(t.y);
  const c = TS / 2;
  return (
    <g transform={`translate(${x},${y}) rotate(${ROT[dir]} ${c} ${c})`}>
      {/* Mount bolted to the wall behind it */}
      <rect x={TS - 22} y={2} width={22} height={TS - 4} rx={3} fill="var(--wall-face)" />
      <rect x={TS - 20} y={6} width={18} height={TS - 12} rx={2} fill="url(#lx-steel)" stroke="#0b0e10" strokeWidth={1.5} />
      {[10, TS - 10].map((by) => (
        <circle key={by} cx={TS - 11} cy={by} r={1.8} fill="#9aa6ad" />
      ))}
      <rect x={TS - 32} y={c - 13} width={14} height={26} rx={3} fill="#2a3237" stroke="#0b0e10" strokeWidth={1.5} />
      {/* Ammo feed */}
      <path d={`M${TS - 26} ${c + 13} q-2 8 6 12`} fill="none" stroke="#b8902e" strokeWidth={3} strokeDasharray="2 1.5" />
      {/* Housing and barrel */}
      <circle cx={c + 4} cy={c} r={14} fill="url(#lx-steel)" stroke="#0b0e10" strokeWidth={2} />
      <circle cx={c + 4} cy={c} r={7} fill="#1a2024" />
      <circle cx={c + 8} cy={c - 6} r={2.2} fill="#ff4a3a" />
      <rect x={4} y={c - 4.5} width={c - 2} height={9} rx={2} fill="#2a3237" stroke="#0b0e10" strokeWidth={1.5} />
      <rect x={2} y={c - 6} width={7} height={12} rx={2} fill="#1a2024" stroke="#0b0e10" strokeWidth={1.5} />
      <line x1={12} x2={c - 2} y1={c - 1.5} y2={c - 1.5} stroke="#6d7a82" strokeWidth={1} />
    </g>
  );
}

export function Vent({ t }: { t: Pos }) {
  const x = tx(t.x) + TS / 2;
  const y = ty(t.y) + TS / 2;
  const id = `vent-clip-${t.x}-${t.y}`;
  return (
    <g>
      <clipPath id={id}>
        <circle cx={x} cy={y} r={17} />
      </clipPath>
      <circle cx={x + 2} cy={y + 3} r={23} fill="#000" opacity={0.25} />
      <circle cx={x} cy={y} r={22} fill="url(#lx-steel)" stroke="#0b0e10" strokeWidth={1.5} />
      <circle cx={x} cy={y} r={17} fill="#0c0f11" />
      {/* The fan below the grate */}
      <g clipPath={`url(#${id})`} className="lx-fan" style={{ transformOrigin: `${x}px ${y}px` }}>
        {[0, 72, 144, 216, 288].map((a) => (
          <path key={a} d={`M${x} ${y} l14 -4 a14 14 0 0 1 -6 10 z`} fill="#2c353a" transform={`rotate(${a} ${x} ${y})`} />
        ))}
      </g>
      <g clipPath={`url(#${id})`}>
        {[-12, -6, 0, 6, 12].map((d) => (
          <rect key={d} x={x - 18} y={y + d - 1.5} width={36} height={3} fill="#5b676e" />
        ))}
      </g>
      <circle cx={x} cy={y} r={17} fill="none" stroke="#0b0e10" strokeWidth={2} />
      {[45, 135, 225, 315].map((a) => (
        <circle key={a} cx={x + Math.cos((a * Math.PI) / 180) * 19.5} cy={y + Math.sin((a * Math.PI) / 180) * 19.5} r={1.4} fill="#c8d0d4" />
      ))}
    </g>
  );
}

export function FloorPanel({ t }: { t: Pos }) {
  const x = tx(t.x);
  const y = ty(t.y);
  return (
    <g>
      <rect x={x + 3} y={y + 3} width={TS - 6} height={TS - 6} rx={3} fill="url(#lx-stripes-fine)" />
      <rect x={x + 8} y={y + 8} width={TS - 16} height={TS - 16} rx={2} fill="#14191c" />
      {[0, 1, 2, 3].map((i) => (
        <g key={i}>
          <line x1={x + 12 + i * 13} x2={x + 12 + i * 13} y1={y + 10} y2={y + TS - 10} stroke="var(--copper)" strokeWidth={1.6} opacity={0.75} />
          <line y1={y + 12 + i * 13} y2={y + 12 + i * 13} x1={x + 10} x2={x + TS - 10} stroke="var(--copper)" strokeWidth={1.6} opacity={0.75} />
        </g>
      ))}
      <path
        d={`M${x + 35} ${y + 15} L${x + 26} ${y + 33} L${x + 33} ${y + 33} L${x + 28} ${y + 49} L${x + 40} ${y + 29} L${x + 33} ${y + 29} Z`}
        fill="#f2b01e"
        stroke="#16181a"
        strokeWidth={1.5}
        opacity={0.9}
      />
    </g>
  );
}
