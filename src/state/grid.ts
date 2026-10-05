import type { MapFile, TerrainDef } from '../content/types';
import { parseTile, type Pos } from '../util/tiles';

export const N: Pos = { x: 0, y: -1 };
export const E: Pos = { x: 1, y: 0 };
export const S: Pos = { x: 0, y: 1 };
export const W: Pos = { x: -1, y: 0 };
/** Fixed order for every 4-direction scan: north, east, south, west. */
export const DIRS4: readonly Pos[] = [N, E, S, W];
export const DIR_BY_NAME: Record<'N' | 'E' | 'S' | 'W', Pos> = { N, E, S, W };
/** The 8 neighbours in row-major order (lowest row, then lowest column). */
export const DIRS8: readonly Pos[] = [
  { x: -1, y: -1 }, { x: 0, y: -1 }, { x: 1, y: -1 },
  { x: -1, y: 0 }, { x: 1, y: 0 },
  { x: -1, y: 1 }, { x: 0, y: 1 }, { x: 1, y: 1 },
];

export const samePos = (a: Pos, b: Pos): boolean => a.x === b.x && a.y === b.y;
export const addPos = (a: Pos, d: Pos, k = 1): Pos => ({ x: a.x + d.x * k, y: a.y + d.y * k });
/** Range distance: the larger of the column and row difference. */
export const cheb = (a: Pos, b: Pos): number => Math.max(Math.abs(a.x - b.x), Math.abs(a.y - b.y));
export const manhattan = (a: Pos, b: Pos): number => Math.abs(a.x - b.x) + Math.abs(a.y - b.y);
/** Row-major order: lowest row, then lowest column. */
export const comparePos = (a: Pos, b: Pos): number => a.y - b.y || a.x - b.x;
export const dirName = (d: Pos): string => (d.y < 0 ? 'N' : d.x > 0 ? 'E' : d.y > 0 ? 'S' : 'W');

/** Standard all-octant integer Bresenham from a to b, both endpoints included. */
export function bresenham(a: Pos, b: Pos): Pos[] {
  const pts: Pos[] = [];
  let x0 = a.x;
  let y0 = a.y;
  const dx = Math.abs(b.x - x0);
  const sx = x0 < b.x ? 1 : -1;
  const dy = -Math.abs(b.y - y0);
  const sy = y0 < b.y ? 1 : -1;
  let err = dx + dy;
  for (;;) {
    pts.push({ x: x0, y: y0 });
    if (x0 === b.x && y0 === b.y) break;
    const e2 = 2 * err;
    if (e2 >= dy) {
      err += dy;
      x0 += sx;
    }
    if (e2 <= dx) {
      err += dx;
      y0 += sy;
    }
  }
  return pts;
}

/**
 * The tile list of the line between a and b, ordered from a to b.
 * The line is always traced from the tile with the lower row, then the lower column, and the same list is
 * used in both directions, so sight and projectile paths are symmetric.
 */
export function lineTiles(a: Pos, b: Pos): Pos[] {
  const aFirst = a.y < b.y || (a.y === b.y && a.x <= b.x);
  return aFirst ? bresenham(a, b) : bresenham(b, a).reverse();
}

export class Board {
  readonly width: number;
  readonly height: number;
  private readonly terrain: TerrainDef[];
  private readonly terrainIds: string[];

  constructor(map: MapFile) {
    this.width = map.width;
    this.height = map.height;
    this.terrain = [];
    this.terrainIds = [];
    for (let y = 0; y < map.height; y++) {
      for (let x = 0; x < map.width; x++) {
        const id = map.legend[map.rows[y]![x]!]!;
        this.terrainIds.push(id);
        this.terrain.push(map.terrain[id]!);
      }
    }
  }

  inBounds(p: Pos): boolean {
    return p.x >= 0 && p.y >= 0 && p.x < this.width && p.y < this.height;
  }
  idx(p: Pos): number {
    return p.y * this.width + p.x;
  }
  pos(i: number): Pos {
    return { x: i % this.width, y: Math.floor(i / this.width) };
  }
  terrainId(p: Pos): string {
    return this.terrainIds[this.idx(p)]!;
  }
  /** Out-of-bounds counts as blocking everything. */
  blocksMove(p: Pos): boolean {
    return !this.inBounds(p) || this.terrain[this.idx(p)]!.blocksMove;
  }
  blocksSight(p: Pos): boolean {
    return !this.inBounds(p) || this.terrain[this.idx(p)]!.blocksSight;
  }
  blocksProjectile(p: Pos): boolean {
    return !this.inBounds(p) || this.terrain[this.idx(p)]!.blocksProjectile;
  }
  /** Line of sight: no pillar or wall strictly between the two tiles. Symmetric by construction. */
  hasLOS(a: Pos, b: Pos): boolean {
    const pts = lineTiles(a, b);
    for (let i = 1; i < pts.length - 1; i++) if (this.blocksSight(pts[i]!)) return false;
    return true;
  }
  /** Tiles within Chebyshev radius of the centre, in bounds, row-major. Includes the centre. */
  square(center: Pos, radius: number): Pos[] {
    const out: Pos[] = [];
    for (let y = center.y - radius; y <= center.y + radius; y++) {
      for (let x = center.x - radius; x <= center.x + radius; x++) {
        const p = { x, y };
        if (this.inBounds(p)) out.push(p);
      }
    }
    return out;
  }
  neighbors8(p: Pos): Pos[] {
    return DIRS8.map((d) => addPos(p, d)).filter((q) => this.inBounds(q));
  }
  allTiles(): Pos[] {
    const out: Pos[] = [];
    for (let y = 0; y < this.height; y++) for (let x = 0; x < this.width; x++) out.push({ x, y });
    return out;
  }
}

const boards = new WeakMap<MapFile, Board>();
export function boardFor(map: MapFile): Board {
  let b = boards.get(map);
  if (!b) {
    b = new Board(map);
    boards.set(map, b);
  }
  return b;
}

export { parseTile };
