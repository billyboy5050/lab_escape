import os from 'node:os';
import { Worker } from 'node:worker_threads';
import { runBatch, type BatchSpec, type FightRow } from './batch';

/**
 * Runs a batch across worker threads. Seeds are split into small chunks handed out as workers free up;
 * rows are reassembled in seed order, so the result is identical to a single-threaded run.
 */
export async function runBatchParallel(spec: BatchSpec, opts: { workers?: number; onProgress?: (done: number, total: number) => void } = {}): Promise<FightRow[]> {
  const workers = Math.max(1, Math.min(opts.workers ?? Math.max(1, os.cpus().length - 1), Math.ceil(spec.seeds.length / 4)));
  if (workers <= 1) {
    const rows = runBatch(spec);
    opts.onProgress?.(rows.length, rows.length);
    return rows;
  }
  const chunkSize = Math.max(1, Math.min(25, Math.ceil(spec.seeds.length / (workers * 4))));
  const chunks: number[][] = [];
  for (let i = 0; i < spec.seeds.length; i += chunkSize) chunks.push(spec.seeds.slice(i, i + chunkSize));
  const results = new Map<number, FightRow[]>();
  let next = 0;
  let done = 0;
  const execArgv = process.execArgv.some((a) => a.includes('tsx')) ? process.execArgv : ['--import', 'tsx'];
  await new Promise<void>((resolve, reject) => {
    let live = 0;
    const pool: Worker[] = [];
    const finish = (err?: unknown) => {
      for (const w of pool) void w.terminate();
      if (err) reject(err);
      else resolve();
    };
    const feed = (w: Worker) => {
      if (next >= chunks.length) {
        live--;
        if (live === 0) finish();
        return;
      }
      const id = next++;
      w.postMessage({ id, spec: { ...spec, seeds: chunks[id] } });
    };
    for (let i = 0; i < workers; i++) {
      const w = new Worker(new URL('./worker.ts', import.meta.url), { execArgv });
      pool.push(w);
      live++;
      w.on('message', (m: { id: number; rows: FightRow[] }) => {
        results.set(m.id, m.rows);
        done += m.rows.length;
        opts.onProgress?.(done, spec.seeds.length);
        feed(w);
      });
      w.on('error', (e) => finish(e));
      feed(w);
    }
  });
  return chunks.flatMap((_, i) => results.get(i)!);
}
