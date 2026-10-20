import { Fragment, useEffect, useRef, type ReactNode } from 'react';
import type { Content } from '../../content/types';
import type { ActionStatus } from '../../engine/commands';
import type { CommandPreview, Intent, IntentReport } from '../../preview/preview';
import type { GameObject, GameState, Unit } from '../../state/types';
import { tileName } from '../../util/tiles';
import { actionKeyLabel } from '../actionKeys';
import { Icon } from '../art/icons';
import { UnitPortrait } from '../art/units';
import { describeObject, describeUnit, unitName, type LogLine } from '../format';
import { useTypewriter } from '../useTypewriter';

/** A row of segments, filled up to n: the HUD's vitals, action points and movement. */
function Segments({ n, max, cls, label }: { n: number; max: number; cls: string; label: string }) {
  return (
    <span className={cls} role="img" aria-label={`${label}: ${n} of ${max}`}>
      {Array.from({ length: Math.max(max, n) }, (_, i) => (
        <i key={i} className={i < n ? 'full' : ''} />
      ))}
    </span>
  );
}

/** AP cost as small slanted cells, as on the HUD. */
export function ApPips({ n }: { n: number }) {
  return (
    <span className="ap-pips" aria-hidden="true">
      {Array.from({ length: n }, (_, i) => (
        <i key={i} />
      ))}
    </span>
  );
}

export function PanelTitle({ icon, children, led = '', right }: { icon?: string; children: ReactNode; led?: string; right?: ReactNode }) {
  return (
    <h3>
      <span className="title-left">
        {icon ? <Icon name={icon} size={16} /> : <span className={`led ${led}`} />}
        {children}
      </span>
      {right}
    </h3>
  );
}

export function StatusStrip({ c, s, intents, busy }: { c: Content; s: GameState; intents: IntentReport | null; busy: boolean }) {
  const p = s.units.find((u) => u.id === 0);
  const hp = p?.hp ?? 0;
  const maxHp = p?.maxHp ?? c.units['player']!.hp;
  const incoming = intents?.playerDamage ?? 0;
  const lethal = !!intents?.playerDies;
  const phaseName: Record<string, string> = { roundStart: 'Round start', player: 'Your turn', minion: 'Minions', enemy: 'Enemies', environment: 'Environment', ended: 'Fight over' };
  return (
    <div className="panel hud crt" role="status">
      <div className={`cell hp-cell ${hp <= maxHp / 3 ? 'low' : ''}`}>
        <Icon name="heart" size={22} />
        <span className="label">HP</span>
        <Segments n={hp} max={maxHp} cls="vitals" label="HP" />
        <span className="value mono" style={{ fontSize: 17, fontFamily: 'var(--mono)', paddingTop: 0 }}>
          {hp}/{maxHp}
        </span>
        {p?.statuses.poison && (
          <span className="pill" style={{ color: 'var(--poison)', borderColor: 'var(--poison)' }}>
            <Icon name="poison" size={13} /> Poisoned {p.statuses.poison.remaining}
          </span>
        )}
        {p?.statuses.parasite && (
          <span className="pill" style={{ color: 'var(--parasite)', borderColor: 'var(--parasite)' }}>
            <Icon name="parasite" size={13} /> Parasite {p.statuses.parasite.remaining}
          </span>
        )}
      </div>
      <div className="cell">
        <Icon name="ap" size={18} />
        <span className="label">AP</span>
        <Segments n={s.ap} max={c.rules.turn.ap} cls="cells ap" label="AP" />
      </div>
      <div className="cell">
        <span className="label">Move</span>
        <Segments n={s.movement} max={c.rules.turn.movement} cls="cells mv" label="Movement" />
      </div>
      <div className="cell">
        <span className="label">Round</span>
        <span className="value">{String(s.round).padStart(2, '0')}</span>
      </div>
      <div className="cell">
        <span className="label">Phase</span>
        <span className="value small">{phaseName[s.phase] ?? s.phase}</span>
      </div>
      {!busy && s.phase === 'player' && intents && (
        <div className={`cell incoming ${incoming > 0 ? 'bad' : ''} ${lethal ? 'lethal' : ''}`} title="Damage you would take from minions and enemies if you ended the turn now (hazards excluded)">
          {(lethal || incoming > 0) && <Icon name="alert" size={18} />}
          {lethal ? 'Ending the turn now is lethal' : incoming > 0 ? `End turn now: −${incoming} HP` : 'No incoming attacks'}
        </div>
      )}
    </div>
  );
}

export function ActionBar(props: {
  c: Content;
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
        const kit = props.c.abilities[st.id]?.kit ?? 'utility';
        return (
          <Fragment key={st.id}>
            <button
              className={`action-btn ${kit} ${props.selected === st.id ? 'selected' : ''}`}
              disabled={props.disabled || !st.usable}
              onClick={() => props.onSelect(st.id)}
              title={st.reason ?? st.name}
              aria-pressed={props.selected === st.id}
            >
              <span className="ab-icon">
                <Icon name={st.id} size={22} />
              </span>
              <span className="name">{st.name}</span>
              <span className="key">{keyLabel}</span>
              <span className="cost">
                <ApPips n={st.ap} /> {st.ap} AP
              </span>
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
      <PanelTitle icon="scan">{preview ? 'Preview' : props.hoverUnit ? 'Scan: unit' : props.hoverObjects.length || props.hoverNote ? 'Scan: tile' : 'Scanner'}</PanelTitle>
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
            <div className="info-head">
              <UnitPortrait def={props.hoverUnit.def} team={props.hoverUnit.team} size={44} />
              <div>
                <div className="name">{unitName(c, props.hoverUnit.def, props.hoverUnit.id)}</div>
                <span className="faint mono">{tileName(props.hoverUnit.pos)}</span>
              </div>
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
      <PanelTitle
        icon="minion"
        right={
          <span className="mono faint" style={{ fontSize: 12, letterSpacing: 0 }}>
            {minions.length}/{c.rules.caps.minions}
          </span>
        }
      >
        Minions
      </PanelTitle>
      <div className="minion-list">
        {!minions.length && <div className="muted" style={{ padding: '0 2px 4px' }}>None deployed.</div>}
        {minions.map((m) => (
          <div className="minion" key={m.id}>
            <UnitPortrait def={m.def} team={m.team} size={30} />
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
  const text = last ? `“${last.text}”` : '';
  const { shown, done } = useTypewriter([text], 60);
  return (
    <div className="panel">
      <PanelTitle
        led="amber blink"
        right={
          <button className="btn small" onClick={onOpenLog} title="Open the transmission log (L)">
            Log ({s.intercom.log.length}) <kbd>L</kbd>
          </button>
        }
      >
        Intercom
        <span className={`speaker ${last && !done ? 'live' : ''}`} aria-hidden="true">
          <i />
          <i />
          <i />
          <i />
        </span>
      </PanelTitle>
      <div className="intercom">
        {last ? (
          <div className="line crt">
            <span aria-hidden="true">{shown[0]}</span>
            <span style={{ position: 'absolute', width: 1, height: 1, overflow: 'hidden', clip: 'rect(0 0 0 0)' }}>{text}</span>
          </div>
        ) : (
          <div className="line crt muted">Static.</div>
        )}
      </div>
    </div>
  );
}

export function IntercomLogModal({ s, onClose }: { s: GameState; onClose: () => void }) {
  return (
    <div className="modal-back" onClick={onClose}>
      <div className="panel modal crt" onClick={(e) => e.stopPropagation()} role="dialog" aria-label="Transmission log">
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 10 }}>
          <h2 className="display" style={{ fontSize: 22, color: 'var(--term)' }}>
            Transmission log
          </h2>
          <button className="btn small" onClick={onClose}>
            Close <kbd>Esc</kbd>
          </button>
        </div>
        {s.intercom.log.map((l, i) => (
          <div key={i} style={{ padding: '7px 0', borderTop: '1px solid var(--line)', fontFamily: 'var(--mono)', fontSize: 13 }}>
            <span className="faint">R{String(l.round).padStart(2, '0')} · </span>
            <span style={{ color: 'var(--amber)' }}>“{l.text}”</span>
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
      <PanelTitle icon="log">Combat log</PanelTitle>
      <div className="log crt" ref={ref} aria-live="polite">
        {lines.map((l) => (
          <div key={l.id} className={l.kind === 'round' ? 'round' : l.kind === 'phase' ? 'phase' : `ev ${l.kind}`}>
            {l.text}
          </div>
        ))}
      </div>
    </div>
  );
}
