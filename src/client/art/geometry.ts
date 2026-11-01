import type { Pos } from '../../state/types';

/** Board drawing units: one tile is TS pixels, and the room's wall frame around the grid is MG pixels thick. */
export const TS = 64;
export const MG = 30;

/** Centre of a tile's column or row, in board pixels. */
export const cx = (x: number) => MG + x * TS + TS / 2;
export const cy = (y: number) => MG + y * TS + TS / 2;
/** Top-left corner of a tile. */
export const tx = (x: number) => MG + x * TS;
export const ty = (y: number) => MG + y * TS;

export const posKey = (p: Pos) => `${p.x},${p.y}`;

/**
 * A fixed pseudo-random number per tile, for decoration only (grime, wear, jitter in an arc). The same tile always gets
 * the same value, so the room looks the same on every attempt and in every replay.
 */
export function tileNoise(x: number, y: number, salt = 0): number {
  let h = (Math.imul(x + 17, 374761393) + Math.imul(y + 31, 668265263) + Math.imul(salt + 7, 2246822519)) >>> 0;
  h = Math.imul(h ^ (h >>> 13), 1274126177) >>> 0;
  return ((h ^ (h >>> 16)) >>> 0) / 4294967296;
}

// ------------------------------------------------------------------------------------------ projections

/** How the fight board is drawn: from straight above (the default), or tilted to a three-quarter isometric view. */
export type BoardView = 'top' | 'iso';

export const isBoardView = (v: unknown): v is BoardView => v === 'top' || v === 'iso';

export interface XY {
  x: number;
  y: number;
}

/** Where a unit stands in its own drawing: every figure in units.tsx is drawn around (0, 0) with its feet at y = FOOT. */
export const FOOT = 19;

/**
 * Maps the board onto the screen. Everything on the floor is drawn in floor space, the top-down board's own pixels (tile
 * (x, y) covers MG + x * TS to MG + (x + 1) * TS), and placed by the projection; upright things (units, objects, text,
 * chips) are drawn front-on at projected points so they never skew. The top-down projection is the identity, so the
 * default view draws exactly as it did before the isometric view existed.
 */
export interface Projection {
  readonly view: BoardView;
  /** The board's size in drawing units (the SVG's viewBox). */
  readonly width: number;
  readonly height: number;
  /** Scale of upright art (units, objects and their chips) relative to the top-down board. */
  readonly k: number;
  /** The SVG transform from floor space to the screen, or undefined when they are the same. */
  readonly floor: string | undefined;
  /** A point in floor space, on the screen. */
  pt(fx: number, fy: number): XY;
  /** A tile's centre on the floor. Fractional tiles are allowed (a unit halfway through a slide). */
  at(x: number, y: number): XY;
  /**
   * A point given in a unit's own drawing units (centred on its tile, as the top-down board draws it) for a unit on tile
   * (x, y). Overlays that sit on a unit (damage numbers, hit badges, intent tags) use this, so they follow the figure.
   */
  near(x: number, y: number, dx: number, dy: number): XY;
  /** The transform, after a translation to the tile's centre, that draws a unit's own drawing upright on the floor. */
  readonly upright: string | undefined;
  /** Draw order for upright things: larger is nearer the viewer. */
  depth(x: number, y: number): number;
  /** A tile's outline on the screen, inset by `inset` floor pixels, as SVG polygon points. */
  outline(x: number, y: number, inset?: number): string;
}

/** Isometric tile: a diamond ISO_W wide and ISO_W / 2 tall, made by `matrix(1 0.5 -1 0.5)` from a TS-pixel square. */
const ISO_HALF_W = TS;
/** The back walls' height above the floor, and the margins around the room, in drawing units. */
export const ISO_WALL = 84;
const ISO_TOP = ISO_WALL + 16;
const ISO_SIDE = 26;
const ISO_BOTTOM = 58;
/** Upright art is this much larger than on the top-down board, relative to a tile (the concept art's scale). */
const ISO_K = 1.3;

const fmt = (n: number) => +n.toFixed(2);

function topDown(width: number, height: number): Projection {
  return {
    view: 'top',
    width: MG * 2 + width * TS,
    height: MG * 2 + height * TS,
    k: 1,
    floor: undefined,
    upright: undefined,
    pt: (fx, fy) => ({ x: fx, y: fy }),
    at: (x, y) => ({ x: cx(x), y: cy(y) }),
    near: (x, y, dx, dy) => ({ x: cx(x) + dx, y: cy(y) + dy }),
    depth: (x, y) => x + y,
    outline: (x, y, inset = 0) => {
      const [x0, y0, x1, y1] = [tx(x) + inset, ty(y) + inset, tx(x) + TS - inset, ty(y) + TS - inset];
      return `${x0},${y0} ${x1},${y0} ${x1},${y1} ${x0},${y1}`;
    },
  };
}

function isometric(width: number, height: number): Projection {
  // The floor is a diamond: grid corner (0, 0) at the top, (width, 0) on the right, (0, height) on the left.
  const ox = ISO_SIDE + height * ISO_HALF_W;
  const oy = ISO_TOP;
  const pt = (fx: number, fy: number): XY => ({ x: ox + (fx - MG) - (fy - MG), y: oy + (fx - MG + fy - MG) / 2 });
  const at = (x: number, y: number) => pt(MG + x * TS + TS / 2, MG + y * TS + TS / 2);
  return {
    view: 'iso',
    width: ISO_SIDE * 2 + (width + height) * ISO_HALF_W,
    height: ISO_TOP + ((width + height) * ISO_HALF_W) / 2 + ISO_BOTTOM,
    k: ISO_K,
    floor: `matrix(1 0.5 -1 0.5 ${ox} ${oy - MG})`,
    upright: `scale(${ISO_K}) translate(0,${-FOOT})`,
    pt,
    at,
    near: (x, y, dx, dy) => {
      const c = at(x, y);
      return { x: c.x + dx * ISO_K, y: c.y + (dy - FOOT) * ISO_K };
    },
    depth: (x, y) => x + y,
    outline: (x, y, inset = 0) =>
      [
        [tx(x) + inset, ty(y) + inset],
        [tx(x) + TS - inset, ty(y) + inset],
        [tx(x) + TS - inset, ty(y) + TS - inset],
        [tx(x) + inset, ty(y) + TS - inset],
      ]
        .map(([a, b]) => {
          const p = pt(a!, b!);
          return `${fmt(p.x)},${fmt(p.y)}`;
        })
        .join(' '),
  };
}

const cache = new Map<string, Projection>();

/** The projection for a board of the given size. Cached, so components can compare projections by identity. */
export function projection(view: BoardView, width: number, height: number): Projection {
  const k = `${view}:${width}x${height}`;
  let p = cache.get(k);
  if (!p) {
    p = view === 'iso' ? isometric(width, height) : topDown(width, height);
    cache.set(k, p);
  }
  return p;
}
