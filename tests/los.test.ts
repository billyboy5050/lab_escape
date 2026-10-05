import { describe, expect, it } from 'vitest';
import { boardFor, bresenham, lineTiles } from '../src/state/grid';
import { C, T, name } from './helpers';

const board = boardFor(C.map);
const tiles = board.allTiles();

/** Sight with a plain trace from a to b (what the spec warns against). */
function plainLOS(a: { x: number; y: number }, b: { x: number; y: number }): boolean {
  const pts = bresenham(a, b);
  for (let i = 1; i < pts.length - 1; i++) if (board.blocksSight(pts[i]!)) return false;
  return true;
}

describe('line of sight', () => {
  it('is symmetric for every pair of tiles on Armory Lockdown', () => {
    for (const a of tiles) for (const b of tiles) expect(board.hasLOS(a, b), `${name(a)} <-> ${name(b)}`).toBe(board.hasLOS(b, a));
  });

  it('uses the same tile list in both directions', () => {
    for (const a of tiles) {
      for (const b of tiles) {
        const ab = lineTiles(a, b).map(name);
        const ba = lineTiles(b, a).map(name).reverse();
        expect(ab).toEqual(ba);
      }
    }
  });

  it('starts every line at the tile it is traced from', () => {
    expect(name(lineTiles(T('B8'), T('C3'))[0]!)).toBe('B8');
    expect(name(lineTiles(T('C3'), T('B8'))[0]!)).toBe('C3');
  });

  it('a plain trace disagrees by direction for 88 tile pairs on this map (the reason for the rule)', () => {
    // The spec's figure counts pairs of tiles that do not themselves block sight (64 minus 2 walls and 3 pillars).
    const open = tiles.filter((p) => !board.blocksSight(p));
    expect(open).toHaveLength(59);
    let unordered = 0;
    for (let i = 0; i < open.length; i++) {
      for (let j = i + 1; j < open.length; j++) {
        if (plainLOS(open[i]!, open[j]!) !== plainLOS(open[j]!, open[i]!)) unordered++;
      }
    }
    expect(unordered).toBe(88);
  });

  it('is blocked by pillars and walls only', () => {
    expect(board.hasLOS(T('B3'), T('B5'))).toBe(false); // pillar B4
    expect(board.hasLOS(T('A3'), T('A5'))).toBe(false); // wall A4
    expect(board.hasLOS(T('C1'), T('F1'))).toBe(true); // shutter D1, E1 does not block sight
    expect(board.hasLOS(T('H4'), T('H6'))).toBe(true); // wall gun H5 does not block sight
    expect(board.hasLOS(T('C5'), T('C7'))).toBe(false); // pillar C6
  });

  it('adjacent tiles always see each other, including diagonally past a pillar', () => {
    for (const a of tiles) for (const b of board.neighbors8(a)) expect(board.hasLOS(a, b)).toBe(true);
    expect(board.hasLOS(T('B5'), T('C6'))).toBe(true);
  });

  it('handles corner cases that graze a pillar', () => {
    // C3 to B8 passes C4, C5, B6, B7: clear of B4 and C6.
    expect(lineTiles(T('C3'), T('B8')).map(name)).toEqual(['C3', 'C4', 'C5', 'B6', 'B7', 'B8']);
    expect(board.hasLOS(T('C3'), T('B8'))).toBe(true);
    // D3 to B8 passes C6.
    expect(lineTiles(T('D3'), T('B8')).map(name)).toContain('C6');
    expect(board.hasLOS(T('D3'), T('B8'))).toBe(false);
    // A shallow diagonal past F6.
    expect(board.hasLOS(T('E5'), T('G7'))).toBe(lineTiles(T('E5'), T('G7')).slice(1, -1).every((p) => !board.blocksSight(p)));
  });

  it('a target standing on a pillar tile is not hidden by that pillar', () => {
    // Endpoints never block their own line.
    expect(board.hasLOS(T('B3'), T('B4'))).toBe(true);
  });
});
