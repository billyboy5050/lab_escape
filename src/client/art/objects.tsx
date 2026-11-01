import type { GameObject } from '../../state/types';

const OUT = 'var(--art-outline)';

/** A number on a small dark chip, used for timers, decay and object HP. */
export function Chip({ x, y, text, color = 'var(--ink)', bg = 'var(--chip-bg)', size = 10 }: { x: number; y: number; text: string; color?: string; bg?: string; size?: number }) {
  const w = Math.max(14, text.length * size * 0.62 + 7);
  return (
    <g transform={`translate(${x},${y})`}>
      <rect x={-w / 2} y={-size * 0.78} width={w} height={size * 1.5} rx={3} fill={bg} stroke={color} strokeWidth={1} />
      <text y={size * 0.36} textAnchor="middle" fontSize={size} fontWeight={700} fontFamily="var(--mono)" fill={color}>
        {text}
      </text>
    </g>
  );
}

/** Objects that lie flat on the floor; the others stand up from it. */
export const isFlat = (o: GameObject) => o.kind === 'mine' || o.kind === 'corpse';

/**
 * An object drawn at the tile centre (0, 0). The caller positions it. `parts` draws only the object (`art`) or only its
 * number chips (`chips`): the isometric board puts chips on a layer above everything, so a unit in front never hides them.
 */
export function ObjectArt({ o, parts = 'all' }: { o: GameObject; parts?: 'all' | 'art' | 'chips' }) {
  const art = parts !== 'chips';
  const chips = parts !== 'art';
  switch (o.kind) {
    case 'mine':
      if (!art) return null;
      return (
        <g>
          <ellipse cx={1.5} cy={4} rx={13} ry={5} fill="#000" opacity={0.35} />
          <circle r={12} fill="none" stroke="var(--player)" strokeWidth={1.6} strokeDasharray="3 3" opacity={0.85} />
          <circle r={9.5} fill="url(#lx-steel)" stroke={OUT} strokeWidth={1.5} />
          {[0, 60, 120, 180, 240, 300].map((a) => (
            <rect key={a} x={-1.2} y={-9} width={2.4} height={3.5} fill="#1a2024" transform={`rotate(${a})`} />
          ))}
          <circle r={4.6} fill="#20272c" stroke={OUT} strokeWidth={1} />
          <circle r={2.4} fill="#ff3b2f" className="art-blink" />
          <circle r={7} fill="url(#lx-glow-red)" opacity={0.6} className="art-blink" />
        </g>
      );
    case 'egg':
      return (
        <g>
          {art && (
            <>
              <ellipse cx={1} cy={16} rx={14} ry={4.5} fill="#000" opacity={0.35} />
              <path d="M-12 12 q-4 4 -9 3 M12 12 q4 4 9 3 M-6 15 q-1 4 -4 6 M7 15 q2 3 5 5" stroke="#1d6b4b" strokeWidth={2} fill="none" strokeLinecap="round" />
              <ellipse cx={0} cy={-2} rx={13} ry={17} fill="url(#lx-egg)" stroke={OUT} strokeWidth={1.6} className="art-throb-slow" />
              <path d="M-7 -12 q4 6 1 14 q-2 6 2 12 M6 -14 q-3 8 0 14 q3 6 -1 13" stroke="#0f4a35" strokeWidth={1.2} fill="none" opacity={0.7} />
              <ellipse cx={-5} cy={-10} rx={3.5} ry={5.5} fill="#fff" opacity={0.35} />
            </>
          )}
          {chips && <Chip x={0} y={1} text={`${o.timer ?? ''}`} size={11} color="var(--hatch-ink)" />}
          {chips && o.hp !== undefined && <Chip x={17} y={22} text={`${o.hp}hp`} size={8.5} color="var(--ink-2)" />}
        </g>
      );
    case 'shield':
      return (
        <g>
          {art && (
            <>
              <rect x={-27} y={-27} width={54} height={50} rx={4} fill="url(#lx-shield)" />
              <rect x={-27} y={-27} width={54} height={50} rx={4} fill="url(#lx-hex)" opacity={0.7} />
              <rect x={-27} y={-27} width={54} height={50} rx={4} fill="none" stroke="var(--player)" strokeWidth={2.5} className="art-shield-edge" />
              <rect x={-29} y={21} width={58} height={7} rx={2} fill="url(#lx-steel)" stroke={OUT} strokeWidth={1.4} />
              <rect x={-24} y={23} width={48} height={2.2} fill="var(--player)" opacity={0.9} />
            </>
          )}
          {chips && <Chip x={19} y={-18} text={`${o.hp ?? ''}`} size={10} color="var(--player)" />}
        </g>
      );
    case 'corpse':
      return (
        <g opacity={0.9}>
          {art && (
            <>
              <ellipse cx={0} cy={6} rx={21} ry={9} fill="var(--stain)" opacity={0.55} />
              <g transform="rotate(-18)">
                <rect x={-14} y={-2} width={24} height={11} rx={5} fill="#4b5257" stroke={OUT} strokeWidth={1.3} />
                <circle cx={14} cy={2} r={5.5} fill="#3a4147" stroke={OUT} strokeWidth={1.3} />
                <path d="M-14 3 l-7 -3 M-12 8 l-8 3" stroke="#3a4147" strokeWidth={3.5} strokeLinecap="round" />
              </g>
            </>
          )}
          {chips && <Chip x={17} y={22} text={`${o.decay ?? ''}`} size={9} color="var(--ink-2)" />}
        </g>
      );
  }
}
