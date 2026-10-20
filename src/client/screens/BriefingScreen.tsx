import { useEffect } from 'react';
import { operatesFocus } from '../actionKeys';
import type { Content } from '../../content/types';
import { boardFor } from '../../state/grid';
import type { Loadout } from '../../state/types';
import { MG, TS } from '../art/geometry';
import { Icon } from '../art/icons';
import { Room } from '../art/terrain';
import { PanelTitle } from '../components/Panels';
import { useTypewriter } from '../useTypewriter';

/**
 * Between the loadout and the fight: the room, the first intercom line, the goal and the kit. It says what the rules
 * sheet says and nothing the game hides (when waves come, what the defences cover). The fight, and its turn clock,
 * starts only when the player goes in.
 */
export function BriefingScreen({ c, loadout, onDeploy, onBack }: { c: Content; loadout: Loadout; onDeploy: () => void; onBack: () => void }) {
  const opening = c.intercom.lines.find((l) => l.trigger.type === 'roundStart' && l.trigger.round === 1)?.text ?? '';
  const { shown, done } = useTypewriter([opening ? `“${opening}”` : ''], 45, 300);
  const b = boardFor(c.map);
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onBack();
      // Enter goes in, unless a button has focus: then Enter presses that button (src/client/actionKeys.ts).
      else if (e.key === 'Enter' && !operatesFocus(e.target as HTMLElement | null)) onDeploy();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onBack, onDeploy]);
  const name = (id: string) => c.abilities[id]?.name ?? id;
  return (
    <div className="screen">
      <div className="screen-head">
        <div>
          <div className="kicker">Briefing · intercepted transmission</div>
          <h2>{c.map.name}</h2>
        </div>
        <div className="faint mono">
          {b.width}×{b.height} grid · {c.waves.length} waves
        </div>
      </div>
      <div className="briefing">
        <div className="panel room-map">
          <svg viewBox={`0 0 ${MG * 2 + b.width * TS} ${MG * 2 + b.height * TS}`} role="img" aria-label={`Map of ${c.map.name}`}>
            <Room map={c.map} />
          </svg>
        </div>
        <div className="panel">
          <PanelTitle led="amber blink">Lab intercom</PanelTitle>
          <div className="dossier">
            <div className="transmission crt" aria-label={opening}>
              <span aria-hidden="true">{shown[0]}</span>
              {!done && <span className="cursor" aria-hidden="true" style={{ display: 'inline-block', width: '0.55em', height: '1em', background: 'currentColor', verticalAlign: 'text-bottom', marginLeft: 2 }} />}
            </div>
            <div>
              <h4>Objective</h4>
              <ul>
                <li>
                  You are alone, with {c.units['player']!.hp} HP and no way to heal. Security arrives in {c.waves.length} waves: the first is already here, and you are not told when the others come.
                </li>
                <li>Win when the last wave has arrived and every enemy is dead.</li>
                <li>Lose at 0 HP, or if round {c.rules.roundCap} ends with the fight undecided.</li>
              </ul>
            </div>
            <div>
              <h4>Your kit</h4>
              <div className="kit-strip">
                {loadout.abilities.map((id) => (
                  <span key={id} className={`chip ${c.abilities[id]?.kit ?? ''}`}>
                    <Icon name={id} size={16} /> {name(id)}
                  </span>
                ))}
                {loadout.upgrades.map((id) => (
                  <span key={id} className={`chip ${c.upgrades[id]?.kit ?? ''}`}>
                    <Icon name={`kit-${c.upgrades[id]?.kit ?? 'tech'}`} size={16} /> {c.upgrades[id]?.name ?? id}
                  </span>
                ))}
              </div>
            </div>
            <div>
              <h4>Know the room</h4>
              <ul>
                <li>The lab's own defences fire on whoever is in reach. They always give a cue first; nothing shows which tiles are at risk.</li>
                <li>Every enemy and minion shows what it will do, and every action of yours is previewed before you commit.</li>
              </ul>
            </div>
            <div className="title-actions">
              <button className="btn primary big" autoFocus onClick={onDeploy}>
                <Icon name="play" size={16} /> Enter the armory <kbd>Enter</kbd>
              </button>
              <button className="btn" onClick={onBack}>
                Back to loadout <kbd>Esc</kbd>
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
