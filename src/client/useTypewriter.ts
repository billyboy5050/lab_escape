import { useEffect, useState } from 'react';

export function prefersReducedMotion(): boolean {
  try {
    return window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  } catch {
    return false;
  }
}

/**
 * Reveals text a character at a time, as the lab's terminals print it. A new text starts over; with reduced motion
 * the whole text shows at once. `lines` reveals several lines in turn, each after the one before it has finished.
 */
export function useTypewriter(lines: string[], cps = 55, startDelayMs = 0): { shown: string[]; done: boolean } {
  const total = lines.reduce((n, l) => n + l.length, 0);
  const keyText = lines.join('\n');
  const [count, setCount] = useState(() => (prefersReducedMotion() ? total : 0));
  useEffect(() => {
    if (prefersReducedMotion()) {
      setCount(total);
      return;
    }
    setCount(0);
    let timer: number | null = null;
    const start = window.setTimeout(() => {
      timer = window.setInterval(() => {
        setCount((n) => {
          if (n >= total) {
            if (timer !== null) window.clearInterval(timer);
            return n;
          }
          return n + 1;
        });
      }, 1000 / cps);
    }, startDelayMs);
    return () => {
      window.clearTimeout(start);
      if (timer !== null) window.clearInterval(timer);
    };
  }, [keyText, total, cps, startDelayMs]);
  let left = count;
  const shown = lines.map((l) => {
    const take = Math.max(0, Math.min(l.length, left));
    left -= take;
    return l.slice(0, take);
  });
  return { shown, done: count >= total };
}
