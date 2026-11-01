import { useCallback, useEffect, useRef, useState } from 'react';
import { loadoutProblems } from '../engine/step';
import type { FightSettings, Loadout } from '../state/types';
import { checkCompatible, type FightRecord } from '../telemetry/record';
import type { FightSession } from '../telemetry/session';
import { ArtDefs } from './art/defs';
import { Icon } from './art/icons';
import { useContent } from './contentHot';
import { isLoadout, isPresetId, loadoutToRetry, reconcileLoadout } from './loadoutSync';
import { FightScreen } from './screens/FightScreen';
import { LoadoutScreen } from './screens/LoadoutScreen';
import { BriefingScreen } from './screens/BriefingScreen';
import { ResultScreen } from './screens/ResultScreen';
import { TitleScreen } from './screens/TitleScreen';
import { initialBoardView, saveBoardView } from './boardView';
import type { BoardView } from './art/geometry';
import { isMuted, onMutedChange, playSfx, setMuted } from './sfx';
import { loadPref, loadTester, savePref } from './telemetry';
import { cleanTester } from '../telemetry/label';

type Screen = 'title' | 'loadout' | 'briefing' | 'fight' | 'result';

export function App() {
  const { content: c, error } = useContent();
  const hybrid = c.presets.find((p) => p.id === 'hybrid') ?? c.presets[0]!;
  // A replay link goes straight to the fight; anything else opens on the title.
  const [screen, setScreen] = useState<Screen>(() => (new URLSearchParams(window.location.search).get('replay') ? 'loadout' : 'title'));
  const [muted, setMutedState] = useState(isMuted);
  const [boardView, setBoardViewState] = useState<BoardView>(() => initialBoardView());
  const [loadout, setLoadoutState] = useState<Loadout>(() => loadPref('loadout', { abilities: [...hybrid.abilities], upgrades: [...hybrid.upgrades] }, isLoadout));
  const [presetId, setPresetIdState] = useState<string | null>(() => loadPref('preset', hybrid.id, isPresetId));
  const [tester, setTesterState] = useState<string | undefined>(() => loadTester());
  const [settings, setSettings] = useState<FightSettings>({});
  const [restartKey, setRestartKey] = useState(0);
  const [replay, setReplay] = useState<FightRecord | null>(null);
  const [debugOpen, setDebugOpen] = useState(false);
  const [result, setResult] = useState<{ session: FightSession; save: Promise<string | null> | null } | null>(null);
  const [toastMsg, setToastMsg] = useState<string | null>(null);
  const toastTimer = useRef<number | null>(null);
  const lastHash = useRef(c.hash);
  // The content now, for callbacks that outlive the render that created them (a request in flight during a reload).
  const contentRef = useRef(c);
  contentRef.current = c;

  const toast = useCallback((msg: string) => {
    setToastMsg(msg);
    if (toastTimer.current !== null) window.clearTimeout(toastTimer.current);
    toastTimer.current = window.setTimeout(() => setToastMsg(null), 3200);
  }, []);
  const setLoadout = (l: Loadout) => {
    setLoadoutState(l);
    savePref('loadout', l);
  };
  const setTester = (raw: string) => {
    const t = cleanTester(raw);
    setTesterState(t);
    savePref('tester', t ?? '');
  };
  const setPresetId = (p: string | null) => {
    setPresetIdState(p);
    savePref('preset', p);
  };
  const setBoardView = useCallback((v: BoardView) => {
    setBoardViewState(v);
    saveBoardView(v);
  }, []);

  // A content reload gives this render the new content before the effect below has fixed the loadout, and a fight
  // cannot be built from a loadout that no longer fits (newGame throws, and the whole app goes with it). So decide
  // here, from the content in hand, what the fight screen may be built from, and show the loadout screen if nothing.
  const current = reconcileLoadout(c, loadout, presetId);
  const fightLoadout = replay ? replay.loadout : current.loadout;
  const canFight = loadoutProblems(c, fightLoadout).length === 0;
  const view: Screen = (screen === 'fight' || screen === 'briefing') && !canFight ? 'loadout' : screen;

  // Hot reload of content: the fight restarts with the new values (a log never spans two sets of values).
  useEffect(() => {
    if (c.hash === lastHash.current) return;
    lastHash.current = c.hash;
    // A selected preset follows its edited picks, so the fight and its telemetry label match what was played.
    const synced = reconcileLoadout(c, loadout, presetId);
    if (synced.presetId !== presetId) setPresetId(synced.presetId);
    if (synced.loadout !== loadout) setLoadout(synced.loadout);
    if (synced.problems.length) {
      setScreen('loadout');
      toast('Content reloaded. The loadout is no longer valid; pick again.');
    } else if (synced.loadout !== loadout) toast('Content reloaded: the selected preset changed, so its new picks are loaded.');
    else if (screen === 'fight') toast('Content reloaded: the fight restarted with the new values.');
    else toast('Content reloaded.');
    setReplay(null);
  }, [c, loadout, presetId, screen, toast]);

  useEffect(() => {
    if (error) toast(`Content error, keeping the old values: ${error.split('\n')[0]}`);
  }, [error, toast]);

  // ?replay=path/to/replay.json loads a saved command log straight into replay playback (bug reports).
  useEffect(() => {
    const path = new URLSearchParams(window.location.search).get('replay');
    if (!path) return;
    let cancelled = false;
    void fetch(path.startsWith('/') ? path : `/${path}`)
      .then((r) => (r.ok ? r.json() : Promise.reject(new Error(`${r.status} ${r.statusText}`))))
      .then((rec: unknown) => {
        if (cancelled) return;
        // Judged against the content as it is when the response arrives: a hot reload while the request was in
        // flight changes what the replay must match, and the hash the effect started with is out of date.
        checkCompatible(contentRef.current, rec);
        setReplay(rec);
        setDebugOpen(true);
        setRestartKey((k) => k + 1);
        setScreen('fight');
      })
      .catch((e: unknown) => {
        if (!cancelled) toast(`Could not load replay ${path}: ${e instanceof Error ? e.message : String(e)}`);
      });
    return () => {
      cancelled = true;
    };
    // Only on first load.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Sound: M mutes on every screen. Modified keys stay the browser's (Cmd+M minimises a window).
  useEffect(() => onMutedChange(setMutedState), []);
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const el = e.target as HTMLElement | null;
      if (el && (el.tagName === 'INPUT' || el.tagName === 'SELECT' || el.tagName === 'TEXTAREA')) return;
      if ((e.key === 'm' || e.key === 'M') && !e.ctrlKey && !e.metaKey && !e.altKey) setMuted(!isMuted());
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

  // The debug overlay key works on every screen (the fight screen handles it during fights).
  useEffect(() => {
    if (screen === 'fight') return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === '`') setDebugOpen((d) => !d);
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [screen]);

  const start = () => {
    setReplay(null);
    setRestartKey((k) => k + 1);
    setScreen('fight');
  };

  // The result screen shows the session that just ended, which an imported replay can make differ from the picks on
  // the loadout screen: retry that session's loadout and settings, not whatever was selected before the import.
  const retry = (session: FightSession) => {
    const next = loadoutToRetry(c, session.record.loadout, loadout, presetId);
    if (next.loadout !== loadout) {
      setLoadout(next.loadout);
      setPresetId(next.presetId);
    }
    setSettings({ ...session.record.settings });
    start();
  };

  return (
    <>
      <ArtDefs />
      {view === 'title' && (
        <TitleScreen
          c={c}
          onBegin={() => {
            playSfx(['ui']);
            setScreen('loadout');
          }}
        />
      )}
      {view !== 'title' && (
        <header className="app-header">
          <div className="brand">
            <svg viewBox="0 0 24 24" className="logo-mark" aria-hidden="true">
              <path d="M12 2l8.66 5v10L12 22l-8.66-5V7z" fill="none" stroke="currentColor" strokeWidth="1.8" />
              <path d="M8 8v8h4M14 8h3M14 12h2.5M14 16h3M14 8v8" fill="none" stroke="currentColor" strokeWidth="1.6" />
            </svg>
            <h1>Lab Escape</h1>
            <span className="room">// {c.map.name}</span>
          </div>
          <div className="tools">
            {debugOpen && <span className="pill mono">content {c.hash}</span>}
            <button className={`btn small icon-btn ${muted ? '' : 'on'}`} onClick={() => setMuted(!muted)} title={muted ? 'Sound off: turn it on (M)' : 'Sound on: mute (M)'} aria-pressed={!muted} aria-label="Sound">
              <Icon name={muted ? 'sound-off' : 'sound-on'} size={16} /> <kbd>M</kbd>
            </button>
            <button className="btn small" onClick={() => setDebugOpen(!debugOpen)} title="Toggle the debug overlay (`)">
              Debug <kbd>`</kbd>
            </button>
          </div>
        </header>
      )}
      {view === 'loadout' && (
        <LoadoutScreen c={c} loadout={loadout} setLoadout={setLoadout} presetId={presetId} setPresetId={setPresetId} settings={settings} setSettings={setSettings} tester={tester} setTester={setTester} onStart={() => setScreen('briefing')} />
      )}
      {view === 'briefing' && <BriefingScreen c={c} loadout={current.loadout} onDeploy={start} onBack={() => setScreen('loadout')} />}
      {view === 'fight' && (
        <div className="screen">
          <FightScreen
            key={`${restartKey}-${c.hash}-${current.loadout.abilities.join()}|${current.loadout.upgrades.join()}`}
            c={c}
            loadout={fightLoadout}
            settings={replay ? replay.settings : settings}
            setSettings={(s) => {
              setSettings(s);
              setRestartKey((k) => k + 1);
            }}
            presetId={current.presetId}
            tester={tester}
            replay={replay}
            restartKey={restartKey}
            onRestart={() => {
              setReplay(null);
              setRestartKey((k) => k + 1);
            }}
            onLoadReplay={(rec) => {
              setReplay(rec);
              setRestartKey((k) => k + 1);
            }}
            onFinished={(session, save) => {
              setResult({ session, save });
              setScreen('result');
            }}
            onChangeLoadout={() => setScreen('loadout')}
            debugOpen={debugOpen}
            setDebugOpen={setDebugOpen}
            toast={toast}
            view={boardView}
            setView={setBoardView}
          />
        </div>
      )}
      {view === 'result' && result && <ResultScreen c={c} session={result.session} save={result.save} onRetry={() => retry(result.session)} onChange={() => setScreen('loadout')} />}
      {toastMsg && <div className="toast" role="status">{toastMsg}</div>}
    </>
  );
}
