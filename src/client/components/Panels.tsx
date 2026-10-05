import { Fragment, useEffect, useRef } from 'react';
import type { Content } from '../../content/types';
import type { ActionStatus } from '../../engine/commands';
import type { CommandPreview, Intent, IntentReport } from '../../preview/preview';
import type { GameObject, GameState, Unit } from '../../state/types';
import { tileName } from '../../util/tiles';
import { actionKeyLabel } from '../actionKeys';
import { describeObject, describeUnit, unitName, type LogLine } from '../format';

export function Pips({ n, max, cls, round }: { n: number; max: number; cls: string; round?: boolean }) {
  return (
    <span className={`pips ${cls}`} aria-label={`${n} of ${max}`}>
      {Array.from({ length: Math.max(max, n) }, (_, i) => (
        <span key={i} className={`pip ${round ? 'round' : ''} ${i < n ? 'full' : ''}`} />
      ))}
    </span>
  );
}

export function StatusStrip({ c, s, intents, busy }: { c: Content; s: GameState; intents: IntentReport | null; busy: boolean }) {
  const p = s.units.find((u) => u.id === 0);
  const hp = p?.hp ?? 0;
  const incoming = intents?.playerDamage ?? 0;
  const lethal = !!intents?.playerDies;
  const phaseName: Record<string, string> = { roundStart: 'Round start', player: 'Your turn', minion: 'Minions', enemy: 'Enemies', environment: 'Environment', ended: 'Fight over' };
  return (
    <div className="panel status-strip" role="status">
      <div className="stat">
        <span className="label">HP</span>
        <Pips n={hp} max={p?.maxHp ?? c.units['player']!.hp} cls="hp-color" round />
        <span className="mono">
          {hp}/{p?.maxHp ?? c.units['player']!.hp}
        </span>
        {p?.statuses.poison && <span className="pill" style={{ color: 'var(--poison)' }}>Poisoned {p.statuses.poison.remaining}</span>}
        {p?.statuses.parasite && <span className="pill" style={{ color: 'var(--parasite)' }}>Parasite {p.statuses.parasite.remaining}</span>}
      </div>
      <div className="stat">
        <span className="label">AP</span>
        <Pips n={s.ap} max={c.rules.turn.ap} cls="ap-color" />
      </div>
      <div className="stat">
        <span className="label">Move</span>
        <Pips n={s.movement} max={c.rules.turn.movement} cls="mv-color" />
      </div>
      <div className="stat">
        <span className="label">Round</span>
        <span className="mono" style={{ fontWeight: 700 }}>
          {s.round}
        </span>
      </div>
      <div className="stat">
        <span className="label">Phase</span>
        <span>{phaseName[s.phase] ?? s.phase}</span>
      </div>
      {!busy && s.phase === 'player' && intents && (
        <div className={`incoming ${incoming > 0 ? 'bad' : ''}`} title="Damage you would take from minions and enemies if you ended the turn now (hazards excluded)">
          {lethal ? 'Ending the turn now is lethal' : incoming > 0 ? `End turn now: −${incoming} HP` : 'No incoming attacks'}
        </div>
      )}
    </div>
  );
}

export function ActionBar(props: {
  statuses: ActionStatus[];
  selected: string | null;
  onSelect: (id: string) => void;
  disabled: boolean;
  grappleMode: 'self' | 'unit';
  onGrappleMode: (m: 'self' | 'unit') => void;
}) {
  return (
    <div className="action-row" role="toolbar" aria-label="Actions">
      {props.statuses.map((st, i) => {
        const keyLabel = actionKeyLabel(i);
        const showModes = st.id === 'grapple_hook' && props.selected === st.id;
        return (
          <Fragment key={st.id}>
            <button
              className={`action-btn ${props.selected === st.id ? 'selected' : ''}`}
              disabled={props.disabled || !st.usable}
              onClick={() => props.onSelect(st.id)}
              title={st.reason ?? st.name}
              aria-pressed={props.selected === st.id}
            >
              <span className="key">{keyLabel}</span>
              <span>{st.name}</span>
              <span className="cost">{st.ap} AP</span>
              {!st.usable && st.reason && <span className="why">{st.reason}</span>}
            </button>
            {showModes && (
              <span className="seg" role="group" aria-label="Grapple mode (Tab)" style={{ alignSelf: 'center' }}>
                <button className={props.grappleMode === 'unit' ? 'on' : ''} onClick={() => props.onGrappleMode('unit')}>
                  Pull unit
                </button>
                <button className={props.grappleMode === 'self' ? 'on' : ''} onClick={() => props.onGrappleMode('self')}>
                  Pull self
                </button>
              </span>
            )}
          </Fragment>
        );
      })}
    </div>
  );
}

export function InfoPanel(props: {
  c: Content;
  s: GameState;
  hoverUnit: Unit | null;
  hoverObjects: GameObject[];
  hoverNote: string | null;
  intent: Intent | null;
  preview: CommandPreview | null;
  previewTitle: string | null;
  hint: string;
}) {
  const { c, preview } = props;
  const name = (kind: 'unit' | 'object', id: number, def: string) => (kind === 'unit' ? unitName(c, def, id) : def === 'shield' ? 'Shield segment' : def === 'egg' ? 'Egg' : def);
  return (
    <div className="panel">
      <h3>{preview ? 'Preview' : props.hoverUnit ? 'Unit' : props.hoverObjects.length || props.hoverNote ? 'Tile' : 'Info'}</h3>
      <div className="info-body">
        {preview && preview.ok && (
          <>
            <div style={{ fontWeight: 600 }}>{props.previewTitle}</div>
            {preview.hits.length === 0 && preview.moves.length === 0 && preview.placed.length === 0 && preview.spawned.length === 0 && <div className="muted">No effect on any unit.</div>}
            {preview.hits.map((h) => (
              <div key={`${h.ref.kind}${h.ref.id}`} className={h.friendly ? 'warn-line' : undefined}>
                {h.friendly ? '⚠ ' : ''}
                {name(h.ref.kind, h.ref.id, h.def)}: {h.damage > 0 || h.absorbed > 0 ? `${h.damage} damage${h.absorbed ? ` (${h.absorbed} absorbed)` : ''}` : ''}
                {h.healed ? ` +${h.healed} HP` : ''}
                {h.statuses.length ? ` +${h.statuses.join(', +')}` : ''}
                {h.killed ? ' — dies' : ''}
              </div>
            ))}
            {preview.placed.map((o) => (
              <div key={`pl${o.id}`}>{describeObject(c, o, true)}</div>
            ))}
            {preview.spawned.map((u) => (
              <div key={`sp${u.id}`}>
                {unitName(c, u.def, u.id)} appears at {tileName(u.pos)}.
              </div>
            ))}
            {preview.moves.map((m) => {
              const u = preview.state?.units.find((x) => x.id === m.id) ?? props.s.units.find((x) => x.id === m.id);
              return m.id === 0 ? (
                <div key="me">You end on {tileName(m.to)}.</div>
              ) : (
                <div key={`mv${m.id}`}>
                  {u ? unitName(c, u.def, u.id) : `Unit ${m.id}`} ends on {tileName(m.to)}.
                </div>
              );
            })}
            {preview.minesTriggered.length > 0 && <div className="warn-line">Sets off a mine at {preview.minesTriggered.map(tileName).join(', ')}.</div>}
            {preview.friendlyFire && <div className="warn-line">Friendly fire: your own units are hit.</div>}
            {preview.outcome && <div className={preview.outcome.result === 'win' ? '' : 'warn-line'}>{preview.outcome.result === 'win' ? 'This wins the fight.' : 'This kills you.'}</div>}
            {preview.intents && (
              <div className={preview.intents.playerDies ? 'warn-line' : 'muted'}>
                Then, if you end the turn: {preview.intents.playerDies ? 'you die' : preview.intents.playerDamage > 0 ? `you take ${preview.intents.playerDamage}` : 'no attacks reach you'}.
              </div>
            )}
          </>
        )}
        {preview && !preview.ok && <div className="warn-line">{preview.error}</div>}
        {!preview && props.hoverUnit && (
          <>
            <div style={{ fontWeight: 600 }}>
              {unitName(c, props.hoverUnit.def, props.hoverUnit.id)} <span className="faint mono">{tileName(props.hoverUnit.pos)}</span>
            </div>
            <div className="muted">{describeUnit(c, props.hoverUnit, props.s.round)}</div>
            {props.intent && <div>Intent: {props.intent.label}</div>}
          </>
        )}
        {!preview &&
          props.hoverObjects.map((o) => (
            <div key={`ho${o.id}`} className="muted">
              {describeObject(c, o)}
            </div>
          ))}
        {!preview && props.hoverNote && <div className="muted">{props.hoverNote}</div>}
        {!preview && !props.hoverUnit && !props.hoverObjects.length && !props.hoverNote && <div className="muted">{props.hint}</div>}
      </div>
    </div>
  );
}

export function MinionPanel({ c, s }: { c: Content; s: GameState }) {
  const minions = s.units.filter((u) => u.kind === 'minion' && !u.dead);
  return (
    <div className="panel">
      <h3>
        Minions{' '}
        <span className="mono faint">
          {minions.length}/{c.rules.caps.minions}
        </span>
      </h3>
      <div className="minion-list">
        {!minions.length && <div className="muted" style={{ padding: '0 2px 4px' }}>None yet.</div>}
        {minions.map((m) => (
          <div className="minion" key={m.id}>
            <span className="mono" style={{ fontWeight: 700, textAlign: 'center' }}>
              {m.def === 'turret' ? 'T' : m.def === 'drone' ? 'D' : m.def === 'spitter' ? 's' : m.def === 'burster' ? 'b' : 'h'}
            </span>
            <div>
              <div style={{ display: 'flex', justifyContent: 'space-between', gap: 6 }}>
                <span>
                  {unitName(c, m.def, m.id)} <span className="faint mono">{m.droneState === 'docked' ? 'docked' : tileName(m.pos)}</span>
                </span>
                <span className="mono faint">
                  {m.hp}/{m.maxHp}
                </span>
              </div>
              <div className="hpbar">
                <div style={{ width: `${(100 * m.hp) / m.maxHp}%` }} />
              </div>
            </div>
            <span className="mono faint" style={{ fontSize: 11 }}>
              {m.ammo !== undefined ? `ammo ${m.ammo}` : m.charges !== undefined ? `${m.droneState === 'deployed' ? `${m.charges} chg` : m.droneState}` : ''}
            </span>
          </div>
        ))}
      </div>
    </div>
  );
}

export function IntercomPanel({ s, onOpenLog }: { s: GameState; onOpenLog: () => void }) {
  const last = s.intercom.log[s.intercom.log.length - 1];
  return (
    <div className="panel">
      <h3>
        Intercom
        <button className="btn small" onClick={onOpenLog} title="Open the transmission log (L)">
          Log ({s.intercom.log.length})
        </button>
      </h3>
      <div className="intercom">{last ? <div className="line">“{last.text}”</div> : <div className="muted">Static.</div>}</div>
    </div>
  );
}

export function IntercomLogModal({ s, onClose }: { s: GameState; onClose: () => void }) {
  return (
    <div className="modal-back" onClick={onClose}>
      <div className="panel modal" onClick={(e) => e.stopPropagation()} role="dialog" aria-label="Transmission log">
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 10 }}>
          <h2 style={{ fontSize: 16 }}>Transmission log</h2>
          <button className="btn small" onClick={onClose}>
            Close
          </button>
        </div>
        {s.intercom.log.map((l, i) => (
          <div key={i} style={{ padding: '6px 0', borderTop: '1px solid var(--line)' }}>
            <span className="faint mono">Round {l.round} · </span>“{l.text}”
          </div>
        ))}
      </div>
    </div>
  );
}

export function CombatLog({ lines }: { lines: LogLine[] }) {
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const el = ref.current;
    if (el) el.scrollTop = el.scrollHeight;
  }, [lines.length]);
  return (
    <div className="panel">
      <h3>Combat log</h3>
      <div className="log" ref={ref} aria-live="polite">
        {lines.map((l) => (
          <div key={l.id} className={l.kind === 'round' ? 'round' : l.kind === 'phase' ? 'phase' : `ev ${l.kind}`}>
            {l.text}
          </div>
        ))}
      </div>
    </div>
  );
}
