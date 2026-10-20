import { TS } from './geometry';

/**
 * Patterns, gradients and markers shared by every drawing in the app (the board, the minion roster, the title and
 * briefing art). Rendered once at the root in a zero-size SVG, so a drawing can reference them by ID whether or not the
 * board is on screen. A `display: none` SVG would not do: browsers then drop its gradients.
 */
export function ArtDefs() {
  return (
    <svg width={0} height={0} style={{ position: 'absolute', overflow: 'hidden' }} aria-hidden="true" focusable="false">
      <defs>
        {/* Floor: riveted steel plates, one per tile, with a diamond tread on alternate plates. */}
        <pattern id="lx-plate" width={TS} height={TS} patternUnits="userSpaceOnUse">
          <rect width={TS} height={TS} fill="var(--floor-1)" />
          <rect x={1} y={1} width={TS - 2} height={TS - 2} fill="none" stroke="var(--floor-hi)" strokeWidth={1.5} />
          <path d={`M${TS - 1} 1 V${TS - 1} H1`} fill="none" stroke="var(--floor-lo)" strokeWidth={2} />
          {[
            [7, 7],
            [TS - 7, 7],
            [7, TS - 7],
            [TS - 7, TS - 7],
          ].map(([x, y]) => (
            <g key={`${x}-${y}`}>
              <circle cx={x} cy={y} r={2.2} fill="var(--floor-lo)" />
              <circle cx={x! - 0.5} cy={y! - 0.5} r={1.4} fill="var(--rivet)" />
            </g>
          ))}
        </pattern>
        <pattern id="lx-tread" width={10} height={10} patternUnits="userSpaceOnUse" patternTransform="rotate(45)">
          <rect x={2} y={4} width={6} height={2} rx={1} fill="var(--floor-hi)" opacity={0.55} />
        </pattern>
        <pattern id="lx-stripes" width={12} height={12} patternUnits="userSpaceOnUse" patternTransform="rotate(45)">
          <rect width={12} height={12} fill="#16181a" />
          <rect width={6} height={12} fill="#f2b01e" />
        </pattern>
        <pattern id="lx-stripes-fine" width={6} height={6} patternUnits="userSpaceOnUse" patternTransform="rotate(45)">
          <rect width={6} height={6} fill="#16181a" />
          <rect width={3} height={6} fill="#f2b01e" />
        </pattern>
        <pattern id="lx-hatch" width={7} height={7} patternUnits="userSpaceOnUse" patternTransform="rotate(45)">
          <rect width={2.5} height={7} fill="var(--danger)" opacity={0.42} />
        </pattern>
        <pattern id="lx-hex" width={10} height={17.32} patternUnits="userSpaceOnUse">
          <path d="M5 0 L10 2.89 L10 8.66 L5 11.55 L0 8.66 L0 2.89 Z M5 11.55 V17.32" fill="none" stroke="var(--player)" strokeWidth={0.8} opacity={0.75} />
        </pattern>
        <pattern id="lx-grille" width={4} height={4} patternUnits="userSpaceOnUse">
          <rect width={4} height={4} fill="#101315" />
          <rect width={4} height={1.6} fill="#3b444a" />
        </pattern>

        <filter id="lx-soft" x="-30%" y="-30%" width="160%" height="160%">
          <feGaussianBlur stdDeviation="5" />
        </filter>

        {/* Lighting */}
        <radialGradient id="lx-vignette" cx="50%" cy="50%" r="72%">
          <stop offset="55%" stopColor="#000" stopOpacity={0} />
          <stop offset="100%" stopColor="#000" stopOpacity={0.55} />
        </radialGradient>
        <radialGradient id="lx-pool" cx="50%" cy="50%" r="50%">
          <stop offset="0%" stopColor="var(--lamp)" stopOpacity={0.16} />
          <stop offset="100%" stopColor="var(--lamp)" stopOpacity={0} />
        </radialGradient>
        {(
          [
            ['amber', '#ffb020'],
            ['red', '#ff3b2f'],
            ['green', '#3df59a'],
            ['blue', '#4fb8ff'],
            ['white', '#ffffff'],
            ['magenta', '#ff6ad5'],
          ] as const
        ).map(([n, c]) => (
          <radialGradient key={n} id={`lx-glow-${n}`} cx="50%" cy="50%" r="50%">
            <stop offset="0%" stopColor={c} stopOpacity={0.95} />
            <stop offset="35%" stopColor={c} stopOpacity={0.5} />
            <stop offset="100%" stopColor={c} stopOpacity={0} />
          </radialGradient>
        ))}
        <radialGradient id="lx-fireball" cx="50%" cy="50%" r="50%">
          <stop offset="0%" stopColor="#fff6c8" />
          <stop offset="30%" stopColor="#ffc23d" />
          <stop offset="65%" stopColor="#ff6a1a" stopOpacity={0.85} />
          <stop offset="100%" stopColor="#8a1e05" stopOpacity={0} />
        </radialGradient>
        <radialGradient id="lx-gas" cx="50%" cy="50%" r="50%">
          <stop offset="0%" stopColor="#b9f07a" stopOpacity={0.75} />
          <stop offset="70%" stopColor="#5fcf6a" stopOpacity={0.35} />
          <stop offset="100%" stopColor="#2f8f4a" stopOpacity={0} />
        </radialGradient>
        <radialGradient id="lx-acid" cx="50%" cy="50%" r="50%">
          <stop offset="0%" stopColor="#eaff7a" />
          <stop offset="60%" stopColor="#8be03a" stopOpacity={0.8} />
          <stop offset="100%" stopColor="#3a8f1f" stopOpacity={0} />
        </radialGradient>
        <radialGradient id="lx-egg" cx="38%" cy="32%" r="70%">
          <stop offset="0%" stopColor="#c9f5d8" />
          <stop offset="35%" stopColor="#4fc890" />
          <stop offset="100%" stopColor="#0f4a35" />
        </radialGradient>
        <linearGradient id="lx-steel" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor="#6d7a82" />
          <stop offset="55%" stopColor="#3c464d" />
          <stop offset="100%" stopColor="#252c31" />
        </linearGradient>
        <linearGradient id="lx-steel-light" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0%" stopColor="#c3ccd1" />
          <stop offset="100%" stopColor="#7b878e" />
        </linearGradient>
        <linearGradient id="lx-pillar-top" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0%" stopColor="var(--pillar-hi)" />
          <stop offset="100%" stopColor="var(--pillar-top)" />
        </linearGradient>
        <linearGradient id="lx-shield" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor="var(--player)" stopOpacity={0.05} />
          <stop offset="100%" stopColor="var(--player)" stopOpacity={0.45} />
        </linearGradient>

        {/* Arrowheads for intents and previews */}
        <marker id="arrow-enemy" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="5" markerHeight="5" orient="auto-start-reverse">
          <path d="M0,0 L10,5 L0,10 z" fill="var(--danger)" />
        </marker>
        <marker id="arrow-minion" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="5" markerHeight="5" orient="auto-start-reverse">
          <path d="M0,0 L10,5 L0,10 z" fill="var(--player)" />
        </marker>
        <marker id="arrow-preview" viewBox="0 0 10 10" refX="8" refY="5" markerWidth="5" markerHeight="5" orient="auto-start-reverse">
          <path d="M0,0 L10,5 L0,10 z" fill="var(--ink)" />
        </marker>
      </defs>
    </svg>
  );
}
