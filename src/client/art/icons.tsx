import type { ReactNode } from 'react';

/**
 * Line icons on a 24x24 grid, drawn in currentColor. Abilities and utilities are keyed by their content ID; statuses
 * by their status ID; the rest are interface icons. An unknown key draws a plain square rather than nothing.
 */
const GLYPHS: Record<string, ReactNode> = {
  // Tech kit
  sidearm: (
    <>
      <path d="M3 8h15.5v3.5H12l-1.2 2.2H9l-1.4 5.3H4.2l1.5-5.3H3z" />
      <path d="M18.5 9.2h2.5" />
      <path d="M9.5 11.5v2" />
    </>
  ),
  grapple_hook: (
    <>
      <path d="M4 20l9-9" strokeDasharray="2 2" />
      <path d="M13 11l3-3" />
      <path d="M16 8c-2-2-1-5 2-5s4 3 2 5l-1.5 1.5" />
      <path d="M16 8l-1-3M16 8l3 1" />
    </>
  ),
  proximity_mine: (
    <>
      <circle cx="12" cy="14" r="5.5" />
      <circle cx="12" cy="14" r="1.6" fill="currentColor" />
      <path d="M12 8.5V4.5M6.5 14H3M21 14h-3.5M8 10L5.5 7.5M16 10l2.5-2.5" />
    </>
  ),
  auto_turret: (
    <>
      <rect x="6" y="6" width="10" height="7" rx="1.5" />
      <path d="M16 8.5l5-2" />
      <path d="M11 13v2.5M11 15.5L6 21M11 15.5l5 5.5M11 15.5V21" />
    </>
  ),
  scout_drone: (
    <>
      <path d="M6 6l12 12M18 6L6 18" />
      <circle cx="6" cy="6" r="2.6" />
      <circle cx="18" cy="6" r="2.6" />
      <circle cx="6" cy="18" r="2.6" />
      <circle cx="18" cy="18" r="2.6" />
      <rect x="9.5" y="9.5" width="5" height="5" rx="1" fill="currentColor" />
    </>
  ),
  barrier_shield: (
    <>
      <path d="M12 3l7.5 3v5.5c0 4.5-3.2 8-7.5 9.5-4.3-1.5-7.5-5-7.5-9.5V6z" />
      <path d="M12 7.5l3 1.7v3.4L12 14.3l-3-1.7V9.2z" />
    </>
  ),
  // Alien kit
  acid_spit: (
    <>
      <path d="M10 4c3 4.5 5 7 5 9.5a5 5 0 0 1-10 0C5 11 7 8.5 10 4z" />
      <circle cx="18.5" cy="7" r="1.4" fill="currentColor" />
      <circle cx="19" cy="12" r="1" fill="currentColor" />
      <circle cx="16.5" cy="17.5" r="1.2" fill="currentColor" />
    </>
  ),
  lunge: (
    <>
      <path d="M5 19C8 13 12 8 19 4" />
      <path d="M8.5 20.5C11 15.5 14.5 11.5 20 8.5" />
      <path d="M3.5 15C6 10 9.5 6.5 15 3.5" />
    </>
  ),
  spore_pod: (
    <>
      <circle cx="9" cy="10" r="3.5" />
      <circle cx="15.5" cy="9" r="2.5" />
      <circle cx="14" cy="15.5" r="3" />
      <circle cx="7.5" cy="17" r="1.6" />
      <circle cx="19" cy="15" r="1.2" />
    </>
  ),
  brood_egg: (
    <>
      <path d="M12 3c4 0 6.5 6 6.5 10a6.5 6.5 0 0 1-13 0C5.5 9 8 3 12 3z" />
      <path d="M8 12l2 1.5 1.5-2 2 2 2-1.5" />
    </>
  ),
  parasite: (
    <>
      <path d="M4 16c2-4 4 2 6-2s4 2 6-2 2-4 3.5-5" />
      <circle cx="19.5" cy="6.5" r="1.8" fill="currentColor" />
    </>
  ),
  // Utilities
  sprint: (
    <>
      <path d="M5 6l6 6-6 6" />
      <path d="M12 6l6 6-6 6" />
    </>
  ),
  reload: (
    <>
      <path d="M19 12a7 7 0 1 1-2.1-5" />
      <path d="M17.5 3.5v4h-4" />
      <rect x="10.5" y="9" width="3" height="6" rx="1" />
    </>
  ),
  pick_up_mine: (
    <>
      <ellipse cx="12" cy="17" rx="6" ry="2.6" />
      <path d="M12 13V4M8.5 7.5L12 4l3.5 3.5" />
    </>
  ),
  redeploy: (
    <>
      <path d="M7 14l10 0" />
      <circle cx="6" cy="14" r="2" />
      <circle cx="18" cy="14" r="2" />
      <path d="M12 11V3.5M9 6.5l3-3 3 3" />
      <path d="M9 18h6" />
    </>
  ),
  // Statuses
  poison: <path d="M12 3.5c3.5 5 5.5 8 5.5 11a5.5 5.5 0 0 1-11 0c0-3 2-6 5.5-11z" fill="currentColor" stroke="none" />,
  corrode: (
    <>
      <path d="M12 3l7 2.8v5c0 4.2-3 7.5-7 9-4-1.5-7-4.8-7-9v-5z" />
      <path d="M12 6l-1.5 4 3 2-2 4" />
    </>
  ),
  pinned: (
    <>
      <path d="M9 3h6l-1 6 3 3H7l3-3z" fill="currentColor" stroke="none" />
      <path d="M12 12v9" />
    </>
  ),
  slowed: (
    <>
      <path d="M7 3h10M7 21h10" />
      <path d="M8 3c0 5 8 5 8 9s-8 4-8 9M16 3c0 5-8 5-8 9s8 4 8 9" />
    </>
  ),
  // Kits, for upgrades
  'kit-tech': (
    <>
      <circle cx="12" cy="12" r="3" />
      <path d="M12 3v3M12 18v3M3 12h3M18 12h3M5.6 5.6l2.1 2.1M16.3 16.3l2.1 2.1M5.6 18.4l2.1-2.1M16.3 7.7l2.1-2.1" />
    </>
  ),
  'kit-alien': (
    <>
      <path d="M12 3c5 0 7 4 7 7 0 5-4 11-7 11s-7-6-7-11c0-3 2-7 7-7z" />
      <path d="M8 11c1.5 0 3 1 3 2.5M16 11c-1.5 0-3 1-3 2.5" />
    </>
  ),
  'kit-hybrid': (
    <>
      <path d="M7 3c0 6 10 6 10 12s-10 3-10 6M17 3c0 6-10 6-10 12s10 3 10 6" />
      <path d="M9 7h6M9 17h6" />
    </>
  ),
  // Interface
  heart: <path d="M3 12h4l2-4 3 9 2.5-6H21" />,
  ap: <path d="M13 3L5 13.5h6L10 21l8-10.5h-6z" fill="currentColor" stroke="none" />,
  move: (
    <>
      <path d="M8 4c2 0 3 2 3 4s-1 3-1 5H6c0-2-1-3-1-5s1-4 3-4z" />
      <path d="M15.5 9c2 0 3 2 3 4s-1 3-1 5h-4c0-2-1-3-1-5s1-4 3-4z" />
      <path d="M6 16h4M13.5 21h4" />
    </>
  ),
  round: (
    <>
      <circle cx="12" cy="13" r="8" />
      <path d="M12 9v4l3 2M10 2.5h4" />
    </>
  ),
  intercom: (
    <>
      <rect x="4" y="4" width="16" height="16" rx="3" />
      <path d="M8 9h8M8 12h8M8 15h8" />
    </>
  ),
  alert: (
    <>
      <path d="M12 3.5L21.5 20h-19z" />
      <path d="M12 10v4.5M12 17.2v.3" />
    </>
  ),
  'sound-on': (
    <>
      <path d="M4 9h3.5L12 5v14l-4.5-4H4z" />
      <path d="M15.5 9a4 4 0 0 1 0 6M18 6.5a7.5 7.5 0 0 1 0 11" />
    </>
  ),
  'sound-off': (
    <>
      <path d="M4 9h3.5L12 5v14l-4.5-4H4z" />
      <path d="M16 9.5l5 5M21 9.5l-5 5" />
    </>
  ),
  target: (
    <>
      <circle cx="12" cy="12" r="7" />
      <path d="M12 2.5v5M12 16.5v5M2.5 12h5M16.5 12h5" />
    </>
  ),
  skull: (
    <>
      <path d="M12 3a7.5 7.5 0 0 0-5 13v3h10v-3a7.5 7.5 0 0 0-5-13z" />
      <circle cx="9.3" cy="11.5" r="1.7" fill="currentColor" />
      <circle cx="14.7" cy="11.5" r="1.7" fill="currentColor" />
      <path d="M10.5 19v-2.5M13.5 19v-2.5" />
    </>
  ),
  minion: (
    <>
      <circle cx="12" cy="9" r="4" />
      <path d="M5 20c1-4 4-6 7-6s6 2 7 6" />
    </>
  ),
  scan: (
    <>
      <path d="M4 8V5a1 1 0 0 1 1-1h3M16 4h3a1 1 0 0 1 1 1v3M20 16v3a1 1 0 0 1-1 1h-3M8 20H5a1 1 0 0 1-1-1v-3" />
      <path d="M4 12h16" />
    </>
  ),
  log: (
    <>
      <path d="M5 4h14v16H5z" />
      <path d="M8 8h3M8 12h8M8 16h6" />
    </>
  ),
  play: <path d="M7 4.5l12 7.5-12 7.5z" fill="currentColor" stroke="none" />,
  retry: (
    <>
      <path d="M5 12a7 7 0 1 0 2.1-5" />
      <path d="M6.5 3.5v4h4" />
    </>
  ),
};

export function hasIcon(name: string): boolean {
  return name in GLYPHS;
}

/** The icon's strokes, for drawing inside another SVG: a 24-unit box at the origin. */
export function IconGlyph({ name, strokeWidth = 1.9 }: { name: string; strokeWidth?: number }) {
  return (
    <g fill="none" stroke="currentColor" strokeWidth={strokeWidth} strokeLinecap="round" strokeLinejoin="round">
      {GLYPHS[name] ?? <rect x="5" y="5" width="14" height="14" rx="2" />}
    </g>
  );
}

export function Icon({ name, size = 20, className, title }: { name: string; size?: number; className?: string; title?: string }) {
  return (
    <svg viewBox="0 0 24 24" width={size} height={size} className={`icon ${className ?? ''}`} aria-hidden={title ? undefined : true} role={title ? 'img' : undefined}>
      {title && <title>{title}</title>}
      <IconGlyph name={name} />
    </svg>
  );
}
