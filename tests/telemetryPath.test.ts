import path from 'node:path';
import { describe, expect, it } from 'vitest';
import { telemetryDir, telemetryFile } from '../src/dev/telemetryPath';

const root = path.resolve('/repo/telemetry');

describe('telemetryDir', () => {
  it('puts a normal fight folder under the root', () => {
    expect(telemetryDir(root, '2026-10-03T08-31-52-716Z-hybrid-lose')).toBe(path.join(root, '2026-10-03T08-31-52-716Z-hybrid-lose'));
  });

  it.each(['..', '.', '...', ''])('refuses %j, which would resolve to the root or above it', (name) => {
    expect(telemetryDir(root, name)).toBeNull();
  });

  it.each([undefined, null, 5, {}, ['..']])('refuses a name that is not a string (%j)', (name) => {
    expect(telemetryDir(root, name)).toBeNull();
  });

  it.each(['../x', '../../etc', '/etc/passwd', 'a/b', 'a\\b', '..\\..'])('keeps %j inside the root by turning separators into underscores', (name) => {
    const dir = telemetryDir(root, name);
    expect(dir).not.toBeNull();
    expect(path.dirname(dir!)).toBe(root);
  });
});

describe('telemetryFile', () => {
  const dir = path.join(root, 'fight');

  it('allows the files the client sends', () => {
    for (const f of ['replay.json', 'summary.json', 'events.jsonl']) expect(telemetryFile(dir, f)).toBe(path.join(dir, f));
  });

  it.each(['..', '.', '', '...'])('refuses %j', (file) => {
    expect(telemetryFile(dir, file)).toBeNull();
  });

  it('refuses a name that is not a string', () => {
    expect(telemetryFile(dir, 7)).toBeNull();
  });

  it.each(['../package.json', '../../package.json', '/etc/hosts', 'a/../../b'])('cannot leave the folder with %j', (file) => {
    const target = telemetryFile(dir, file);
    expect(target).not.toBeNull();
    expect(path.dirname(target!)).toBe(dir);
  });
});
