import { useState } from 'react';
import type { Content } from '../../content/types';
import type { IntentReport } from '../../preview/preview';
import { ENGINE_VERSION } from '../../state/state';
import type { AIOption, Command, GameEvent, GameState } from '../../state/types';
import type { FightRecord } from '../../telemetry/record';
import { tileName, isTileName } from '../../util/tiles';
import { hazardName, unitName } from '../format';

export interface DebugToggles {
  ids: boolean;
  spawns: boolean;
  hazards: boolean;
  flamer: boolean;
  log: boolean;
}

export function flamerOptionsFrom(rep: IntentReport | null): AIOption[] | null {
  if (!rep) return null;
  for (const f of rep.frames) {
    for (const e of f.events) if (e.t === 'AIDecision' && e.def === 'flamer' && e.options?.length) return e.options;
  }
  return null;
}

export function DebugPanel(props: {
  c: Content;
  s: GameState;
  events: GameEvent[];
  intents: IntentReport | null;
  toggles: DebugToggles;
  setToggles: (t: DebugToggles) => void;
  stepMode: boolean;
  setStepMode: (b: boolean) => void;
  pending: number;
  advance: (untilPhase?: boolean) => void;
  apply: (cmd: Command) => void;
  holdWave3: boolean;
  setHoldWave3: (b: boolean) => void;
  restart: () => void;
  onLoadReplay: (rec: FightRecord) => void;
  replayInfo: string | null;
  replayPlaying: boolean;
  setReplayPlaying: (b: boolean) => void;
  stepReplay: () => void;
  close: () => void;
}) {
  const { c, s, toggles } = props;
  const [hpUnit, setHpUnit] = useState(0);
  const [hpValue, setHpValue] = useState(1);
  const [apValue, setApValue] = useState(3);
  const [mvValue, setMvValue] = useState(3);
  const [spawnDef, setSpawnDef] = useState('guard');
  const [spawnTile, setSpawnTile] = useState('E4');
  const [loadError, setLoadError] = useState<string | null>(null);
  const t = (k: keyof DebugToggles) => (
    <label key={k} style={{ display: 'inline-flex', gap: 4, alignItems: 'center' }}>
      <input type="checkbox" checked={toggles[k]} onChange={(e) => props.setToggles({ ...toggles, [k]: e.target.checked })} />
      {k}
    </label>
  );
  const nextWave = c.waves[s.wavesSpawned];
  const suppressed = props.events.filter((e) => e.t === 'ChainSuppressed');
  const flamer = flamerOptionsFrom(props.intents);
  const units = s.units.filter((u) => !u.dead);
  return (
    <div className="panel debug" aria-label="Debug overlay">
      <h3>
        Debug overlay <span className="faint">engine {ENGINE_VERSION} · content {c.hash}</span>
        <button className="btn small" onClick={props.close}>
          Hide (`)
        </button>
      </h3>
      <section>
        <div className="row">{(['ids', 'spawns', 'hazards', 'flamer', 'log'] as const).map(t)}</div>
      </section>
      <section>
        <h4>Wave timer (hidden from players)</h4>
        <div>
          Round {s.round} · waves spawned {s.wavesSpawned}/{c.waves.length}
          {s.settings.maxWaves !== undefined ? ` (in play: ${s.settings.maxWaves})` : ''}
        </div>
        <div>{nextWave ? `Next: wave ${s.wavesSpawned + 1} at round ${nextWave.round} (${Math.max(0, nextWave.round - s.round)} rounds)` : 'No waves left'}</div>
        <div className="faint">{c.waves.map((w, i) => `W${i + 1}@R${w.round}: ${w.units.map((u) => `${u.def} ${u.tile}`).join(', ')}`).join(' | ')}</div>
        <div className="row" style={{ marginTop: 6 }}>
          <button className="btn small" disabled={s.wavesSpawned >= c.waves.length} onClick={() => props.apply({ type: 'debug', op: 'forceWave' })}>
            Force next wave
          </button>
          <label>
            <input type="checkbox" checked={props.holdWave3} onChange={(e) => props.setHoldWave3(e.target.checked)} /> Hold back wave 3 (restarts)
          </label>
        </div>
      </section>
      <section>
        <h4>Hazards</h4>
        <table>
          <thead>
            <tr>
              <th>Hazard</th>
              <th>State</th>
              <th>Last decision</th>
            </tr>
          </thead>
          <tbody>
            {s.hazards.map((h) => (
              <tr key={h.id}>
                <td>{hazardName(c, s, h.id)}</td>
                <td>{h.state === 'cooldown' ? `cooldown ${h.cooldown}` : h.state}</td>
                <td>{h.last ? `R${h.last.round} ${h.last.stage}: ${h.last.result}${h.last.step ? ` (step ${h.last.step})` : ''} — ${h.last.reason}` : '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>
      {flamer && (
        <section>
          <h4>Flamer options (scored now)</h4>
          <div className="faint">{flamer.filter((o) => o.score > 0).length} positive of {flamer.length} non-zero</div>
          <div>
            {[...flamer]
              .sort((a, b) => b.score - a.score)
              .slice(0, 8)
              .map((o, i) => (
                <div key={i}>
                  {tileName(o.tile)} {o.dir.y < 0 ? 'N' : o.dir.x > 0 ? 'E' : o.dir.y > 0 ? 'S' : 'W'} score {o.score} ({o.steps} steps)
                </div>
              ))}
          </div>
        </section>
      )}
      <section>
        <h4>Step-through</h4>
        <div className="row">
          <label>
            <input type="checkbox" checked={props.stepMode} onChange={(e) => props.setStepMode(e.target.checked)} /> Pause after each action
          </label>
          <button className="btn small" disabled={!props.stepMode} onClick={() => props.advance(false)}>
            Next action (N)
          </button>
          <button className="btn small" disabled={!props.stepMode} onClick={() => props.advance(true)}>
            Next phase (⇧N)
          </button>
          <span className="faint">{props.pending} queued</span>
        </div>
      </section>
      <section>
        <h4>Edit state (recorded in the command log)</h4>
        <div className="row">
          <select value={hpUnit} onChange={(e) => setHpUnit(Number(e.target.value))}>
            {units.map((u) => (
              <option key={u.id} value={u.id}>
                #{u.id} {unitName(c, u.def, u.id)}
              </option>
            ))}
          </select>
          <input type="number" value={hpValue} min={1} max={99} style={{ width: 52 }} onChange={(e) => setHpValue(Number(e.target.value))} />
          <button className="btn small" onClick={() => props.apply({ type: 'debug', op: 'setHp', unit: hpUnit, value: hpValue })}>
            Set HP
          </button>
        </div>
        <div className="row" style={{ marginTop: 6 }}>
          AP <input type="number" value={apValue} min={0} max={20} style={{ width: 46 }} onChange={(e) => setApValue(Number(e.target.value))} />
          Move <input type="number" value={mvValue} min={0} max={20} style={{ width: 46 }} onChange={(e) => setMvValue(Number(e.target.value))} />
          <button className="btn small" onClick={() => props.apply({ type: 'debug', op: 'setAp', ap: apValue, movement: mvValue })}>
            Set AP and movement
          </button>
        </div>
        <div className="row" style={{ marginTop: 6 }}>
          <select value={spawnDef} onChange={(e) => setSpawnDef(e.target.value)}>
            {Object.keys(c.units)
              .filter((d) => d !== 'player')
              .map((d) => (
                <option key={d} value={d}>
                  {d}
                </option>
              ))}
          </select>
          <input value={spawnTile} style={{ width: 46 }} onChange={(e) => setSpawnTile(e.target.value.toUpperCase())} />
          <button className="btn small" disabled={!isTileName(spawnTile)} onClick={() => props.apply({ type: 'debug', op: 'spawn', def: spawnDef, target: spawnTile })}>
            Spawn unit
          </button>
        </div>
      </section>
      <section>
        <h4>Replay playback and data</h4>
        <div className="row">
          <label className="btn small" style={{ cursor: 'pointer' }}>
            Load replay…
            <input
              type="file"
              accept="application/json,.json"
              style={{ display: 'none' }}
              onChange={async (e) => {
                const f = e.target.files?.[0];
                if (!f) return;
                try {
                  const rec = JSON.parse(await f.text()) as FightRecord;
                  if (rec.contentHash !== c.hash) throw new Error(`Recorded with content ${rec.contentHash}; current content is ${c.hash}`);
                  setLoadError(null);
                  props.onLoadReplay(rec);
                } catch (err) {
                  setLoadError(err instanceof Error ? err.message : String(err));
                }
                e.target.value = '';
              }}
            />
          </label>
          {props.replayInfo && (
            <>
              <button className="btn small" onClick={() => props.setReplayPlaying(!props.replayPlaying)}>
                {props.replayPlaying ? 'Pause' : 'Play'}
              </button>
              <button className="btn small" disabled={props.replayPlaying} onClick={props.stepReplay}>
                Step
              </button>
              <span className="faint">{props.replayInfo}</span>
            </>
          )}
          <button className="btn small" onClick={props.restart}>
            Restart fight
          </button>
        </div>
        {loadError && <div className="warn-line">{loadError}</div>}
        <div className="faint" style={{ marginTop: 4 }}>
          Edit any file in content/ and save: values hot-reload and the fight restarts.
        </div>
      </section>
      <section>
        <h4>Chain rule</h4>
        <div>{suppressed.length ? `${suppressed.length} suppressed trigger(s) this fight` : 'No suppressed triggers this fight'}</div>
      </section>
    </div>
  );
}
