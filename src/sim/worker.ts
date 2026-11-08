// Worker thread for parallel batches: receives chunks of a batch spec, sends back fight rows.
import { parentPort } from 'node:worker_threads';
import { contentOfBatch, runBatch, type BatchSpec } from './batch';

const cache = new Map<string, ReturnType<typeof contentOfBatch>>();

parentPort!.on('message', (msg: { id: number; spec: BatchSpec }) => {
  const key = JSON.stringify([msg.spec.overrides ?? {}, msg.spec.rooms ?? null]);
  let c = cache.get(key);
  if (!c) {
    c = contentOfBatch(msg.spec);
    cache.set(key, c);
  }
  parentPort!.postMessage({ id: msg.id, rows: runBatch(msg.spec, c) });
});
