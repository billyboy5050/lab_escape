import { useEffect, useRef, useState } from 'react';
import type { Pos } from '../state/types';
import type { XY } from './art/geometry';

/** How long a unit takes to slide to its new tile: the same as the top-down board's CSS transition. */
export const SLIDE_MS = 280;

export interface Slide {
  from: XY;
  to: Pos;
  t0: number;
}

/** CSS's ease-in-out, closely enough for a slide this short. */
const ease = (t: number) => t * t * (3 - 2 * t);

/** Where a slide is at time `now`, in tiles. */
export function slideAt(s: Slide, now: number, ms = SLIDE_MS): XY {
  const t = ms > 0 ? (now - s.t0) / ms : 1;
  if (t >= 1) return s.to;
  if (t <= 0) return s.from;
  const e = ease(t);
  return { x: s.from.x + (s.to.x - s.from.x) * e, y: s.from.y + (s.to.y - s.from.y) * e };
}

/**
 * Where each unit is drawn, in tiles: fractional while it slides to a new tile. The isometric board draws units in depth
 * order, so units that pass each other swap places in the document, and moving an element cancels its CSS transition.
 * There the slide is driven from here instead, a frame at a time, which also keeps the depth order right mid-slide.
 * With `on` false every unit is simply where it stands (the top-down board slides with CSS).
 */
export function useSlides(units: readonly { id: number; pos: Pos }[], on: boolean): Map<number, XY> {
  const slides = useRef(new Map<number, Slide>());
  const [, setFrame] = useState(0);
  const now = performance.now();
  const out = new Map<number, XY>();
  const present = new Set<number>();
  let moving = false;
  for (const u of units) {
    present.add(u.id);
    const s = slides.current.get(u.id);
    if (!s) {
      // A unit that has just appeared (spawned, hatched, redeployed) is drawn where it is; it fades in, it does not slide.
      slides.current.set(u.id, { from: u.pos, to: u.pos, t0: -Infinity });
      out.set(u.id, u.pos);
      continue;
    }
    if (s.to.x !== u.pos.x || s.to.y !== u.pos.y) {
      s.from = on ? slideAt(s, now) : u.pos;
      s.to = u.pos;
      s.t0 = now;
    }
    if (on && now - s.t0 < SLIDE_MS) moving = true;
    out.set(u.id, on ? slideAt(s, now) : u.pos);
  }
  for (const id of [...slides.current.keys()]) if (!present.has(id)) slides.current.delete(id);
  useEffect(() => {
    if (!moving) return;
    const r = requestAnimationFrame(() => setFrame((n) => n + 1));
    return () => cancelAnimationFrame(r);
  });
  return out;
}
