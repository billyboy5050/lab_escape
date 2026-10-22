import { isBoardView, type BoardView } from './art/geometry';
import { loadPref, savePref } from './telemetry';

/**
 * Which way the fight board is drawn. Top-down is the default, and stays so until the owner decides otherwise: fights
 * played in another view are not comparable with earlier playtest rounds (docs/roadmap.md). A `?view=top` or `?view=iso`
 * in the page's URL wins for that page; otherwise the view chosen last time in this browser is used.
 */
export function initialBoardView(search: string = window.location.search): BoardView {
  const fromUrl = new URLSearchParams(search).get('view');
  if (isBoardView(fromUrl)) return fromUrl;
  return loadPref('view', 'top', isBoardView);
}

/** Remembers the chosen view, and keeps a `?view=` in the address bar in step, so a reload shows what was chosen. */
export function saveBoardView(v: BoardView): void {
  savePref('view', v);
  try {
    const url = new URL(window.location.href);
    if (!url.searchParams.has('view')) return;
    url.searchParams.set('view', v);
    window.history.replaceState(window.history.state, '', url);
  } catch {
    // The address bar is a convenience; the saved preference is what counts.
  }
}

/** The view recorded in a fight's telemetry: the one it started in, or `both` once it has been switched mid-fight. */
export function recordedView(current: 'top' | 'iso' | 'both' | undefined, now: BoardView): 'top' | 'iso' | 'both' {
  return current === undefined || current === now ? now : 'both';
}
