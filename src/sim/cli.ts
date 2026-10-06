// Headless simulator CLI. Run with: npm run sim -- <command> [options]
import fs from 'node:fs';
import path from 'node:path';
import { parseArgs } from 'node:util';
import { defaultContent } from '../content';
import type { GameEvent } from '../state/types';
import { checkCompatible, replay } from '../telemetry/record';
import { summarize } from '../telemetry/summary';
import { contentFor, seedRange, type BatchSpec, type BotKind, type FightRow } from './batch';
import { recordGolden, verifyGolden, eventsToJsonl, type GoldenFile } from './golden';
import { parseInteger } from './options';
import { DEFAULT_PLAYTEST, formatPlaytest, loadTelemetry } from './playtest';
import { runBatchParallel } from './parallel';
import { aggregate, formatReport, formatTuneRow, liftToCsv, rowsToCsv } from './report';

const HELP = `Lab Escape headless simulator

Usage: npm run sim -- <command> [options]

Commands
  batch     One preset (or loadout) for many seeds, with a report
  presets   Every preset, compared; saves the first winning line of each (the paper check)
  sweep     Random valid loadouts, with the win-rate lift of every ability and upgrade
  tune      One tuning value across a list, e.g. --param units.warden.hp --values 12,16,20
  golden    Verify the golden replays, or --record them
  replay    Replay a saved command log (FILE) and print its summary
  telemetry Playtest metrics from the fights saved under telemetry/ (or DIR), against the spec's targets and alarms

Options
  --bot random|greedy|greedy-naive   Bot to play (default greedy; greedy-naive ignores hazard cues)
  --preset tech|alien|hybrid         Preset for batch and tune (default hybrid)
  --seeds N        Number of seeds (default 1000)
  --from N         First seed (default 1)
  --workers N      Worker threads (default: CPU count minus 1)
  --size N         Abilities per random loadout in a sweep (default: the loadout size in rules.json)
  --set PATH=VAL   Content override, repeatable, e.g. --set units.warden.hp=16
  --hold-wave3     Hold back wave 3; clearing wave 2 wins (the first playtest's setup)
  --csv FILE       Write one row per fight
  --out DIR        Write the report, CSV and winning replays to DIR (default reports/ for presets)
  --record         With golden: re-record the golden replays
  --informed-from N  With telemetry: the first attempt number counted as informed (default 2)
  --include-unlabelled  With telemetry: also count fights saved with no tester ID (by default they are skipped)
  --informed-attempts N  With telemetry: how many attempts per tester the informed round has, from --informed-from (default 3)
  --informed-preset ID  With telemetry: the preset the informed round plays (default hybrid)
`;

function main(): Promise<void> | void {
  const { values, positionals } = parseArgs({
    allowPositionals: true,
    options: {
      bot: { type: 'string', default: 'greedy' },
      preset: { type: 'string', default: 'hybrid' },
      seeds: { type: 'string' },
      from: { type: 'string', default: '1' },
      workers: { type: 'string' },
      size: { type: 'string' },
      set: { type: 'string', multiple: true },
      'hold-wave3': { type: 'boolean', default: false },
      csv: { type: 'string' },
      out: { type: 'string' },
      record: { type: 'boolean', default: false },
      'informed-from': { type: 'string' },
      'include-unlabelled': { type: 'boolean', default: false },
      'informed-attempts': { type: 'string' },
      'informed-preset': { type: 'string' },
      param: { type: 'string' },
      values: { type: 'string' },
      help: { type: 'boolean', short: 'h', default: false },
    },
  });
  const cmd = positionals[0];
  if (!cmd || values.help) {
    console.log(HELP);
    return;
  }
  const bot = values.bot as BotKind;
  if (!['random', 'greedy', 'greedy-naive'].includes(bot)) throw new Error(`Unknown bot "${bot}"`);
  const overrides = parseOverrides(values.set ?? []);
  const seeds = seedRange(parseInteger('from', values.from)!, parseInteger('seeds', values.seeds, { min: 1 }) ?? 1000);
  const settings = values['hold-wave3'] ? { maxWaves: 2 } : {};
  const workers = parseInteger('workers', values.workers, { min: 1 });
  const outDir = values.out;
  const progress = (label: string) => (done: number, total: number) => {
    if (process.stderr.isTTY) process.stderr.write(`\r${label}: ${done}/${total}   `);
    if (done === total && process.stderr.isTTY) process.stderr.write('\n');
  };

  switch (cmd) {
    case 'batch':
      return (async () => {
        const spec: BatchSpec = { bot, loadout: { preset: values.preset! }, seeds, overrides, settings };
        const c = contentFor(overrides);
        const t = Date.now();
        const rows = await runBatchParallel(spec, { workers, onProgress: progress(`${values.preset} x ${bot}`) });
        const stats = aggregate(rows, c);
        const title = `Batch: ${values.preset} preset, ${bot} bot, seeds ${seeds[0]} to ${seeds[seeds.length - 1]}${describeOverrides(overrides)}`;
        const text = formatReport(title, stats) + `\n\n(${((Date.now() - t) / 1000).toFixed(1)} s, content ${c.hash})`;
        console.log(text);
        reportErrors(rows);
        writeOutputs({ outDir, csv: values.csv, name: `batch-${values.preset}-${bot}`, rows, text, stats });
      })();
    case 'presets':
      return (async () => {
        const c = contentFor(overrides);
        const dir = outDir ?? 'reports';
        const lines: string[] = [];
        for (const p of c.presets) {
          const rows = await runBatchParallel({ bot, loadout: { preset: p.id }, seeds, overrides, settings, keepRecords: true }, { workers, onProgress: progress(`${p.id} x ${bot}`) });
          const stats = aggregate(rows, c);
          const text = formatReport(`Preset ${p.name}: ${bot} bot, ${seeds.length} seeds${describeOverrides(overrides)}`, stats);
          console.log(text);
          reportErrors(rows);
          const win = rows.find((r) => r.outcome === 'win' && r.record);
          if (win) {
            fs.mkdirSync(dir, { recursive: true });
            const file = path.join(dir, `winning-line-${p.id}.json`);
            fs.writeFileSync(file, JSON.stringify(win.record, null, 2));
            lines.push(`${p.name}: winning line found (seed ${win.seed}, ${win.rounds} rounds) -> ${file}`);
          } else lines.push(`${p.name}: no winning line in ${seeds.length} seeds`);
          writeOutputs({ outDir: dir, csv: undefined, name: `preset-${p.id}-${bot}`, rows, text, stats });
        }
        console.log(`\nPaper check (does each preset have a winning line?)\n  ${lines.join('\n  ')}`);
      })();
    case 'sweep':
      return (async () => {
        const c = contentFor(overrides);
        const size = parseInteger('size', values.size, { min: 1 }) ?? c.rules.loadout.abilities;
        const rows = await runBatchParallel({ bot, loadout: { sweep: true, size }, seeds, overrides, settings }, { workers, onProgress: progress(`sweep ${size} of ${c.abilityOrder.length}`) });
        const stats = aggregate(rows, c);
        const text = formatReport(`Loadout sweep: ${size} of ${c.abilityOrder.length} abilities, ${bot} bot, ${seeds.length} random loadouts${describeOverrides(overrides)}`, stats);
        console.log(text);
        reportErrors(rows);
        writeOutputs({ outDir, csv: values.csv, name: `sweep-${size}-${bot}`, rows, text, stats, extra: { 'lift.csv': liftToCsv(stats.lift) } });
      })();
    case 'tune':
      return (async () => {
        if (!values.param || !values.values) throw new Error('tune needs --param PATH and --values A,B,C');
        const out: string[] = [`Tuning ${values.param} on the ${values.preset} preset (${bot} bot, ${seeds.length} seeds each)`];
        out.push(`${'value'.padEnd(10)} ${'win rate'.padEnd(26)} rounds(win)  hazard fires  own-damage share`);
        for (const raw of values.values.split(',')) {
          const v = parseValue(raw);
          const o = { ...overrides, [values.param]: v };
          const c = contentFor(o);
          const rows = await runBatchParallel({ bot, loadout: { preset: values.preset! }, seeds, overrides: o, settings }, { workers, onProgress: progress(`${values.param}=${raw}`) });
          const s = aggregate(rows, c);
          // A bad value can break only some seeds; aggregate leaves those out, so the row has to say they happened.
          out.push(...formatTuneRow(String(raw), s, rows.find((r) => r.outcome === 'error')?.error));
        }
        console.log(out.join('\n'));
      })();
    case 'golden': {
      const c = defaultContent();
      const dir = 'golden';
      if (values.record) {
        fs.mkdirSync(dir, { recursive: true });
        for (const p of c.presets) {
          const { golden, events } = recordGolden(c, p.id);
          fs.writeFileSync(path.join(dir, `${p.id}.json`), JSON.stringify(golden, null, 2) + '\n');
          fs.writeFileSync(path.join(dir, `${p.id}.events.jsonl`), eventsToJsonl(events));
          console.log(`Recorded golden ${p.id}: ${golden.outcome} in ${golden.rounds} rounds, ${golden.eventCount} events, hash ${golden.eventsHash}`);
        }
        return;
      }
      let failed = 0;
      for (const p of c.presets) {
        const file = path.join(dir, `${p.id}.json`);
        if (!fs.existsSync(file)) {
          console.log(`${p.id}: missing (${file}); run npm run golden:record`);
          failed++;
          continue;
        }
        const g = JSON.parse(fs.readFileSync(file, 'utf8')) as GoldenFile;
        const evFile = path.join(dir, `${p.id}.events.jsonl`);
        const expected = fs.existsSync(evFile) ? fs.readFileSync(evFile, 'utf8').trim().split('\n').map((l) => JSON.parse(l) as GameEvent) : undefined;
        const r = verifyGolden(c, g, expected);
        console.log(`${p.id}: ${r.ok ? 'ok' : `FAILED - ${r.problem}`}`);
        if (!r.ok) failed++;
      }
      if (failed) process.exitCode = 1;
      return;
    }
    case 'replay': {
      const file = positionals[1];
      if (!file) throw new Error('replay needs a FILE');
      const rec: unknown = JSON.parse(fs.readFileSync(file, 'utf8'));
      const c = defaultContent();
      checkCompatible(c, rec);
      const r = replay(c, rec);
      const s = summarize(r.events, r.state);
      console.log(JSON.stringify({ outcome: s.outcome, cause: s.cause, rounds: s.rounds, loadout: s.loadout, playerDamageTaken: s.playerDamageTaken, kills: s.kills, peakMinions: s.peakMinions, hazardFires: s.hazardFires.length }, null, 2));
      return;
    }
    case 'telemetry': {
      const dir = positionals[1] ?? 'telemetry';
      const opts = { informedFrom: parseInteger('informed-from', values['informed-from'], { min: 1 }) ?? DEFAULT_PLAYTEST.informedFrom, informedAttempts: parseInteger('informed-attempts', values['informed-attempts'], { min: 1 }) ?? DEFAULT_PLAYTEST.informedAttempts, informedPreset: values['informed-preset'] ?? DEFAULT_PLAYTEST.informedPreset };
      console.log(formatPlaytest(dir, loadTelemetry(defaultContent(), dir, { includeUnlabelled: values['include-unlabelled'] }), opts));
      return;
    }
    default:
      console.log(HELP);
      throw new Error(`Unknown command "${cmd}"`);
  }
}

function parseValue(raw: string): unknown {
  try {
    return JSON.parse(raw);
  } catch {
    return raw;
  }
}

function parseOverrides(sets: string[]): Record<string, unknown> {
  const out: Record<string, unknown> = {};
  for (const s of sets) {
    const i = s.indexOf('=');
    if (i < 0) throw new Error(`--set needs PATH=VALUE, got "${s}"`);
    out[s.slice(0, i)] = parseValue(s.slice(i + 1));
  }
  return out;
}

function describeOverrides(o: Record<string, unknown>): string {
  const keys = Object.keys(o);
  return keys.length ? ` [${keys.map((k) => `${k}=${JSON.stringify(o[k])}`).join(', ')}]` : '';
}

function reportErrors(rows: FightRow[]): void {
  const errs = rows.filter((r) => r.outcome === 'error');
  if (!errs.length) return;
  console.log(`\n${errs.length} fights raised errors. First: seed ${errs[0]!.seed}: ${errs[0]!.error}`);
  fs.mkdirSync('reports', { recursive: true });
  const file = path.join('reports', `error-seed-${errs[0]!.seed}.json`);
  if (errs[0]!.record) {
    fs.writeFileSync(file, JSON.stringify(errs[0]!.record, null, 2));
    console.log(`Command log for the first error: ${file}`);
  }
}

function writeOutputs(o: { outDir?: string; csv?: string; name: string; rows: FightRow[]; text: string; stats: unknown; extra?: Record<string, string> }): void {
  if (o.csv) {
    fs.mkdirSync(path.dirname(path.resolve(o.csv)), { recursive: true });
    fs.writeFileSync(o.csv, rowsToCsv(o.rows));
    console.log(`Wrote ${o.csv}`);
  }
  if (!o.outDir) return;
  fs.mkdirSync(o.outDir, { recursive: true });
  const base = path.join(o.outDir, o.name);
  fs.writeFileSync(`${base}.txt`, o.text + '\n');
  fs.writeFileSync(`${base}.csv`, rowsToCsv(o.rows));
  fs.writeFileSync(`${base}.stats.json`, JSON.stringify(o.stats, null, 2));
  for (const [suffix, body] of Object.entries(o.extra ?? {})) fs.writeFileSync(`${base}.${suffix}`, body);
  console.log(`Wrote ${base}.txt, .csv and .stats.json`);
}

// main() runs inside the chain so that an error it throws before it returns a promise (a bad option, a bad file) is
// printed as a message by the handler below, like the errors from the async commands, instead of as a stack trace.
Promise.resolve()
  .then(main)
  .catch((e) => {
    console.error(e instanceof Error ? e.message : e);
    process.exitCode = 1;
  });
