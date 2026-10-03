// Worker thread for parallel batches: receives chunks of a batch spec, sends back fight rows.
import { parentPort } from 'node:worker_threads';
import { contentFor, runBatch, type BatchSpec } from './batch';

const cache = new Map<string, ReturnType<typeof contentFor>>();

parentPort!.on('message', (msg: { id: number; spec: BatchSpec }) => {
  const key = JSON.stringify(msg.spec.overrides ?? {});
  let c = cache.get(key);
  if (!c) {
    c = contentFor(msg.spec.overrides);
    cache.set(key, c);
  }
  parentPort!.postMessage({ id: msg.id, rows: runBatch(msg.spec, c) });
});
