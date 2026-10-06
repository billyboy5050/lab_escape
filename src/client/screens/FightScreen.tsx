import { useCallback, useEffect, useMemo, useState } from 'react';
import type { Content } from '../../content/types';
import { actionStatuses, commandFor, moveCommand, moveOptions, type ActionStatus } from '../../engine/commands';
import type { TargetOption } from '../../effects/abilities';
import { computeIntents, previewCommand } from '../../preview/preview';
import { samePos } from '../../state/grid';
import type { Command, FightSettings, Loadout, Pos } from '../../state/types';
import type { FightRecord } from '../../telemetry/record';
import type { FightSession } from '../../telemetry/session';
import { parseTile, tileName } from '../../util/tiles';
import { Board, type Highlight } from '../components/Board';
import { DebugPanel, flamerOptionsFrom, type DebugToggles } from '../components/DebugPanel';
import { ActionBar, CombatLog, InfoPanel, IntercomLogModal, IntercomPanel, MinionPanel, StatusStrip } from '../components/Panels';
import { actionIndexForKey, endsTurn, spaceSkips } from '../actionKeys';
import { nextAttempt, saveTelemetry } from '../telemetry';
import { useFight, type Speed } from '../useFight';

interface Mode {
  id: string | null;
  /** Two-step actions (Barrier Shield, Lunge): the first tile chosen. */
  first: Pos | null;
}

function uniqTiles(ps: Pos[]): Pos[] {
  const out: Pos[] = [];
  for (const p of ps) if (!out.some((q) => samePos(p, q))) out.push(p);
  return out;
}

export function FightScreen(props: {
  c: Content;
  loadout: Loadout;
  settings: FightSettings;
  setSettings: (s: FightSettings) => void;
  presetId: string | null;
  replay: FightRecord | null;
  restartKey: number;
  onRestart: () => void;
  onLoadReplay: (rec: FightRecord) => void;
  /** `save` settles with the telemetry folder (null when it was not saved); it is null when no save was started (a replay). */
  onFinished: (session: FightSession, save: Promise<string | null> | null) => void;
  onChangeLoadout: () => void;
  /** Who is playing, for the telemetry folder name and the replay's meta. */
  tester?: string;
  debugOpen: boolean;
  setDebugOpen: (b: boolean) => void;
  toast: (msg: string) => void;
}) {
  const { c, loadout, settings } = props;
  const [toggles, setToggles] = useState<DebugToggles>({ ids: true, spawns: true, hazards: true, flamer: true, log: false });
  const fight = useFight(c, loadout, settings, {
    debugLog: props.debugOpen && toggles.log,
    restartKey: props.restartKey,
    replay: props.replay,
    meta: { preset: props.presetId ?? undefined, ...(props.tester ? { tester: props.tester, attempt: nextAttempt(props.tester) } : {}) },
  });
  const live = fight.session.state;
  const [cursor, setCursor] = useState(0);
  const [replayPlaying, setReplayPlaying] = useState(!!props.replay);
  const replayActive = !!props.replay && cursor < props.replay.commands.length;
  const idle = !fight.busy && live.phase === 'player' && !live.outcome && !replayActive;
  const [mode, setMode] = useState<Mode>({ id: null, first: null });
  const [grappleMode, setGrappleMode] = useState<'self' | 'unit'>('unit');
  const [hover, setHover] = useState<Pos | null>(null);
  const [showIntents, setShowIntents] = useState(true);
  const [logOpen, setLogOpen] = useState(false);
  // The telemetry save, started once when the fight is over. The promise itself is kept (not just its result) so that
  // a click on See results before it settles hands it to the result screen, which then shows how it ended.
  const [save, setSave] = useState<Promise<string | null> | null>(null);

  const statuses = useMemo(() => actionStatuses(c, live), [c, live]);
  const moves = useMemo(() => (idle ? moveOptions(c, live) : []), [c, live, idle]);
  const intents = useMemo(() => (live.phase === 'player' && !live.outcome ? computeIntents(c, live) : null), [c, live]);
  const st: ActionStatus | undefined = mode.id ? statuses.find((a) => a.id === mode.id) : undefined;

  // Drop a selection that is no longer usable (after AP is spent, or a new round).
  useEffect(() => {
    if (mode.id && (!st || !st.usable)) setMode({ id: null, first: null });
  }, [mode.id, st]);

  const options: TargetOption[] = useMemo(() => {
    if (!st) return [];
    return st.id === 'grapple_hook' ? st.options.filter((o) => o.mode === grappleMode) : st.options;
  }, [st, grappleMode]);
  const twoStep = st?.id === 'barrier_shield' || st?.id === 'lunge';
  // Barrier Shield pairs are unordered: either tile of a pair can be picked first.
  const pairs = st?.id === 'barrier_shield';
  const firstTiles = useMemo(() => uniqTiles(options.flatMap((o) => (pairs && o.target2 ? [o.target, o.target2] : [o.target]))), [options, pairs]);
  const secondTiles = useMemo(() => {
    if (!mode.first) return [];
    const f = mode.first;
    const out: Pos[] = [];
    for (const o of options) {
      if (!o.target2) continue;
      if (samePos(o.target, f)) out.push(o.target2);
      else if (pairs && samePos(o.target2, f)) out.push(o.target);
    }
    return uniqTiles(out);
  }, [options, mode.first, pairs]);

  const candidateFor = useCallback(
    (tile: Pos): Command | null => {
      if (!st) {
        const m = moves.find((x) => samePos(x.tile, tile));
        return m ? moveCommand(m.path) : null;
      }
      if (st.id === 'sprint') return null;
      if (!twoStep) {
        const o = options.find((x) => samePos(x.target, tile));
        return o ? commandFor(st, o) : null;
      }
      if (!mode.first) {
        const o =
          st.id === 'lunge'
            ? options.find((x) => samePos(x.target, tile) && !x.target2)
            : options.find((x) => samePos(x.target, tile) || (x.target2 && samePos(x.target2, tile)));
        return o ? commandFor(st, o) : null;
      }
      if (st.id === 'lunge' && samePos(tile, mode.first)) {
        const o = options.find((x) => samePos(x.target, tile) && !x.target2);
        return o ? commandFor(st, o) : null;
      }
      const f = mode.first;
      const o = options.find((x) => x.target2 && ((samePos(x.target, f) && samePos(x.target2, tile)) || (st.id === 'barrier_shield' && samePos(x.target2, f) && samePos(x.target, tile))));
      return o ? commandFor(st, o) : null;
    },
    [st, moves, options, twoStep, mode.first],
  );

  const hoverCmd = hover && idle ? candidateFor(hover) : null;
  const hoverKey = hoverCmd ? JSON.stringify(hoverCmd) : '';
  const preview = useMemo(() => (hoverCmd ? previewCommand(c, live, hoverCmd, { intents: true }) : null), [c, live, hoverKey]); // eslint-disable-line react-hooks/exhaustive-deps

  const commit = useCallback(
    (cmd: Command) => {
      const r = fight.apply(cmd);
      if (!r.ok) props.toast(r.error ?? 'Not allowed');
      else setMode({ id: null, first: null });
    },
    [fight, props],
  );

  const selectAction = useCallback(
    (id: string) => {
      const a = statuses.find((x) => x.id === id);
      if (!idle || !a) return;
      if (!a.usable) {
        props.toast(`${a.name}: ${a.reason}`);
        return;
      }
      if (id === 'sprint') return commit({ type: 'sprint' });
      if (mode.id === id) return setMode({ id: null, first: null });
      setMode({ id, first: null });
      if (id === 'grapple_hook') setGrappleMode(a.options.some((o) => o.mode === 'unit') ? 'unit' : 'self');
    },
    [statuses, idle, mode.id, commit, props],
  );

  const onTileClick = useCallback(
    (tile: Pos, touch: boolean) => {
      if (touch && !(hover && samePos(hover, tile))) {
        setHover(tile);
        return;
      }
      if (!idle) return;
      if (!st) {
        const m = moves.find((x) => samePos(x.tile, tile));
        if (m) commit(moveCommand(m.path));
        return;
      }
      if (!twoStep) {
        const cmd = candidateFor(tile);
        if (cmd) commit(cmd);
        return;
      }
      if (!mode.first) {
        if (!firstTiles.some((t) => samePos(t, tile))) return;
        const strikes = options.filter((o) => samePos(o.target, tile) && o.target2);
        if (st.id === 'lunge' && strikes.length === 0) {
          const cmd = candidateFor(tile);
          if (cmd) commit(cmd);
          return;
        }
        setMode({ id: mode.id, first: tile });
        return;
      }
      const cmd = candidateFor(tile);
      if (cmd) commit(cmd);
      else setMode({ id: mode.id, first: null });
    },
    [hover, idle, st, moves, twoStep, candidateFor, mode, firstTiles, options, commit],
  );

  // Applies the next recorded command. A command the engine rejects (a corrupted or hand-edited file) stops playback
  // there and says so: carrying on would show a fight that never happened, built from the commands that still apply.
  const playReplayCommand = () => {
    if (!props.replay) return;
    const r = fight.apply(props.replay.commands[cursor]!);
    if (!r.ok) {
      setReplayPlaying(false);
      props.toast(`Replay stopped: command ${cursor + 1} of ${props.replay.commands.length} was rejected (${r.error ?? 'not allowed'})`);
      return;
    }
    setCursor((x) => x + 1);
  };

  // Replay playback: feed recorded commands one at a time once each animation finishes.
  useEffect(() => {
    if (!props.replay || !replayPlaying || fight.busy) return;
    if (cursor >= props.replay.commands.length) {
      setReplayPlaying(false);
      return;
    }
    const t = window.setTimeout(playReplayCommand, 120);
    return () => window.clearTimeout(t);
    // playReplayCommand is rebuilt every render and reads only what these dependencies cover. `fight` itself is left out
    // on purpose: it is a new object every render, so depending on it would restart the 120 ms timer on every hover.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [props.replay, replayPlaying, fight.busy, fight.apply, cursor, props.toast]);

  // Save telemetry once the fight is over and the last animation has played.
  useEffect(() => {
    if (!live.outcome || fight.busy || save !== null || props.replay) return;
    setSave(saveTelemetry(c, fight.session));
  }, [live.outcome, fight.busy, save, c, fight.session, props.replay]);

  // Keyboard: every control has a shortcut.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const el = e.target as HTMLElement | null;
      if (el && (el.tagName === 'INPUT' || el.tagName === 'SELECT' || el.tagName === 'TEXTAREA')) return;
      if (e.key === '`') {
        props.setDebugOpen(!props.debugOpen);
        return e.preventDefault();
      }
      if ((e.key === 'z' || e.key === 'Z') && !e.shiftKey) {
        // Only when the player can act: between the commands of a replay the session still has an undo entry, and
        // undoing then would leave the replay's cursor ahead of the state it feeds the next command.
        if (idle && !fight.undo()) props.toast('Nothing to undo: AP spent or nothing moved');
        return e.preventDefault();
      }
      if (e.key === 'Escape') {
        if (logOpen) return setLogOpen(false);
        if (mode.first) return setMode({ id: mode.id, first: null });
        return setMode({ id: null, first: null });
      }
      if (e.key === ' ') {
        // A focused button (or link, or summary) is operated by Space; only otherwise does it skip the animation.
        if (!spaceSkips(el as HTMLElement | null)) return;
        if (fight.busy) fight.skip();
        return e.preventDefault();
      }
      if (e.key === 'f' || e.key === 'F') {
        fight.setSpeed(fight.speed === 1 ? 2 : fight.speed === 2 ? 0 : 1);
        return;
      }
      if (e.key === 'i' || e.key === 'I') return setShowIntents((v) => !v);
      if (e.key === 'l' || e.key === 'L') return setLogOpen((v) => !v);
      if (e.key === 'n' || e.key === 'N') {
        if (fight.stepMode) fight.advance(e.shiftKey);
        return;
      }
      if (e.key === 'Tab' && mode.id === 'grapple_hook') {
        setGrappleMode((m) => (m === 'unit' ? 'self' : 'unit'));
        return e.preventDefault();
      }
      if (idle && endsTurn(e.key, el)) {
        commit({ type: 'endTurn' });
        // Without this a focused button would also be clicked by the same Enter.
        return e.preventDefault();
      }
      // Modified keys are the browser's (Ctrl and Cmd with - and = zoom the page).
      const slot = e.ctrlKey || e.metaKey || e.altKey ? -1 : actionIndexForKey(e.key);
      if (slot >= 0) {
        const a = statuses[slot];
        if (a) selectAction(a.id);
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [fight, idle, mode, logOpen, statuses, selectAction, commit, props]);

  const highlights: Highlight[] = [];
  if (idle && !st) highlights.push({ tiles: moves.map((m) => m.tile), kind: 'move' });
  if (idle && st && st.id !== 'sprint') {
    if (twoStep && mode.first) {
      highlights.push({ tiles: [mode.first], kind: 'chosen' });
      highlights.push({ tiles: secondTiles, kind: 'second' });
    } else highlights.push({ tiles: firstTiles, kind: 'target' });
  }
  const spawns = props.debugOpen && toggles.spawns ? c.waves.flatMap((w, i) => (i >= live.wavesSpawned ? w.units.map((u) => ({ tile: parseTile(u.tile), wave: i + 1 })) : [])) : [];

  const hoverMove = idle && !st && hover ? moves.find((m) => samePos(m.tile, hover)) : undefined;
  const shown = fight.shown;
  const hoverUnit = hover ? shown.units.find((u) => !u.dead && u.droneState !== 'docked' && samePos(u.pos, hover)) ?? null : null;
  const hoverIntent = hoverUnit && intents ? intents.intents.find((i) => i.id === hoverUnit.id) ?? null : null;
  const hoverObjects = hover ? shown.objects.filter((o) => samePos(o.pos, hover)) : [];
  const hoverHazard = hover ? shown.hazards.find((h) => h.tiles.some((t) => samePos(t, hover))) : undefined;
  const hoverNote = hoverHazard
    ? `${c.hazardDefs[hoverHazard.def]?.name ?? hoverHazard.def}${hoverHazard.state === 'primed' ? `. ${c.hazardDefs[hoverHazard.def]?.cue ?? ''}` : '.'}`
    : hover && c.map.legend[c.map.rows[hover.y]![hover.x]!] === 'shutter'
      ? 'Shutter: impassable; blocks projectiles but not sight.'
      : null;
  const previewTitle = hoverCmd
    ? hoverCmd.type === 'move'
      ? `Move to ${hoverCmd.path[hoverCmd.path.length - 1]}`
      : hoverCmd.type === 'ability'
        ? `${c.abilities[hoverCmd.ability]?.name}${hoverCmd.mode ? ` (pull ${hoverCmd.mode})` : ''} → ${hoverCmd.target}${hoverCmd.target2 ? ` / ${hoverCmd.target2}` : ''}`
        : `${st?.name ?? ''} → ${'target' in hoverCmd ? hoverCmd.target : ''}`
    : null;
  const hint = !idle
    ? replayActive
      ? 'Replaying a recorded fight.'
      : fight.busy
        ? 'Resolving… press Space to skip.'
        : live.outcome
          ? 'The fight is over.'
          : ''
    : st
      ? twoStep
        ? st.id === 'barrier_shield'
          ? mode.first
            ? 'Barrier Shield: pick the second tile (adjacent to the first). Esc to go back.'
            : 'Barrier Shield: pick the first tile.'
          : mode.first
            ? 'Lunge: pick a unit to hit, or click the landing tile again to land without hitting.'
            : 'Lunge: pick a landing tile.'
        : `${st.name}: pick a highlighted target. Esc cancels.${st.id === 'grapple_hook' ? ' Tab switches pull self / pull unit.' : ''}`
      : 'Click a highlighted tile to move (free, undo with Z). Pick an action below or press 1–9. Hover a unit to see its intent. End the turn with E.';

  const debugBoard = props.debugOpen ? { ids: toggles.ids, spawns, flamer: toggles.flamer ? flamerOptionsFrom(intents) : null, hazards: toggles.hazards } : null;

  return (
    <div className="fight">
      <div className="fight-main">
      <StatusStrip c={c} s={shown} intents={idle ? intents : null} busy={fight.busy} />
      <div className="panel board-wrap">
        <Board
          c={c}
          s={shown}
          frameEvents={fight.frameEvents}
          frameKey={fight.frameKey}
          highlights={highlights}
          hover={hover}
          movePath={hoverMove?.path ?? null}
          onHover={setHover}
          onClick={onTileClick}
          preview={idle ? preview : null}
          intents={idle ? intents : null}
          showIntents={showIntents}
          debug={debugBoard}
        />
        {fight.banner && <div className="banner">{fight.banner}</div>}
        {live.outcome && !fight.busy && (
          <div className="banner" style={{ pointerEvents: 'auto', textTransform: 'none', letterSpacing: 0, display: 'grid', gap: 10, justifyItems: 'center' }}>
            <div style={{ fontSize: 22, letterSpacing: '0.1em', textTransform: 'uppercase' }}>{live.outcome.result === 'win' ? 'Victory' : 'Defeat'}</div>
            <div style={{ fontWeight: 400 }}>{live.outcome.cause}</div>
            <button className="btn primary" onClick={() => props.onFinished(fight.session, save)}>
              See results
            </button>
          </div>
        )}
      </div>
      <div className="panel actions">
        <ActionBar statuses={statuses} selected={mode.id} onSelect={selectAction} disabled={!idle} grappleMode={grappleMode} onGrappleMode={setGrappleMode} />
        <div className="controls">
          <button className="btn" onClick={() => fight.undo() || props.toast('Nothing to undo')} disabled={!idle || !fight.session.canUndo} title="Undo free movement (Z)">
            Undo move <kbd>Z</kbd>
          </button>
          <button className="btn primary" onClick={() => commit({ type: 'endTurn' })} disabled={!idle} title="End the turn (E)">
            End turn <kbd>E</kbd>
          </button>
          {fight.busy && (
            <button className="btn" onClick={fight.skip} title="Skip the animation (Space)">
              Skip <kbd>Space</kbd>
            </button>
          )}
          <span className="spacer" />
          <span className="seg" role="group" aria-label="Animation speed (F)">
            {([1, 2, 0] as Speed[]).map((sp) => (
              <button key={sp} className={fight.speed === sp ? 'on' : ''} onClick={() => fight.setSpeed(sp)}>
                {sp === 0 ? 'Skip' : `${sp}x`}
              </button>
            ))}
          </span>
          <label className="pill" title="Show enemy and minion intents (I)">
            <input type="checkbox" checked={showIntents} onChange={(e) => setShowIntents(e.target.checked)} /> Intents <kbd>I</kbd>
          </label>
        </div>
        <div className="hint">{hint}</div>
      </div>
      </div>
      <div className="side">
        {props.debugOpen && (
          <DebugPanel
            c={c}
            s={live}
            events={fight.session.events}
            intents={intents}
            toggles={toggles}
            setToggles={setToggles}
            stepMode={fight.stepMode}
            setStepMode={fight.setStepMode}
            pending={fight.pending}
            advance={fight.advance}
            apply={(cmd) => {
              const r = fight.apply(cmd);
              if (!r.ok) props.toast(r.error ?? 'Not allowed');
            }}
            holdWave3={settings.maxWaves === 2}
            setHoldWave3={(b) => props.setSettings(b ? { ...settings, maxWaves: 2 } : { ...settings, maxWaves: undefined })}
            restart={props.onRestart}
            onLoadReplay={props.onLoadReplay}
            replayInfo={props.replay ? `${Math.min(cursor, props.replay.commands.length)}/${props.replay.commands.length} commands` : null}
            replayPlaying={replayPlaying}
            setReplayPlaying={setReplayPlaying}
            stepReplay={() => {
              if (!props.replay || fight.busy || cursor >= props.replay.commands.length) return;
              playReplayCommand();
            }}
            close={() => props.setDebugOpen(false)}
          />
        )}
        <IntercomPanel s={shown} onOpenLog={() => setLogOpen(true)} />
        <InfoPanel
          c={c}
          s={shown}
          hoverUnit={preview ? null : hoverUnit}
          hoverObjects={preview ? [] : hoverObjects}
          hoverNote={preview ? null : hoverNote}
          intent={hoverIntent}
          preview={idle ? preview : null}
          previewTitle={previewTitle}
          hint={hint}
        />
        <MinionPanel c={c} s={shown} />
        <CombatLog lines={fight.log} />
        <div className="faint" style={{ fontSize: 12 }}>
          {props.presetId ? `Preset: ${props.presetId}. ` : ''}
          {settings.maxWaves === 2 ? 'Wave 3 held back: clearing wave 2 wins. ' : ''}
          Hover {tileName({ x: 0, y: 0 })}–{tileName({ x: 7, y: 7 })} for previews. <kbd>`</kbd> debug · <kbd>L</kbd> intercom log ·{' '}
          <button className="btn small" onClick={props.onChangeLoadout} style={{ marginLeft: 4 }}>
            Change loadout
          </button>
        </div>
      </div>
      {logOpen && <IntercomLogModal s={shown} onClose={() => setLogOpen(false)} />}
    </div>
  );
}
