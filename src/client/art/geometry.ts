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
