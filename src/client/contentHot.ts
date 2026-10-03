import { useEffect, useState } from 'react';
import { defaultContent, type Content } from '../content';

type Listener = (c: Content | null, error: string | null) => void;

let current: Content = defaultContent();
const listeners = new Set<Listener>();

/**
 * Content with hot reload. Saving any JSON file in content/ reloads the values in the running client,
 * and the fight restarts (a reload never mixes two sets of values in one fight log). Invalid content
 * is reported and the old values are kept.
 */
export function useContent(): { content: Content; error: string | null } {
  const [state, setState] = useState<{ content: Content; error: string | null }>({ content: current, error: null });
  useEffect(() => {
    const l: Listener = (c, error) => setState((s) => ({ content: c ?? s.content, error }));
    listeners.add(l);
    return () => {
      listeners.delete(l);
    };
  }, []);
  return state;
}

if (import.meta.hot) {
  import.meta.hot.accept('../content/index', (mod) => {
    if (!mod) return;
    try {
      const next = (mod as unknown as { defaultContent: () => Content }).defaultContent();
      if (next.hash === current.hash) return;
      current = next;
      for (const l of listeners) l(next, null);
    } catch (e) {
      const msg = e instanceof Error ? e.message : String(e);
      for (const l of listeners) l(null, msg);
    }
  });
}
