import type { CSSProperties, ReactNode } from 'react';

/**
 * Unit illustrations. Each is drawn front-on in a local box centred on the tile (about -28 to +26 vertically) and
 * stands on a base whose shape says whose side it is on: round for yours, a diamond for the lab's soldiers, a hexagon
 * for the Warden. The base colour matches the unit's colour everywhere else in the UI, and the silhouettes differ, so
 * nothing depends on colour alone (spec, Accessibility).
 */

const OUT = 'var(--art-outline)';

export const UNIT_COLOR: Record<string, string> = {
  player: 'var(--player)',
  turret: 'var(--minion)',
  drone: 'var(--minion)',
  hatchling: 'var(--hatch)',
  spitter: 'var(--hatch)',
  burster: 'var(--hatch)',
  guard: 'var(--enemy)',
  medic: 'var(--medic)',
  flamer: 'var(--flamer)',
  warden: 'var(--elite)',
};

export function unitColor(def: string, team: string): string {
  return UNIT_COLOR[def] ?? (team === 'enemy' ? 'var(--enemy)' : 'var(--minion)');
}

export type BaseShape = 'round' | 'diamond' | 'hex';

export function baseShapeFor(def: string, team: string): BaseShape {
  if (def === 'warden') return 'hex';
  return team === 'enemy' ? 'diamond' : 'round';
}

export function UnitBase({ shape, color, small }: { shape: BaseShape; color: string; small?: boolean }) {
  const w = small ? 15 : 22;
  if (shape === 'round')
    return (
      <g>
        <ellipse cx={0} cy={19} rx={w} ry={w * 0.34} fill="#000" opacity={0.45} />
        <ellipse cx={0} cy={19} rx={w} ry={w * 0.34} fill="none" stroke={color} strokeWidth={2.5} />
        <ellipse cx={0} cy={19} rx={w - 5} ry={(w - 5) * 0.34} fill={color} opacity={0.22} />
      </g>
    );
  if (shape === 'diamond')
    return (
      <g>
        <polygon points={`${-w - 2},19 0,${12} ${w + 2},19 0,26`} fill="#000" opacity={0.45} />
        <polygon points={`${-w - 2},19 0,${12} ${w + 2},19 0,26`} fill="none" stroke={color} strokeWidth={2.5} strokeLinejoin="round" />
        <polygon points={`${-w + 6},19 0,${15} ${w - 6},19 0,23`} fill={color} opacity={0.25} />
      </g>
    );
  const pts = (r: number, ry: number) =>
    Array.from({ length: 6 }, (_, i) => `${(Math.cos((Math.PI / 3) * i) * r).toFixed(1)},${(19 + Math.sin((Math.PI / 3) * i) * ry).toFixed(1)}`).join(' ');
  return (
    <g>
      <polygon points={pts(w + 6, 9)} fill="#000" opacity={0.5} />
      <polygon points={pts(w + 6, 9)} fill="none" stroke={color} strokeWidth={3} strokeLinejoin="round" />
      <polygon points={pts(w - 2, 6)} fill={color} opacity={0.28} />
    </g>
  );
}

/** The figure for a unit definition, without its base. */
export function UnitFigure({ def }: { def: string }): ReactNode {
  switch (def) {
    case 'player':
      return <Intruder />;
    case 'guard':
      return <Guard />;
    case 'medic':
      return <Medic />;
    case 'flamer':
      return <Flamer />;
    case 'warden':
      return <Warden />;
    case 'turret':
      return <Turret />;
    case 'drone':
      return <Drone />;
    case 'hatchling':
      return <Hatchling />;
    case 'spitter':
      return <Spitter />;
    case 'burster':
      return <Burster />;
    default:
      return <Unknown />;
  }
}

/** A complete unit picture (base and figure) for use outside the board: rosters, the title screen, the briefing. */
export function UnitPortrait({ def, team, size = 40, style, className }: { def: string; team: string; size?: number; style?: CSSProperties; className?: string }) {
  const color = unitColor(def, team);
  return (
    <svg viewBox="-32 -34 64 64" width={size} height={size} style={style} className={className} aria-hidden="true">
      <UnitBase shape={baseShapeFor(def, team)} color={color} small={def === 'hatchling' || def === 'spitter' || def === 'burster'} />
      <g transform={def === 'drone' ? 'translate(0,-6)' : undefined}>
        <UnitFigure def={def} />
      </g>
    </svg>
  );
}

// ------------------------------------------------------------------------------------------ your side

function Intruder() {
  return (
    <g>
      {/* Legs and bare feet */}
      <path d="M-6.5 6 L-7.5 17 L-2.5 17 L-1.5 7 Z" fill="#33414c" stroke={OUT} strokeWidth={1.2} />
      <path d="M6.5 6 L7.5 17 L2.5 17 L1.5 7 Z" fill="#33414c" stroke={OUT} strokeWidth={1.2} />
      <ellipse cx={-5.5} cy={17.8} rx={3.8} ry={1.7} fill="#d7b08c" stroke={OUT} strokeWidth={1} />
      <ellipse cx={5.5} cy={17.8} rx={3.8} ry={1.7} fill="#d7b08c" stroke={OUT} strokeWidth={1} />
      {/* Hanging arm */}
      <path d="M-9 -9 Q-14 -2 -12 5" fill="none" stroke={OUT} strokeWidth={5.4} strokeLinecap="round" />
      <path d="M-9 -9 Q-14 -2 -12 5" fill="none" stroke="#d7b08c" strokeWidth={3.4} strokeLinecap="round" />
      <path d="M-12.5 0 l2.6 0.8" stroke="#f1f1f1" strokeWidth={2.4} />
      {/* Patient gown */}
      <path d="M-9.5 -11 Q0 -15 9.5 -11 L11 8 Q0 11.5 -11 8 Z" fill="var(--player)" stroke={OUT} strokeWidth={1.6} strokeLinejoin="round" />
      <path d="M-9.5 -11 Q0 -15 9.5 -11 L10 -4 Q0 -7 -10 -4 Z" fill="#fff" opacity={0.18} />
      {[-5, 0, 5].map((dx) => (
        <circle key={dx} cx={dx} cy={2} r={0.9} fill="#fff" opacity={0.5} />
      ))}
      <path d="M0 -12 V8" stroke={OUT} strokeWidth={0.8} opacity={0.5} />
      {/* ID tag */}
      <rect x={-7.5} y={-7} width={5} height={3.5} rx={0.6} fill="#f4f4f0" stroke={OUT} strokeWidth={0.6} />
      {/* Arm raised with the sidearm */}
      <path d="M8.5 -9 Q14 -8 17 -4" fill="none" stroke={OUT} strokeWidth={5.4} strokeLinecap="round" />
      <path d="M8.5 -9 Q14 -8 17 -4" fill="none" stroke="#d7b08c" strokeWidth={3.4} strokeLinecap="round" />
      <path d="M15 -7.5 h9 v3.2 h-5 l-1 3 h-3 z" fill="#20272c" stroke={OUT} strokeWidth={1} />
      <rect x={21.5} y={-7} width={2.5} height={1.5} fill="var(--player)" opacity={0.9} />
      {/* Head: shaved, a bandage, eyes wide */}
      <circle cx={0} cy={-19} r={7.3} fill="#d7b08c" stroke={OUT} strokeWidth={1.6} />
      <path d="M-7 -21 Q-6 -28 0 -27 Q6.5 -28 7.2 -21 Q4 -24 0 -23.5 Q-4 -24 -7 -21 Z" fill="#3a2e28" />
      <path d="M-7.2 -22 Q0 -25.5 7.2 -22 L7 -19.6 Q0 -23 -7 -19.6 Z" fill="#f0eee8" stroke={OUT} strokeWidth={0.7} />
      <circle cx={-2.6} cy={-17.5} r={1.1} fill="#141a1e" />
      <circle cx={2.6} cy={-17.5} r={1.1} fill="#141a1e" />
      <path d="M-1.5 -14 h3" stroke="#7a4a3a" strokeWidth={0.9} strokeLinecap="round" />
    </g>
  );
}

function Turret() {
  return (
    <g>
      <path d="M0 4 L-13 18 M0 4 L13 18 M0 4 L0 19" stroke={OUT} strokeWidth={4.2} strokeLinecap="round" />
      <path d="M0 4 L-13 18 M0 4 L13 18 M0 4 L0 19" stroke="#6b7880" strokeWidth={2.2} strokeLinecap="round" />
      <rect x={-4} y={-1} width={8} height={7} fill="#2a3237" stroke={OUT} strokeWidth={1.2} />
      {/* Body and barrel */}
      <rect x={-12} y={-16} width={22} height={16} rx={4} fill="url(#lx-steel)" stroke={OUT} strokeWidth={1.6} />
      <rect x={-12} y={-6} width={22} height={3} fill="var(--minion)" opacity={0.85} />
      <path d="M8 -12 L24 -17 L25 -13 L9 -8 Z" fill="#20272c" stroke={OUT} strokeWidth={1.3} />
      <rect x={22} y={-18.5} width={4} height={7} rx={1} fill="#20272c" stroke={OUT} strokeWidth={1.1} />
      <circle cx={-4} cy={-11} r={4.2} fill="#0d1215" stroke={OUT} strokeWidth={1} />
      <circle cx={-4} cy={-11} r={2.4} fill="var(--minion)" className="art-blink" />
      <path d="M-12 -12 q-5 2 -4 8" fill="none" stroke="#b8902e" strokeWidth={2.4} strokeDasharray="1.8 1.2" />
    </g>
  );
}

function Drone() {
  const rotor = (x: number, y: number) => (
    <g key={`${x}${y}`}>
      <ellipse cx={x} cy={y} rx={8} ry={2.6} fill="#cfe9ff" opacity={0.28} className="art-rotor" />
      <circle cx={x} cy={y} r={1.8} fill="#20272c" />
    </g>
  );
  return (
    <g>
      <path d="M-14 -8 L14 4 M14 -8 L-14 4" stroke={OUT} strokeWidth={4} strokeLinecap="round" />
      <path d="M-14 -8 L14 4 M14 -8 L-14 4" stroke="#5c6970" strokeWidth={2} strokeLinecap="round" />
      {rotor(-14, -9)}
      {rotor(14, -9)}
      {rotor(-14, 3)}
      {rotor(14, 3)}
      <path d="M-8 -6 L8 -6 L10 0 L6 5 L-6 5 L-10 0 Z" fill="url(#lx-steel)" stroke={OUT} strokeWidth={1.6} strokeLinejoin="round" />
      <rect x={-6} y={-2} width={12} height={2.4} fill="var(--minion)" opacity={0.9} />
      <circle cx={0} cy={2.6} r={2.4} fill="#0d1215" stroke={OUT} strokeWidth={0.8} />
      <circle cx={0} cy={2.6} r={1.3} fill="#ff5a4a" className="art-blink" />
    </g>
  );
}

function HatchLegs({ spread = 1 }: { spread?: number }) {
  return (
    <g stroke={OUT} strokeWidth={1.8} fill="none" strokeLinecap="round">
      {[-1, 1].map((s) =>
        [0, 1, 2].map((i) => <path key={`${s}${i}`} d={`M${s * 5} ${8 + i * 2} q${s * 6 * spread} ${-4 + i * 2} ${s * (10 + i * 2) * spread} ${6 + i * 2}`} />),
      )}
    </g>
  );
}

function Eyes({ y = 2, gap = 3.6, r = 2.4 }: { y?: number; gap?: number; r?: number }) {
  return (
    <g>
      {[-gap, gap].map((x) => (
        <g key={x}>
          <circle cx={x} cy={y} r={r} fill="#f6e05e" stroke={OUT} strokeWidth={0.8} />
          <ellipse cx={x} cy={y} rx={0.7} ry={r * 0.75} fill="#141a1e" />
        </g>
      ))}
    </g>
  );
}

function Hatchling() {
  return (
    <g transform="translate(0,4)">
      <HatchLegs />
      <ellipse cx={0} cy={6} rx={10} ry={7.5} fill="var(--hatch)" stroke={OUT} strokeWidth={1.6} />
      <ellipse cx={-3} cy={3} rx={4} ry={2.2} fill="#fff" opacity={0.2} />
      <circle cx={4} cy={9} r={1.4} fill={OUT} opacity={0.35} />
      <circle cx={-5} cy={9.5} r={1} fill={OUT} opacity={0.35} />
      <Eyes y={3} gap={3.4} r={2.3} />
      <path d="M-3 9.5 q-2 3 -4 3 M3 9.5 q2 3 4 3" stroke={OUT} strokeWidth={1.4} fill="none" strokeLinecap="round" />
    </g>
  );
}

function Spitter() {
  return (
    <g transform="translate(0,2)">
      <HatchLegs spread={1.1} />
      <ellipse cx={0} cy={2} rx={11} ry={10} fill="var(--hatch)" stroke={OUT} strokeWidth={1.6} />
      {/* Acid sac */}
      <ellipse cx={0} cy={9} rx={7.5} ry={6} fill="#c8f25a" stroke={OUT} strokeWidth={1.3} />
      <ellipse cx={-2} cy={7.5} rx={2.6} ry={1.6} fill="#fff" opacity={0.45} />
      <Eyes y={-3} gap={4} r={2.6} />
      <path d="M-2.5 14.5 q2.5 2 5 0" stroke={OUT} strokeWidth={1.4} fill="none" />
      <path d="M1 15.5 q0.6 2.6 0 4 q-0.8 -1.6 0 -4" fill="#c8f25a" stroke={OUT} strokeWidth={0.6} className="art-drip" />
    </g>
  );
}

function Burster() {
  return (
    <g transform="translate(0,2)">
      <HatchLegs spread={0.8} />
      <circle cx={0} cy={3} r={12.5} fill="var(--hatch)" stroke="var(--danger)" strokeWidth={2.5} strokeDasharray="4 2.6" />
      {[
        [-5, -2, 3.2],
        [5, 0, 2.6],
        [-1, 7, 3.6],
        [6, 8, 2],
      ].map(([x, y, r]) => (
        <g key={`${x}${y}`}>
          <circle cx={x} cy={y} r={r} fill="#ff8a3a" stroke={OUT} strokeWidth={0.8} />
          <circle cx={x! - r! * 0.3} cy={y! - r! * 0.3} r={r! * 0.35} fill="#ffe0a8" className="art-throb" />
        </g>
      ))}
      <Eyes y={-6} gap={3.2} r={2} />
    </g>
  );
}

// ------------------------------------------------------------------------------------------ the lab's soldiers

function TrooperLegs({ color }: { color: string }) {
  return (
    <g>
      <path d="M-7 5 L-8 16 L-2.5 16 L-1.5 6 Z" fill={color} stroke={OUT} strokeWidth={1.3} />
      <path d="M7 5 L8 16 L2.5 16 L1.5 6 Z" fill={color} stroke={OUT} strokeWidth={1.3} />
      <path d="M-9.5 15.5 h8 v3 h-8.5 z M1.5 15.5 h8 l0.5 3 h-8.5 z" fill="#15191c" stroke={OUT} strokeWidth={1} />
    </g>
  );
}

function Helmet({ shell, visor }: { shell: string; visor: string }) {
  return (
    <g>
      <path d="M-8.5 -16 Q-9 -28 0 -28 Q9 -28 8.5 -16 L7 -12.5 L-7 -12.5 Z" fill={shell} stroke={OUT} strokeWidth={1.6} strokeLinejoin="round" />
      <path d="M-6 -26 Q0 -28.5 6 -26" stroke="#fff" strokeWidth={1.2} opacity={0.3} fill="none" />
      <rect x={-7.5} y={-21} width={15} height={4.6} rx={2} fill="#0d1215" stroke={OUT} strokeWidth={1} />
      <rect x={-6.5} y={-20} width={13} height={2.4} rx={1.2} fill={visor} className="art-visor" />
    </g>
  );
}

function Guard() {
  return (
    <g>
      <TrooperLegs color="#30363b" />
      <path d="M-10 -11 Q0 -14 10 -11 L9 6 Q0 8.5 -9 6 Z" fill="var(--enemy)" stroke={OUT} strokeWidth={1.6} strokeLinejoin="round" />
      <path d="M-6 -9 h12 v8 q-6 3 -12 0 Z" fill="#1c2125" opacity={0.55} />
      <rect x={-9} y={2.5} width={18} height={3} fill="#1c2125" />
      <ellipse cx={-10.5} cy={-9.5} rx={4.5} ry={3.5} fill="var(--enemy)" stroke={OUT} strokeWidth={1.3} />
      <ellipse cx={10.5} cy={-9.5} rx={4.5} ry={3.5} fill="var(--enemy)" stroke={OUT} strokeWidth={1.3} />
      {/* Rifle held across the body */}
      <path d="M-15 6 L16 -11" stroke={OUT} strokeWidth={5.5} strokeLinecap="round" />
      <path d="M-15 6 L16 -11" stroke="#2b3237" strokeWidth={3.5} strokeLinecap="round" />
      <path d="M2 -3 l3 6 l3 -1.6 l-3 -6 z" fill="#2b3237" stroke={OUT} strokeWidth={0.9} />
      <circle cx={-10} cy={3} r={2.6} fill="#3a4147" stroke={OUT} strokeWidth={1} />
      <circle cx={9} cy={-6.5} r={2.6} fill="#3a4147" stroke={OUT} strokeWidth={1} />
      <Helmet shell="#3a4147" visor="#ffc23d" />
    </g>
  );
}

function Medic() {
  return (
    <g>
      <TrooperLegs color="#8d969c" />
      <path d="M-10 -11 Q0 -14 10 -11 L9 6 Q0 8.5 -9 6 Z" fill="#dfe4e7" stroke={OUT} strokeWidth={1.6} strokeLinejoin="round" />
      <rect x={-9} y={2.5} width={18} height={3} fill="#5b646a" />
      <path d="M-2 -8 h4 v3 h3 v4 h-3 v3 h-4 v-3 h-3 v-4 h3 z" fill="var(--medic)" stroke={OUT} strokeWidth={0.8} />
      <ellipse cx={-10.5} cy={-9.5} rx={4.2} ry={3.3} fill="var(--medic)" stroke={OUT} strokeWidth={1.3} />
      <ellipse cx={10.5} cy={-9.5} rx={4.2} ry={3.3} fill="var(--medic)" stroke={OUT} strokeWidth={1.3} />
      {/* Med kit at the hip */}
      <path d="M10 -6 Q14 0 13 6" fill="none" stroke={OUT} strokeWidth={4.4} strokeLinecap="round" />
      <path d="M10 -6 Q14 0 13 6" fill="none" stroke="#dfe4e7" strokeWidth={2.6} strokeLinecap="round" />
      <rect x={9} y={4} width={11} height={9} rx={1.5} fill="#f4f6f7" stroke={OUT} strokeWidth={1.2} />
      <path d="M13.6 6 h1.8 v1.6 h1.6 v1.8 h-1.6 v1.6 h-1.8 v-1.6 h-1.6 v-1.8 h1.6 z" fill="var(--medic)" />
      <path d="M-10 -6 Q-13 0 -11 5" fill="none" stroke={OUT} strokeWidth={4.4} strokeLinecap="round" />
      <path d="M-10 -6 Q-13 0 -11 5" fill="none" stroke="#dfe4e7" strokeWidth={2.6} strokeLinecap="round" />
      <Helmet shell="#e9edef" visor="#7fe3ff" />
      <path d="M-1 -27.6 h2 v5 h-2 z" fill="var(--medic)" />
    </g>
  );
}

function Flamer() {
  return (
    <g>
      {/* Fuel tanks on the back */}
      <rect x={-15} y={-14} width={7} height={17} rx={3.5} fill="#c8a020" stroke={OUT} strokeWidth={1.3} />
      <rect x={8} y={-14} width={7} height={17} rx={3.5} fill="#c8a020" stroke={OUT} strokeWidth={1.3} />
      <rect x={-15} y={-8} width={7} height={3} fill="#16181a" />
      <rect x={8} y={-8} width={7} height={3} fill="#16181a" />
      <TrooperLegs color="#5a3a1c" />
      <path d="M-11 -11 Q0 -15 11 -11 L10.5 6 Q0 9 -10.5 6 Z" fill="var(--flamer)" stroke={OUT} strokeWidth={1.6} strokeLinejoin="round" />
      <path d="M-7 -8 h14 v9 q-7 3 -14 0 Z" fill="#3a2a16" opacity={0.5} />
      <rect x={-10} y={2.5} width={20} height={3} fill="#2a2016" />
      {/* Nozzle with a pilot flame */}
      <path d="M-4 4 Q8 4 17 -2" stroke={OUT} strokeWidth={5} fill="none" strokeLinecap="round" />
      <path d="M-4 4 Q8 4 17 -2" stroke="#4a5257" strokeWidth={3} fill="none" strokeLinecap="round" />
      <path d="M17 -2 q5 -2 7 -6 q-1 4 -4 6 q4 -1 6 -4 q-2 6 -8 6 z" fill="#ffb23d" stroke="#ff5a1a" strokeWidth={0.8} className="art-flame" />
      {/* Gas mask */}
      <path d="M-8 -16 Q-9 -28 0 -28 Q9 -28 8 -16 L6 -12 L-6 -12 Z" fill="#4a3a26" stroke={OUT} strokeWidth={1.6} />
      <circle cx={-3.6} cy={-19.5} r={3} fill="#ffcf4a" stroke={OUT} strokeWidth={1.2} className="art-visor" />
      <circle cx={3.6} cy={-19.5} r={3} fill="#ffcf4a" stroke={OUT} strokeWidth={1.2} className="art-visor" />
      <rect x={-3} y={-15.5} width={6} height={5} rx={1.5} fill="#2a2f33" stroke={OUT} strokeWidth={1} />
      <path d="M-2 -13 h4 M-2 -11.8 h4" stroke="#6b757b" strokeWidth={0.7} />
    </g>
  );
}

function Warden() {
  return (
    <g transform="scale(1.16) translate(0,-1)">
      {/* Legs like pistons */}
      <path d="M-11 4 L-12 16 L-4 16 L-3 5 Z M11 4 L12 16 L4 16 L3 5 Z" fill="#3a1e18" stroke={OUT} strokeWidth={1.5} />
      <path d="M-14 15 h11 v3.5 h-11.5 z M3 15 h11 l0.5 3.5 h-11.5 z" fill="#1a0f0c" stroke={OUT} strokeWidth={1} />
      {/* Massive torso */}
      <path d="M-17 -12 Q0 -18 17 -12 L14 7 Q0 11 -14 7 Z" fill="var(--elite)" stroke={OUT} strokeWidth={1.8} strokeLinejoin="round" />
      <path d="M-11 -9 h22 l-2 10 q-9 3 -18 0 z" fill="#1a0f0c" opacity={0.45} />
      <path d="M-14 -11 Q0 -16 14 -11" stroke="#ff9a7a" strokeWidth={1.2} fill="none" opacity={0.5} />
      <path d="M-4 -3 h8 l-1 5 h-6 z" fill="#ff3b2f" opacity={0.85} className="art-throb" />
      {/* Ram arms */}
      <rect x={-25} y={-12} width={9} height={18} rx={3} fill="var(--elite)" stroke={OUT} strokeWidth={1.6} />
      <rect x={16} y={-12} width={9} height={18} rx={3} fill="var(--elite)" stroke={OUT} strokeWidth={1.6} />
      <rect x={-26} y={4} width={11} height={7} rx={2} fill="#2a2f33" stroke={OUT} strokeWidth={1.4} />
      <rect x={15} y={4} width={11} height={7} rx={2} fill="#2a2f33" stroke={OUT} strokeWidth={1.4} />
      <path d="M-24 -9 v12 M18 -9 v12" stroke="#ff9a7a" strokeWidth={1} opacity={0.45} />
      {/* Head sunk between the shoulders */}
      <path d="M-8 -14 Q-8 -25 0 -25 Q8 -25 8 -14 Z" fill="#2a2f33" stroke={OUT} strokeWidth={1.6} />
      <rect x={-6.5} y={-20.5} width={13} height={3.4} rx={1.4} fill="#ff2a1f" className="art-visor" />
      <rect x={-20} y={-17} width={9} height={6} rx={2} fill="var(--elite)" stroke={OUT} strokeWidth={1.4} />
      <rect x={11} y={-17} width={9} height={6} rx={2} fill="var(--elite)" stroke={OUT} strokeWidth={1.4} />
    </g>
  );
}

function Unknown() {
  return <circle cx={0} cy={-4} r={14} fill="var(--ink-3)" stroke={OUT} strokeWidth={1.6} />;
}
