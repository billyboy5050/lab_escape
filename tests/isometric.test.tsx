import { renderToStaticMarkup } from 'react-dom/server';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ISO_WALL, MG, TS, cx, cy, projection } from '../src/client/art/geometry';
import { blockBox, muzzle, overlaps } from '../src/client/art/iso';
import { initialBoardView, recordedView } from '../src/client/boardView';
import { Board, type BoardProps } from '../src/client/components/Board';
import { SLIDE_MS, slideAt } from '../src/client/useSlides';
import { computeIntents, previewCommand } from '../src/preview/preview';
import type { GameState } from '../src/state/types';
import { C, scenario } from './helpers';

const ISO = projection('iso', C.map.width, C.map.height);
const TOP = projection('top', C.map.width, C.map.height);

function props(s: GameState, over: Partial<BoardProps> = {}): BoardProps {
  return { c: C, s, frameEvents: [], frameKey: 0, highlights: [], hover: null, movePath: null, onHover: () => {}, onClick: () => {}, preview: null, intents: null, showIntents: true, debug: null, view: 'iso', ...over };
}
const render = (s: GameState, over: Partial<BoardProps> = {}) => renderToStaticMarkup(<Board {...props(s, over)} />);

describe('board projections', () => {
  it('leave the top-down board as it was: floor space is the screen', () => {
    expect(TOP.floor).toBeUndefined();
    expect(TOP.upright).toBeUndefined();
    expect(TOP.pt(123, 45)).toEqual({ x: 123, y: 45 });
    expect(TOP.at(3, 5)).toEqual({ x: cx(3), y: cy(5) });
    expect(TOP.near(3, 5, -9, 4)).toEqual({ x: cx(3) - 9, y: cy(5) + 4 });
    expect([TOP.width, TOP.height]).toEqual([MG * 2 + 8 * TS, MG * 2 + 8 * TS]);
  });

  it('put isometric tile centres where the concept art does: 64 across and 32 down per tile', () => {
    const o = ISO.at(0, 0);
    for (const [x, y] of [
      [1, 0],
      [0, 1],
      [7, 7],
      [3, 5],
    ] as const) {
      expect(ISO.at(x, y)).toEqual({ x: o.x + 64 * (x - y), y: o.y + 32 * (x + y) });
    }
  });

  it('map floor space through the same matrix that draws the floor', () => {
    const m = /^matrix\(([-\d.]+) ([-\d.]+) ([-\d.]+) ([-\d.]+) ([-\d.]+) ([-\d.]+)\)$/.exec(ISO.floor!)!.slice(1).map(Number) as number[];
    expect(m.slice(0, 4)).toEqual([1, 0.5, -1, 0.5]);
    for (const [fx, fy] of [
      [MG, MG],
      [MG + 512, MG],
      [100, 400],
      [333, 77],
    ] as const) {
      expect(ISO.pt(fx, fy)).toEqual({ x: m[0]! * fx + m[2]! * fy + m[4]!, y: m[1]! * fx + m[3]! * fy + m[5]! });
    }
  });

  it('fit the floor, its walls and the front edge inside the board', () => {
    const corners = [ISO.pt(MG, MG), ISO.pt(MG + 8 * TS, MG), ISO.pt(MG, MG + 8 * TS), ISO.pt(MG + 8 * TS, MG + 8 * TS)];
    for (const p of corners) {
      expect(p.x).toBeGreaterThan(0);
      expect(p.x).toBeLessThan(ISO.width);
      expect(p.y).toBeLessThan(ISO.height - 30);
    }
    expect(corners[0]!.y - ISO_WALL).toBeGreaterThan(0);
  });

  it('draw upright art larger, standing on the tile centre', () => {
    const c = ISO.at(2, 2);
    // A unit's feet (y = 19 in its own drawing) land on the tile's centre, and the figure is scaled up from there.
    expect(ISO.near(2, 2, 0, 19)).toEqual(c);
    expect(ISO.near(2, 2, 10, 0).x - c.x).toBeCloseTo(10 * ISO.k);
    expect(ISO.k).toBeGreaterThan(1);
  });

  it('put the wall gun cue at its muzzle, raised on the isometric plinth', () => {
    const gun = C.map.hazards.find((h) => h.def === 'wall_gun')!;
    const t = { x: 7, y: 4 };
    expect(gun.direction).toBe('W');
    expect(muzzle(TOP, t, { x: -1, y: 0 })).toEqual({ x: cx(7) - 26, y: cy(4) });
    expect(muzzle(ISO, t, { x: -1, y: 0 }).y).toBeLessThan(ISO.pt(cx(7) - 26, cy(4)).y);
  });
});

describe('the isometric board', () => {
  const s = scenario({
    player: 'B5',
    units: [
      { def: 'guard', at: 'D7' },
      { def: 'medic', at: 'F3' },
    ],
    objects: [
      { kind: 'mine', at: 'C8' },
      { kind: 'egg', at: 'H8', timer: 2, hp: 3, hatchInto: 'hatchling' },
    ],
  });

  it('draws every layer, skewing only the floor', () => {
    const intents = computeIntents(C, s);
    const preview = previewCommand(C, s, { type: 'ability', ability: 'sidearm', target: 'D7' }, { intents: true });
    const html = render(s, { intents, preview, hover: { x: 3, y: 6 }, highlights: [{ tiles: [{ x: 3, y: 6 }], kind: 'target' }], debug: { ids: true, spawns: [{ tile: { x: 3, y: 0 }, wave: 3 }], flamer: null, hazards: true } });
    expect(html).toContain('matrix(1 0.5 -1 0.5');
    expect(html).toContain('isometric view');
    expect(render(s, { view: 'top' })).not.toContain('matrix(1 0.5 -1 0.5');
    // The debug overlay still names tiles and hazards.
    expect(html).toContain('>B5<');
    expect(html).toMatch(/>(ready|PRIMED|cd\d)</);
  });

  it('draws a unit standing behind a pillar before the pillar, and one in front after it', () => {
    const html = render(s);
    const pillar = html.indexOf('data-tile="C6"');
    const behind = html.indexOf('<title>You:');
    const inFront = html.indexOf('<title>Guard');
    expect(pillar).toBeGreaterThan(0);
    expect(behind).toBeGreaterThan(0);
    expect(behind).toBeLessThan(pillar);
    expect(inFront).toBeGreaterThan(pillar);
  });

  it('fades a pillar that hides a unit, and leaves the others solid', () => {
    const html = render(s);
    const blocks = [...html.matchAll(/data-tile="([A-H]\d)" style="opacity:([\d.]+)"/g)].map((m) => [m[1]!, Number(m[2])] as const);
    expect(blocks.filter(([, o]) => o < 1)).toEqual([['C6', 0.3]]);
    // A unit just behind a wall block fades it too; with everyone in the open, nothing fades.
    expect(render(scenario({ player: 'D8', units: [{ def: 'medic', at: 'G2' }] }))).toMatch(/data-tile="H2" style="opacity:0.3"/);
    const open = render(scenario({ player: 'D8' }));
    expect([...open.matchAll(/data-tile="[A-H]\d" style="opacity:([\d.]+)"/g)].every((m) => Number(m[1]) === 1)).toBe(true);
  });

  it('fades a pillar when the hovered tile is behind it', () => {
    const open = scenario({ player: 'D8' });
    const html = render(open, { hover: { x: 1, y: 4 } });
    expect(html).toMatch(/data-tile="C6" style="opacity:0.3"/);
  });

  it('puts bars and chips above every figure', () => {
    const html = render(s);
    const lastFigure = Math.max(html.lastIndexOf('<title>You:'), html.lastIndexOf('<title>Guard'), html.lastIndexOf('<title>Egg'));
    // HP bars draw their number in a text element after the bar; the egg's timer is a chip.
    const firstBar = html.indexOf('translate(0,28)');
    expect(firstBar).toBeGreaterThan(lastFigure);
  });

  it('gives each figure a click box that picks the tile it stands on', () => {
    const html = render(s);
    // One diamond per tile, then a box per unit and standing object (the flat mine has none).
    expect(html.match(/<polygon points="[^"]+" fill="transparent">/g)).toHaveLength(64);
    const boxes = [...html.matchAll(/<rect x="([-\d.]+)" y="([-\d.]+)" width="([\d.]+)" height="([\d.]+)" fill="transparent"><title>([A-H]\d)<\/title>/g)];
    expect(boxes.map((m) => m[5]).sort()).toEqual(['B5', 'D7', 'F3', 'H8']);
    // The box reaches above the tile's diamond, over the figure's head.
    const b5 = boxes.find((m) => m[5] === 'B5')!;
    expect(Number(b5[2])).toBeLessThan(ISO.at(1, 4).y - TS / 2);
  });

  it('decides what a block hides by the screen boxes they cover', () => {
    const pillar = blockBox(ISO, { x: 2, y: 5 }, 'pillar');
    const behind = ISO.at(1, 4);
    const beside = ISO.at(1, 5);
    expect(overlaps(pillar, { x0: behind.x - 20, x1: behind.x + 20, y0: behind.y - 60, y1: behind.y })).toBe(true);
    expect(overlaps(pillar, { x0: beside.x - 20, x1: beside.x + 20, y0: beside.y - 60, y1: beside.y })).toBe(false);
  });
});

describe('unit slides on the isometric board', () => {
  const slide = { from: { x: 1, y: 7 }, to: { x: 1, y: 4 }, t0: 1000 };

  it('start where the unit was and end on its new tile', () => {
    expect(slideAt(slide, 1000)).toEqual({ x: 1, y: 7 });
    expect(slideAt(slide, 1000 + SLIDE_MS)).toEqual({ x: 1, y: 4 });
    expect(slideAt(slide, 1000 + SLIDE_MS * 3)).toEqual({ x: 1, y: 4 });
  });

  it('move steadily, easing in and out', () => {
    const ys = Array.from({ length: 11 }, (_, i) => slideAt(slide, 1000 + (SLIDE_MS * i) / 10).y);
    for (let i = 1; i < ys.length; i++) expect(ys[i]!).toBeLessThanOrEqual(ys[i - 1]!);
    expect(ys[5]).toBeCloseTo(5.5);
    expect(ys[0]! - ys[1]!).toBeLessThan(ys[4]! - ys[5]!);
  });
});

describe('the board view preference', () => {
  function storage(data: Record<string, string>) {
    const store = new Map(Object.entries(data));
    vi.stubGlobal('localStorage', { getItem: (k: string) => store.get(k) ?? null, setItem: (k: string, v: string) => void store.set(k, v), removeItem: (k: string) => void store.delete(k) });
  }
  afterEach(() => vi.unstubAllGlobals());

  it('is top-down unless something says otherwise', () => {
    storage({});
    expect(initialBoardView('')).toBe('top');
  });

  it('follows the view saved last time, and a ?view= in the URL over that', () => {
    storage({ 'lab-escape.view': '"iso"' });
    expect(initialBoardView('')).toBe('iso');
    expect(initialBoardView('?view=top')).toBe('top');
    storage({});
    expect(initialBoardView('?tester=P3&view=iso')).toBe('iso');
  });

  it.each([['"sideways"'], ['3'], ['{not json'], ['null']])('ignores a saved value of %s, and a URL view it does not know', (raw) => {
    storage({ 'lab-escape.view': raw });
    expect(initialBoardView('?view=sideways')).toBe('top');
  });

  it('records the view a fight was played in, or both once it was switched', () => {
    expect(recordedView(undefined, 'iso')).toBe('iso');
    expect(recordedView('top', 'top')).toBe('top');
    expect(recordedView('top', 'iso')).toBe('both');
    expect(recordedView('both', 'top')).toBe('both');
  });
});
