import { useEffect } from 'react';
import type { Content } from '../../content/types';
import { operatesFocus } from '../actionKeys';
import { ENGINE_VERSION } from '../../state/state';
import { Icon } from '../art/icons';
import { UnitFigure } from '../art/units';
import { useTypewriter } from '../useTypewriter';

/** The first screen: who you are and why you are running, then on to the loadout. */
export function TitleScreen({ c, onBegin }: { c: Content; onBegin: () => void }) {
  const lines = ['> VITAL SIGNS ........ STABLE', '> MEMORY ............. NOT FOUND', `> LOCATION ........... ${c.map.name.toUpperCase()}`, '> 1 UNREAD MESSAGE', '  "Get out. Fast."'];
  const { shown, done } = useTypewriter(lines, 38, 500);
  useEffect(() => {
    // Enter begins, unless a button has focus: then Enter presses that button (src/client/actionKeys.ts).
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Enter' && !operatesFocus(e.target as HTMLElement | null)) onBegin();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onBegin]);
  return (
    <div className="title-screen">
      <div />
      <div className="title-body">
        <div className="title-logo">
          <div className="kicker">
            <span className="led red blink" /> Containment breach
          </div>
          <h1>
            Lab <span>Escape</span>
          </h1>
          <div className="sub">{c.map.name}</div>
          <div className="terminal crt" aria-label={lines.join('. ')}>
            {shown.map((l, i) =>
              l || (i === 0 && !done) ? (
                <div key={i} className={`ln ${i >= 3 ? 'alert' : ''}`} aria-hidden="true">
                  {l}
                  {!done && shown[i + 1] === '' && l.length > 0 && <span className="cursor" />}
                </div>
              ) : null,
            )}
            {done && <span className="cursor" aria-hidden="true" />}
          </div>
          <div className="title-actions">
            <button className="btn primary big" autoFocus onClick={onBegin}>
              <Icon name="play" size={16} /> Begin <kbd>Enter</kbd>
            </button>
            <span className="muted" style={{ maxWidth: 300 }}>
              You wake in a lab with no memories. Stitch tech and alien powers into combos you didn't plan, and get out.
            </span>
          </div>
        </div>
        <div className="title-art">
          <TitleArt />
        </div>
      </div>
      <div className="title-foot">
        <span>Lab Escape · playable prototype · one room</span>
        <span>
          engine {ENGINE_VERSION} · content {c.hash}
        </span>
      </div>
    </div>
  );
}

/** The intruder in front of a sealed shutter, the lab's security closing in behind, lit red by the alarm. */
function TitleArt() {
  return (
    <svg viewBox="0 0 400 380" role="img" aria-label="The intruder in front of the armory shutter, with guards, a flamer and the Warden behind">
      <defs>
        <filter id="lx-silhouette">
          <feColorMatrix type="matrix" values="0.26 0.04 0 0 0.03  0 0.08 0 0 0.008  0 0 0.1 0 0.012  0 0 0 1 0" />
        </filter>
        <radialGradient id="lx-title-fade" cx="50%" cy="50%" r="50%">
          <stop offset="62%" stopColor="#fff" />
          <stop offset="100%" stopColor="#000" />
        </radialGradient>
        <mask id="lx-title-mask">
          <rect width="400" height="380" fill="url(#lx-title-fade)" />
        </mask>
        <radialGradient id="lx-title-floor" cx="50%" cy="20%" r="70%">
          <stop offset="0%" stopColor="#3a2a28" />
          <stop offset="100%" stopColor="#0a0d0f" />
        </radialGradient>
      </defs>
      <g mask="url(#lx-title-mask)">
      {/* The shutter */}
      <rect x="70" y="20" width="260" height="190" rx="6" fill="#111517" />
      <rect x="82" y="34" width="236" height="160" fill="url(#lx-steel)" opacity="0.55" />
      {Array.from({ length: 9 }, (_, i) => (
        <line key={i} x1="82" x2="318" y1={50 + i * 16} y2={50 + i * 16} stroke="#0b0e10" strokeWidth="3" opacity="0.7" />
      ))}
      <rect x="82" y="176" width="236" height="18" fill="url(#lx-stripes)" />
      <circle cx="200" cy="26" r="40" fill="url(#lx-glow-red)" className="lx-lamp" />
      <circle cx="200" cy="26" r="7" fill="#ff4a3a" />
      {/* Floor */}
      <path d="M0 210 H400 V380 H0 Z" fill="url(#lx-title-floor)" />
      <path d="M0 260 H400 M0 320 H400 M120 210 L40 380 M200 210 V380 M280 210 L360 380" stroke="#ffffff" strokeOpacity="0.05" strokeWidth="2" />
      <ellipse cx="200" cy="215" rx="200" ry="40" fill="url(#lx-glow-red)" opacity="0.35" />
      </g>
      {/* Security, in silhouette */}
      <g filter="url(#lx-silhouette)">
        <g transform="translate(200 172) scale(3.1)">
          <UnitFigure def="warden" />
        </g>
        <g transform="translate(98 200) scale(2.5)">
          <UnitFigure def="guard" />
        </g>
        <g transform="translate(306 200) scale(2.5)">
          <UnitFigure def="flamer" />
        </g>
      </g>
      {[
        [200, 110],
        [98, 150],
        [306, 150],
      ].map(([x, y]) => (
        <circle key={x} cx={x} cy={y} r="5" fill="#ff3b2f" className="art-visor" />
      ))}
      {/* The intruder */}
      <ellipse cx="200" cy="358" rx="120" ry="34" fill="url(#lx-glow-blue)" opacity="0.4" />
      <ellipse cx="200" cy="356" rx="70" ry="12" fill="#000" opacity="0.55" />
      <g transform="translate(200 288) scale(4)">
        <g className="art-idle">
          <UnitFigure def="player" />
        </g>
      </g>
    </svg>
  );
}
