import { useEffect, useMemo, useState } from 'react';
import type { Content } from '../../content/types';
import type { Category } from '../../telemetry/summary';
import type { FightSession } from '../../telemetry/session';
import { eventsToJsonl } from '../../telemetry/record';
import { median } from '../../util/stats';
import { Icon } from '../art/icons';
import { PanelTitle } from '../components/Panels';
import { download } from '../telemetry';

const CAT_LABEL: Record<Category, string> = { enemy: 'Enemies', hazard: 'Lab hazards', player: 'Your own abilities', minion: 'Your own minions' };
const CAT_COLOR: Record<Category, string> = { enemy: 'var(--enemy)', hazard: 'var(--hazard)', player: 'var(--player)', minion: 'var(--minion)' };
const CAT_ICON: Record<Category, string> = { enemy: 'target', hazard: 'alert', player: 'sidearm', minion: 'minion' };
const ORDER: Category[] = ['enemy', 'hazard', 'player', 'minion'];

function Bars({ data }: { data: Record<Category, number> }) {
  const max = Math.max(1, ...ORDER.map((k) => data[k]));
  return (
    <div className="bars">
      {ORDER.map((k) => (
        <div className="bar-row" key={k}>
          <span className="who">
            <span style={{ color: CAT_COLOR[k] }}>
              <Icon name={CAT_ICON[k]} size={15} />
            </span>
            {CAT_LABEL[k]}
          </span>
          <div className="bar-track">
            <div className="bar-fill" style={{ width: `${(100 * data[k]) / max}%`, background: CAT_COLOR[k] }} />
          </div>
          <span className="mono" style={{ textAlign: 'right' }}>
            {data[k]}
          </span>
        </div>
      ))}
    </div>
  );
}

/**
 * The telemetry folder a fight was saved to once the save settles: undefined while it is still going, null when it was
 * not saved (no dev server, or no save was started). The save can still be in flight when the player opens the results.
 */
function useSaved(save: Promise<string | null> | null): string | null | undefined {
  const [saved, setSaved] = useState<string | null | undefined>(save ? undefined : null);
  useEffect(() => {
    if (!save) return setSaved(null);
    let current = true;
    setSaved(undefined);
    void save.then((dir) => current && setSaved(dir));
    return () => {
      current = false;
    };
  }, [save]);
  return saved;
}

export function ResultScreen(props: { c: Content; session: FightSession; save: Promise<string | null> | null; onRetry: () => void; onChange: () => void }) {
  const { c, session } = props;
  const saved = useSaved(props.save);
  const s = useMemo(() => session.summary(), [session]);
  const won = s.outcome === 'win';
  const playerTotal = ORDER.reduce((a, k) => a + s.playerDamageTaken[k], 0);
  const ffShare = playerTotal ? (s.friendlyFireToPlayer / playerTotal) * 100 : 0;
  const turnTimes = session.record.meta?.turnTimesMs ?? [];
  const medianTurn = turnTimes.length ? median(turnTimes) / 1000 : null;
  const name = (id: string) => c.abilities[id]?.name ?? c.upgrades[id]?.name ?? c.utilities[id]?.name ?? id;
  const stamp = new Date().toISOString().replace(/[:.]/g, '-');
  return (
    <div className="screen">
      <div className="screen-head">
        <div>
          <div className="kicker">Incident report · {c.map.name}</div>
          <h2>{won ? 'Lockdown broken' : 'Intruder down'}</h2>
        </div>
      </div>
      <div className="panel result-hero crt">
        <div className={`stamp ${won ? 'win' : 'lose'}`}>{won ? 'Victory' : 'Defeat'}</div>
        <div style={{ minWidth: 0, flex: '1 1 340px' }}>
          <div className="verdict">{won ? 'The armory is compromised.' : `Cause of death: ${s.causeOfDeath ? `${s.causeOfDeath.source} (${s.causeOfDeath.via}), ${CAT_LABEL[s.causeOfDeath.category].toLowerCase()}` : s.cause}`}</div>
          <div className="facts">
            <span className="pill">
              {won ? 'Won' : 'Survived'} {s.rounds} round{s.rounds === 1 ? '' : 's'}
            </span>
            <span className="pill">
              Waves seen {s.wavesSpawned}/{c.waves.length}
            </span>
            <span className="pill" style={ffShare > 35 ? { color: 'var(--danger)', borderColor: 'var(--danger)' } : undefined}>
              Friendly-fire share of your damage {ffShare.toFixed(0)}%
            </span>
          </div>
          <div className="muted" style={{ marginTop: 8 }}>
            Loadout: {s.loadout.abilities.map(name).join(', ')}
            {s.loadout.upgrades.length ? ` · ${s.loadout.upgrades.map(name).join(', ')}` : ''}
          </div>
        </div>
        <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
          <button className="btn primary big" onClick={props.onRetry} autoFocus>
            <Icon name="retry" size={16} /> Retry this loadout
          </button>
          <button className="btn" onClick={props.onChange}>
            Change loadout
          </button>
        </div>
      </div>
      <div className="result-grid">
        <div className="panel">
          <PanelTitle icon="heart">Damage you took, by source</PanelTitle>
          <Bars data={s.playerDamageTaken} />
          <div style={{ padding: '0 12px 12px' }} className={ffShare > 35 ? 'warn-line' : 'muted'}>
            Friendly fire: {s.friendlyFireToPlayer} of {playerTotal} ({ffShare.toFixed(0)}%)
          </div>
        </div>
        <div className="panel">
          <PanelTitle icon="target">Damage dealt to enemies, by source</PanelTitle>
          <Bars data={s.enemyDamageTaken} />
        </div>
        <div className="panel">
          <PanelTitle icon="minion">Damage your side took (minions, shields, eggs)</PanelTitle>
          <Bars data={s.sideDamageTaken} />
          <div style={{ padding: '0 12px 12px' }} className="muted">
            Friendly fire to your side: {s.friendlyFireToSide}
          </div>
        </div>
        <div className="panel">
          <PanelTitle icon="round">Fight</PanelTitle>
          <div className="kv">
            <span className="muted">Kills</span>
            <span>{Object.entries(s.kills).map(([k, v]) => `${v} ${c.units[k]?.name ?? k}`).join(', ') || 'none'}</span>
            <span className="muted">Peak minions</span>
            <span>
              {s.peakMinions} of {c.rules.caps.minions} (lost {s.minionsLost})
            </span>
            <span className="muted">Hazard fires</span>
            <span>{s.hazardFires.length ? s.hazardFires.map((h) => `R${h.round} ${h.hazard}→${h.victims.join('/')}`).join('; ') : 'none'}</span>
            <span className="muted">Abilities used</span>
            <span>{Object.entries(s.abilitiesUsed).map(([k, v]) => `${name(k)} ×${v}`).join(', ') || 'none'}</span>
            <span className="muted">Median turn time</span>
            <span>{medianTurn !== null ? `${medianTurn.toFixed(1)} s` : '—'}</span>
            <span className="muted">Warden</span>
            <span>
              {s.wardenReachedPlayerRound !== null ? `reached you in round ${s.wardenReachedPlayerRound}` : 'never reached you'}
              {s.wardenDiedRound !== null ? `, died in round ${s.wardenDiedRound}` : ''}
            </span>
          </div>
        </div>
        <div className="panel">
          <PanelTitle icon="log">Telemetry</PanelTitle>
          <div className="info-body">
            <div className="muted">{saved === undefined ? 'Saving…' : saved ? `Saved to ${saved}/` : 'Not saved automatically (no dev server). Download the files below.'}</div>
            <div style={{ display: 'flex', gap: 8, flexWrap: 'wrap' }}>
              <button className="btn small" onClick={() => download(`replay-${stamp}.json`, JSON.stringify(session.record, null, 2))}>
                Replay (command log)
              </button>
              <button className="btn small" onClick={() => download(`summary-${stamp}.json`, JSON.stringify(s, null, 2))}>
                Summary
              </button>
              <button className="btn small" onClick={() => download(`events-${stamp}.jsonl`, eventsToJsonl(session.events), 'application/x-ndjson')}>
                Event stream
              </button>
            </div>
            <div className="faint">A replay file reproduces this fight exactly (engine {session.record.engineVersion}, content {session.record.contentHash}).</div>
          </div>
        </div>
        <div className="panel">
          <PanelTitle icon="intercom">Intercom</PanelTitle>
          <div className="info-body">
            {session.state.intercom.log.map((l, i) => (
              <div key={i}>
                <span className="faint mono">R{l.round} </span>“{l.text}”
              </div>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}
