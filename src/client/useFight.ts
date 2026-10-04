import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { Content } from '../content/types';
import type { StepResult } from '../engine/step';
import type { Command, FightSettings, Frame, GameEvent, GameState, Loadout } from '../state/types';
import { FightSession } from '../telemetry/session';
import { EventFormatter, type LogLine } from './format';

export type Speed = 1 | 2 | 0;

export interface FightView {
  session: FightSession;
  /** The state on screen: the current animation frame, or the live state when idle. */
  shown: GameState;
  frameEvents: GameEvent[];
  frameKey: number;
  banner: string | null;
  busy: boolean;
  log: LogLine[];
  speed: Speed;
  setSpeed: (s: Speed) => void;
  stepMode: boolean;
  setStepMode: (b: boolean) => void;
  /** Step-through: show the next frame, or every frame up to the next phase. */
  advance: (untilPhase?: boolean) => void;
  apply: (cmd: Command) => StepResult;
  undo: () => boolean;
  skip: () => void;
  pending: number;
  /** Bumps whenever the live state changes. */
  version: number;
}

const PHASE_LABEL: Record<string, string> = { minion: 'Minion phase', enemy: 'Enemy phase', environment: 'Environment', player: 'Your turn' };

export function useFight(c: Content, loadout: Loadout, settings: FightSettings, opts: { debugLog: boolean; restartKey?: number; meta?: Record<string, unknown> }): FightView {
  const session = useMemo(
    () => new FightSession(c, loadout, settings, { label: 'client', ...(opts.meta ?? {}) }, () => performance.timeOrigin + performance.now()),
    // A new session whenever content, loadout or settings change (hot reload restarts the fight).
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [c, JSON.stringify(loadout), JSON.stringify(settings), opts.restartKey],
  );
  const [shown, setShown] = useState<GameState>(session.state);
  const [frameEvents, setFrameEvents] = useState<GameEvent[]>([]);
  const [frameKey, setFrameKey] = useState(0);
  const [banner, setBanner] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [log, setLog] = useState<LogLine[]>([]);
  const [speed, setSpeedState] = useState<Speed>(1);
  const [stepMode, setStepModeState] = useState(false);
  const [version, setVersion] = useState(0);
  const [pending, setPending] = useState(0);
  const queue = useRef<Frame[]>([]);
  const timer = useRef<number | null>(null);
  const speedRef = useRef<Speed>(1);
  const stepRef = useRef(false);
  const fmt = useRef<EventFormatter | null>(null);
  const debugRef = useRef(opts.debugLog);
  debugRef.current = opts.debugLog;

  const appendLog = useCallback((events: GameEvent[], s: GameState) => {
    const f = fmt.current!;
    const lines = events.map((e) => f.format(e, s, debugRef.current)).filter((l): l is LogLine => !!l);
    if (lines.length) setLog((old) => [...old, ...lines].slice(-600));
  }, []);

  const finish = useCallback(() => {
    if (timer.current !== null) window.clearTimeout(timer.current);
    timer.current = null;
    // The animation is over and the player can act: this is when a new turn's clock starts.
    session.turnReady();
    setShown(session.state);
    setBusy(false);
    setBanner(null);
    setPending(0);
  }, [session]);

  const showFrame = useCallback(
    (f: Frame) => {
      setShown(f.state);
      setFrameEvents(f.events);
      setFrameKey((k) => k + 1);
      // A frame can hold several phase starts (an empty minion phase, then the enemy phase): show the last.
      const ph = [...f.events].reverse().find((e): e is Extract<GameEvent, { t: 'PhaseStarted' }> => e.t === 'PhaseStarted' && e.phase !== 'roundStart');
      if (ph) setBanner(PHASE_LABEL[ph.phase] ?? null);
      appendLog(f.events, f.state);
    },
    [appendLog],
  );

  const frameDelay = (f: Frame): number => {
    const base = c.rules.ui.secondsPerActor * 1000;
    const visual = f.events.some((e) => e.t === 'UnitMoved' || e.t === 'DamageDealt' || e.t === 'AreaEffect' || e.t === 'ProjectileFired' || e.t === 'UnitSpawned' || e.t === 'HazardFired');
    const d = visual ? base : f.events.some((e) => e.t === 'PhaseStarted') ? base * 0.8 : base * 0.25;
    return d / (speedRef.current === 2 ? 2 : 1);
  };

  const pump = useCallback(() => {
    timer.current = null;
    const f = queue.current.shift();
    setPending(queue.current.length);
    if (!f) return finish();
    showFrame(f);
    if (stepRef.current) return;
    timer.current = window.setTimeout(pump, frameDelay(f));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [finish, showFrame]);

  const flush = useCallback(() => {
    if (timer.current !== null) window.clearTimeout(timer.current);
    timer.current = null;
    const rest = queue.current;
    queue.current = [];
    for (const f of rest) appendLog(f.events, f.state);
    setFrameEvents(rest.length ? rest[rest.length - 1]!.events : []);
    setFrameKey((k) => k + 1);
    finish();
  }, [appendLog, finish]);

  const play = useCallback(
    (frames: Frame[]) => {
      if (!frames.length) return finish();
      if (speedRef.current === 0) {
        queue.current.push(...frames);
        return flush();
      }
      queue.current.push(...frames);
      setPending(queue.current.length);
      setBusy(true);
      if (timer.current === null && !stepRef.current) pump();
      else if (stepRef.current && timer.current === null && queue.current.length === frames.length) pump();
    },
    [finish, flush, pump],
  );

  // A new session: reset everything and play the opening (wave 1 arrives, the briefing line).
  useEffect(() => {
    fmt.current = new EventFormatter(c);
    queue.current = [];
    if (timer.current !== null) window.clearTimeout(timer.current);
    timer.current = null;
    setLog([]);
    setShown(session.state);
    setFrameEvents([]);
    setBanner(null);
    setBusy(false);
    setVersion((v) => v + 1);
    play(session.startFrames);
    return () => {
      if (timer.current !== null) window.clearTimeout(timer.current);
      timer.current = null;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [session]);

  const apply = useCallback(
    (cmd: Command): StepResult => {
      const r = session.apply(cmd);
      if (r.ok) {
        setVersion((v) => v + 1);
        play(r.frames);
      }
      return r;
    },
    [session, play],
  );

  const undo = useCallback((): boolean => {
    if (busy || !session.undo()) return false;
    setLog((old) => [...old, { id: Date.now(), text: 'Move undone', kind: 'phase' }]);
    setShown(session.state);
    setFrameEvents([]);
    setVersion((v) => v + 1);
    return true;
  }, [busy, session]);

  const setSpeed = useCallback(
    (s: Speed) => {
      speedRef.current = s;
      setSpeedState(s);
      if (s === 0 && queue.current.length) flush();
    },
    [flush],
  );

  const setStepMode = useCallback(
    (b: boolean) => {
      stepRef.current = b;
      setStepModeState(b);
      if (!b && queue.current.length && timer.current === null) pump();
    },
    [pump],
  );

  const advance = useCallback(
    (untilPhase = false) => {
      if (!queue.current.length) return finish();
      if (!untilPhase) return pump();
      // Show frames until the next phase banner (or the end).
      let f = queue.current.shift();
      while (f) {
        showFrame(f);
        const next = queue.current[0];
        if (!next || next.events.some((e) => e.t === 'PhaseStarted')) break;
        f = queue.current.shift();
      }
      setPending(queue.current.length);
      if (!queue.current.length) finish();
    },
    [finish, pump, showFrame],
  );

  return {
    session,
    shown,
    frameEvents,
    frameKey,
    banner,
    busy,
    log,
    speed,
    setSpeed,
    stepMode,
    setStepMode,
    advance,
    apply,
    undo,
    skip: flush,
    pending,
    version,
  };
}
