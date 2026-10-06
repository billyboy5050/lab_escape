import { useMemo } from 'react';
import type { Content } from '../../content/types';
import { upgradeAllowed } from '../../content/load';
import { describeRequires, loadoutProblems } from '../../engine/step';
import type { FightSettings, Loadout } from '../../state/types';
import { loadAttempts } from '../telemetry';

export function LoadoutScreen(props: {
  c: Content;
  loadout: Loadout;
  setLoadout: (l: Loadout) => void;
  presetId: string | null;
  setPresetId: (p: string | null) => void;
  settings: FightSettings;
  setSettings: (s: FightSettings) => void;
  tester?: string;
  setTester: (t: string) => void;
  onStart: () => void;
}) {
  const { c, loadout } = props;
  const maxA = c.rules.loadout.abilities;
  const maxU = c.rules.loadout.upgrades;
  const problems = loadoutProblems(c, loadout);
  const attempts = useMemo(() => loadAttempts().slice(0, 8), []);
  const setAbilities = (abilities: string[]) => {
    // Drop upgrades whose requirements are no longer met.
    const upgrades = loadout.upgrades.filter((u) => upgradeAllowed(c.upgrades[u]!.requires, abilities));
    props.setPresetId(null);
    props.setLoadout({ abilities: c.abilityOrder.filter((a) => abilities.includes(a)), upgrades });
  };
  const toggleAbility = (id: string) => {
    const on = loadout.abilities.includes(id);
    if (!on && loadout.abilities.length >= maxA) return;
    setAbilities(on ? loadout.abilities.filter((a) => a !== id) : [...loadout.abilities, id]);
  };
  const toggleUpgrade = (id: string) => {
    const on = loadout.upgrades.includes(id);
    if (!on && loadout.upgrades.length >= maxU) return;
    props.setPresetId(null);
    props.setLoadout({ ...loadout, upgrades: c.upgradeOrder.filter((u) => (on ? u !== id && loadout.upgrades.includes(u) : u === id || loadout.upgrades.includes(u))) });
  };
  const utilities = Object.values(c.utilities).filter((u) => !u.requiresAbility || loadout.abilities.includes(u.requiresAbility));

  return (
    <div className="screen">
      <div style={{ marginBottom: 14 }}>
        <h2 style={{ fontSize: 22 }}>Loadout</h2>
        <div className="muted">
          Choose up to {maxA} of {c.abilityOrder.length} abilities and up to {maxU} upgrades, then fight Armory Lockdown. Nothing carries between attempts.
        </div>
      </div>
      <div className="loadout-grid">
        <div className="panel">
          <h3>
            Abilities{' '}
            <span className="mono">
              {loadout.abilities.length}/{maxA}
            </span>
          </h3>
          <div className="presets">
            {c.presets.map((p) => (
              <button
                key={p.id}
                className={`btn ${props.presetId === p.id ? 'primary' : ''}`}
                title={p.expected}
                onClick={() => {
                  props.setPresetId(p.id);
                  props.setLoadout({ abilities: [...p.abilities], upgrades: [...p.upgrades] });
                }}
              >
                {p.name} preset
              </button>
            ))}
            <button className="btn" onClick={() => setAbilities([])}>
              Clear
            </button>
          </div>
          <div className="choice-list">
            {c.abilityOrder.map((id) => {
              const a = c.abilities[id]!;
              const on = loadout.abilities.includes(id);
              const full = !on && loadout.abilities.length >= maxA;
              return (
                <button key={id} className={`choice ${on ? 'on' : ''}`} disabled={full} onClick={() => toggleAbility(id)} aria-pressed={on}>
                  <span className="check">{on ? '✓' : ''}</span>
                  <span>
                    <span className="title">{a.name}</span> <span className={`kit-tag ${a.kit}`}>{a.kit}</span>
                    <div className="rule">{a.text}</div>
                  </span>
                  <span className="mono" style={{ color: 'var(--accent)', fontWeight: 600 }}>
                    {a.ap} AP
                  </span>
                </button>
              );
            })}
          </div>
          <div style={{ padding: '0 12px 12px' }} className="muted">
            Always available: {utilities.map((u) => `${u.name} (${u.ap} AP)`).join(', ')}.
          </div>
        </div>
        <div style={{ display: 'grid', gap: 16 }}>
          <div className="panel">
            <h3>
              Upgrades{' '}
              <span className="mono">
                {loadout.upgrades.length}/{maxU}
              </span>
            </h3>
            <div className="choice-list">
              {c.upgradeOrder.map((id) => {
                const u = c.upgrades[id]!;
                const on = loadout.upgrades.includes(id);
                const allowed = upgradeAllowed(u.requires, loadout.abilities);
                const full = !on && loadout.upgrades.length >= maxU;
                return (
                  <button key={id} className={`choice ${on ? 'on' : ''}`} disabled={(!allowed && !on) || full} onClick={() => toggleUpgrade(id)} aria-pressed={on}>
                    <span className="check">{on ? '✓' : ''}</span>
                    <span>
                      <span className="title">{u.name}</span> <span className={`kit-tag ${u.kit}`}>{u.code}</span>
                      <div className="rule">{u.text}</div>
                      {!allowed && <div className="rule faint">Needs {describeRequires(c, u.requires)}</div>}
                    </span>
                    <span />
                  </button>
                );
              })}
            </div>
          </div>
          <div className="panel">
            <h3>Fight</h3>
            <div style={{ padding: '0 12px 8px' }}>
              <label style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
                <input type="checkbox" checked={props.settings.maxWaves === 2} onChange={(e) => props.setSettings(e.target.checked ? { ...props.settings, maxWaves: 2 } : { ...props.settings, maxWaves: undefined })} />
                Hold back wave 3: clearing wave 2 wins (the first playtest's setup)
              </label>
            </div>
            <div style={{ padding: '0 12px 8px' }}>
              <label style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
                Tester ID (playtests)
                <input type="text" value={props.tester ?? ''} maxLength={20} placeholder="P3" onChange={(e) => props.setTester(e.target.value)} style={{ width: '8em' }} />
              </label>
              <span className="muted">{props.tester ? 'Fights are labelled with this ID and an attempt number.' : 'Leave empty outside a playtest.'}</span>
            </div>
            <div className="start-bar">
              <button className="btn primary" disabled={problems.length > 0 || loadout.abilities.length === 0} onClick={props.onStart}>
                Start fight
              </button>
              <span className={problems.length ? 'warn-line' : 'muted'}>{problems.length ? problems.join('; ') : loadout.abilities.length === 0 ? 'Pick at least one ability.' : 'Ready.'}</span>
            </div>
          </div>
          {attempts.length > 0 && (
            <div className="panel">
              <h3>Recent attempts (this browser)</h3>
              <div className="info-body">
                {attempts.map((a, i) => (
                  <div key={i} className="info-row">
                    <span>
                      <strong style={{ color: a.outcome === 'win' ? 'var(--good)' : 'var(--danger)' }}>{a.outcome === 'win' ? 'Won' : 'Lost'}</strong> in round {a.rounds} · {a.preset ?? 'custom'}
                    </span>
                    <span className="faint mono">{new Date(a.at).toLocaleTimeString()}</span>
                  </div>
                ))}
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
