import { useCallback, useEffect, useRef, useState } from 'react';
import { loadoutProblems } from '../engine/step';
import type { FightSettings, Loadout } from '../state/types';
import type { FightRecord } from '../telemetry/record';
import type { FightSession } from '../telemetry/session';
import { useContent } from './contentHot';
import { FightScreen } from './screens/FightScreen';
import { LoadoutScreen } from './screens/LoadoutScreen';
import { ResultScreen } from './screens/ResultScreen';
import { loadPref, savePref } from './telemetry';

type Screen = 'loadout' | 'fight' | 'result';

export function App() {
  const { content: c, error } = useContent();
  const hybrid = c.presets.find((p) => p.id === 'hybrid') ?? c.presets[0]!;
  const [screen, setScreen] = useState<Screen>('loadout');
  const [loadout, setLoadoutState] = useState<Loadout>(() => loadPref('loadout', { abilities: [...hybrid.abilities], upgrades: [...hybrid.upgrades] }));
  const [presetId, setPresetIdState] = useState<string | null>(() => loadPref('preset', hybrid.id));
  const [settings, setSettings] = useState<FightSettings>({});
  const [restartKey, setRestartKey] = useState(0);
  const [replay, setReplay] = useState<FightRecord | null>(null);
  const [debugOpen, setDebugOpen] = useState(false);
  const [result, setResult] = useState<{ session: FightSession; saved: string | null } | null>(null);
  const [toastMsg, setToastMsg] = useState<string | null>(null);
  const toastTimer = useRef<number | null>(null);
  const lastHash = useRef(c.hash);

  const toast = useCallback((msg: string) => {
    setToastMsg(msg);
    if (toastTimer.current !== null) window.clearTimeout(toastTimer.current);
    toastTimer.current = window.setTimeout(() => setToastMsg(null), 3200);
  }, []);
  const setLoadout = (l: Loadout) => {
    setLoadoutState(l);
    savePref('loadout', l);
  };
  const setPresetId = (p: string | null) => {
    setPresetIdState(p);
    savePref('preset', p);
  };

  // Hot reload of content: the fight restarts with the new values (a log never spans two sets of values).
  useEffect(() => {
    if (c.hash === lastHash.current) return;
    lastHash.current = c.hash;
    if (loadoutProblems(c, loadout).length) {
      setScreen('loadout');
      toast('Content reloaded. The loadout is no longer valid; pick again.');
    } else if (screen === 'fight') toast('Content reloaded: the fight restarted with the new values.');
    else toast('Content reloaded.');
    setReplay(null);
  }, [c, loadout, screen, toast]);

  useEffect(() => {
    if (error) toast(`Content error, keeping the old values: ${error.split('\n')[0]}`);
  }, [error, toast]);

  // ?replay=path/to/replay.json loads a saved command log straight into replay playback (bug reports).
  useEffect(() => {
    const path = new URLSearchParams(window.location.search).get('replay');
    if (!path) return;
    void fetch(path.startsWith('/') ? path : `/${path}`)
      .then((r) => (r.ok ? r.json() : Promise.reject(new Error(`${r.status} ${r.statusText}`))))
      .then((rec: FightRecord) => {
        if (rec.contentHash !== c.hash) throw new Error(`recorded with content ${rec.contentHash}, current content is ${c.hash}`);
        setReplay(rec);
        setDebugOpen(true);
        setRestartKey((k) => k + 1);
        setScreen('fight');
      })
      .catch((e: unknown) => toast(`Could not load replay ${path}: ${e instanceof Error ? e.message : String(e)}`));
    // Only on first load.
    // eslint-disable-next-line react-hooks/exhaustive-deps
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

  return (
    <>
      <header className="app-header">
        <div className="brand">
          <h1>Lab Escape · Armory Lockdown</h1>
          <span className="faint">MVP prototype</span>
        </div>
        <div style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
          {debugOpen && <span className="pill mono">content {c.hash}</span>}
          <button className="btn small" onClick={() => setDebugOpen(!debugOpen)} title="Toggle the debug overlay (`)">
            Debug <kbd>`</kbd>
          </button>
        </div>
      </header>
      {screen === 'loadout' && <LoadoutScreen c={c} loadout={loadout} setLoadout={setLoadout} presetId={presetId} setPresetId={setPresetId} settings={settings} setSettings={setSettings} onStart={start} />}
      {screen === 'fight' && (
        <div className="screen">
          <FightScreen
            key={`${restartKey}-${c.hash}`}
            c={c}
            loadout={replay ? replay.loadout : loadout}
            settings={replay ? replay.settings : settings}
            setSettings={(s) => {
              setSettings(s);
              setRestartKey((k) => k + 1);
            }}
            presetId={presetId}
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
            onFinished={(session, saved) => {
              setResult({ session, saved });
              setScreen('result');
            }}
            onChangeLoadout={() => setScreen('loadout')}
            debugOpen={debugOpen}
            setDebugOpen={setDebugOpen}
            toast={toast}
          />
        </div>
      )}
      {screen === 'result' && result && <ResultScreen c={c} session={result.session} saved={result.saved} onRetry={start} onChange={() => setScreen('loadout')} />}
      {toastMsg && <div className="toast" role="status">{toastMsg}</div>}
    </>
  );
}
