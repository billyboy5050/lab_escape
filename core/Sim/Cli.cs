#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using LabEscape.Core;
using Newtonsoft.Json.Linq;

namespace LabEscape.Sim
{
    /// <summary>Headless simulator command line.</summary>
    public static class Cli
    {
        private const string Help = @"Lab Escape headless simulator (C#)

Usage: dotnet run --project core/Sim -c Release -- <command> [options]

Commands
  batch       One preset (or loadout) for many seeds, with a report
  presets     Every preset, compared; saves the first winning line of each (the paper check)
  sweep       Random valid loadouts, with the win-rate lift of every ability and upgrade
  tune        One tuning value across a list, e.g. --param units.warden.hp --values 12,16,20
  golden      Verify the golden replays, or --record them
  replay      Replay a saved command log (FILE) and print its summary
  telemetry   Playtest metrics from the fights saved under telemetry/ (or DIR), against the spec's targets and alarms
  conformance Regenerate every committed report and compare each with its file, byte for byte

Options
  --bot random|greedy|greedy-naive   Bot to play (default greedy; greedy-naive ignores hazard cues)
  --preset tech|alien|hybrid         Preset for batch and tune (default hybrid)
  --seeds N        Number of seeds (default 1000)
  --from N         First seed (default 1)
  --workers N      Worker threads (default: CPU count minus 1)
  --size N         Abilities per random loadout in a sweep (default: the loadout size in rules.json)
  --set PATH=VAL   Content override, repeatable, e.g. --set units.warden.hp=16
  --hold-wave3     Hold back wave 3; clearing wave 2 wins (the first playtest's setup)
  --rooms FILE     Add the rooms in a JSON file to the content, e.g. golden/rooms.json
  --room ID        Fight in this room (default: the Armory)
  --csv FILE       Write one row per fight
  --out DIR        Write the report, CSV and winning replays to DIR (default reports/ for presets)
  --record         With golden: re-record the golden replays
  --informed-from N  With telemetry: the first attempt number counted as informed (default 2)
  --include-unlabelled  With telemetry: also count fights saved with no tester ID (by default they are skipped)
  --exclude-tester ID   With telemetry: leave a tester out (repeatable), such as the developer who played round 1
  --informed-attempts N  With telemetry: how many attempts per tester the informed round has, from --informed-from (default 3)
  --informed-preset ID  With telemetry: the preset the informed round plays (default hybrid)
";

        private const int ConformanceSeeds = 1000;

        public static int Run(string[] args)
        {
            try
            {
                return Execute(args);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message);
                return 1;
            }
        }

        /// <summary>The repository's top folder, found from where the tool was started or where it was built.</summary>
        private static string FindRoot()
        {
            foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "content", "rules.json"))) return dir.FullName;
                }
            }
            throw new DirectoryNotFoundException("Could not find content/rules.json above this folder");
        }

        private static int Execute(string[] args)
        {
            var a = ParsedArgs.Parse(args);
            var cmd = a.Positionals.FirstOrDefault();
            if (string.IsNullOrEmpty(cmd) || a.Flag("help"))
            {
                Console.WriteLine(Help);
                return 0;
            }
            // Paths are relative to the repository, wherever the tool is started.
            Directory.SetCurrentDirectory(FindRoot());
            var bot = a.Value("bot") ?? Batch.GreedyKind;
            if (!Batch.BotKinds.Contains(bot)) throw new ArgumentException($"Unknown bot \"{bot}\"");
            var files = ContentLoader.FilesFromDirectory("content");
            var roomsFile = a.Value("rooms");
            if (roomsFile != null) files.Root["rooms"] = StrictJson.Parse(File.ReadAllText(roomsFile));
            var overrides = ParseOverrides(a.List("set"));
            var room = a.Value("room");
            // A wrong room ID would otherwise surface once per fight, as a report full of errors.
            if (room != null) Batch.ContentFor(files, overrides).RoomFor(room);
            var opts = new RunOptions
            {
                Bot = bot,
                Preset = a.Value("preset") ?? "hybrid",
                Overrides = overrides,
                Seeds = Batch.SeedRange(Options.ParseInteger("from", a.Value("from") ?? "1")!.Value, SeedCount(a.Value("seeds"))),
                Settings = new FightSettings { MaxWaves = a.Flag("hold-wave3") ? 2 : (int?)null, Room = room },
                Workers = ClampToInt(Options.ParseInteger("workers", a.Value("workers"), 1)),
                Out = a.Value("out"),
                Csv = a.Value("csv"),
                Files = files,
            };
            switch (cmd)
            {
                case "batch": RunBatchCommand(opts); return 0;
                case "presets": RunPresets(opts, opts.Out ?? "reports", Console.Out); return 0;
                case "sweep":
                    opts.Size = ClampToInt(Options.ParseInteger("size", a.Value("size"), 1));
                    RunSweep(opts, opts.Out, Console.Out);
                    return 0;
                case "tune": RunTune(opts, a.Value("param"), a.Value("values")); return 0;
                case "golden": return RunGolden(opts, a.Flag("record"));
                case "replay": RunReplay(opts, a.Positionals.ElementAtOrDefault(1)); return 0;
                case "telemetry": RunTelemetry(opts, a); return 0;
                case "conformance": return RunConformance(opts);
                default:
                    Console.WriteLine(Help);
                    throw new ArgumentException($"Unknown command \"{cmd}\"");
            }
        }

        private sealed class RunOptions
        {
            public string Bot = "";
            public string Preset = "";
            public OrderedMap<string, JToken> Overrides = null!;
            public List<long> Seeds = null!;
            public FightSettings Settings = null!;
            public int? Workers;
            public string? Out;
            public string? Csv;
            public int? Size;
            public ContentFiles Files = null!;

            /// <summary>Text for a report's title when the fights are not in the Armory.</summary>
            public string InRoom => Settings.Room == null ? "" : $" in room {Settings.Room}";
        }

        private static int SeedCount(string? raw)
        {
            var n = Options.ParseInteger("seeds", raw, 1) ?? 1000;
            if (n > int.MaxValue) throw new ArgumentException($"--seeds must be at most {int.MaxValue} (got \"{raw}\")");
            return (int)n;
        }

        private static int? ClampToInt(long? n) => n.HasValue ? (int)Math.Min(n.Value, int.MaxValue) : (int?)null;

        private static JToken ParseValue(string raw) => StrictJson.TryParse(raw, out var token) ? token : new JValue(raw);

        private static OrderedMap<string, JToken> ParseOverrides(IEnumerable<string> sets)
        {
            var output = new OrderedMap<string, JToken>(StringComparer.Ordinal);
            foreach (var s in sets)
            {
                var i = s.IndexOf('=');
                if (i < 0) throw new ArgumentException($"--set needs PATH=VALUE, got \"{s}\"");
                output[s.Substring(0, i)] = ParseValue(s.Substring(i + 1));
            }
            return output;
        }

        private static string DescribeOverrides(OrderedMap<string, JToken> overrides)
        {
            return overrides.Count > 0 ? $" [{string.Join(", ", overrides.Select(p => $"{p.Key}={Hash.Stringify(p.Value)}"))}]" : "";
        }

        private static Action<int, int>? Progress(string label)
        {
            if (Console.IsErrorRedirected) return null;
            return (done, total) =>
            {
                Console.Error.Write($"\r{label}: {done}/{total}   ");
                if (done == total) Console.Error.WriteLine();
            };
        }

        private static void ReportErrors(List<FightRow> rows, TextWriter log)
        {
            var errs = rows.Where(r => r.Outcome == FightOutcome.Error).ToList();
            if (errs.Count == 0) return;
            log.WriteLine($"\n{errs.Count} fights raised errors. First: seed {errs[0].Seed}: {errs[0].Error}");
            Directory.CreateDirectory("reports");
            var file = Path.Combine("reports", $"error-seed-{errs[0].Seed}.json");
            if (errs[0].Record == null) return;
            File.WriteAllText(file, errs[0].Record!.ToJson());
            log.WriteLine($"Command log for the first error: {file}");
        }

        private static void WriteOutputs(string? outDir, string? csv, string name, List<FightRow> rows, string text, BatchStats stats, TextWriter log, IEnumerable<KeyValuePair<string, string>>? extra = null)
        {
            if (csv != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(csv))!);
                File.WriteAllText(csv, Report.RowsToCsv(rows));
                log.WriteLine($"Wrote {csv}");
            }
            if (outDir == null) return;
            Directory.CreateDirectory(outDir);
            var path = Path.Combine(outDir, name);
            File.WriteAllText($"{path}.txt", text + "\n");
            File.WriteAllText($"{path}.csv", Report.RowsToCsv(rows));
            File.WriteAllText($"{path}.stats.json", Hash.Stringify(stats.ToToken(), 2));
            foreach (var pair in extra ?? new KeyValuePair<string, string>[0]) File.WriteAllText($"{path}.{pair.Key}", pair.Value);
            log.WriteLine($"Wrote {path}.txt, .csv and .stats.json");
        }

        private static void RunBatchCommand(RunOptions o)
        {
            var c = Batch.ContentFor(o.Files, o.Overrides);
            var clock = Stopwatch.StartNew();
            var spec = new BatchSpec { Bot = o.Bot, Loadout = LoadoutChoice.OfPreset(o.Preset), Seeds = o.Seeds, Settings = o.Settings };
            var rows = Batch.RunBatchParallel(spec, c, o.Workers, Progress($"{o.Preset} x {o.Bot}"));
            var stats = Report.Aggregate(rows, c);
            var title = $"Batch: {o.Preset} preset, {o.Bot} bot, seeds {o.Seeds[0]} to {o.Seeds[o.Seeds.Count - 1]}{o.InRoom}{DescribeOverrides(o.Overrides)}";
            var text = Report.FormatReport(title, stats) + $"\n\n({JsText.ToFixed(clock.Elapsed.TotalSeconds, 1)} s, content {c.Hash})";
            Console.WriteLine(text);
            ReportErrors(rows, Console.Out);
            WriteOutputs(o.Out, o.Csv, $"batch-{o.Preset}-{o.Bot}", rows, text, stats, Console.Out);
        }

        private static void RunPresets(RunOptions o, string dir, TextWriter log)
        {
            var c = Batch.ContentFor(o.Files, o.Overrides);
            var lines = new List<string>();
            foreach (var p in c.Presets)
            {
                var spec = new BatchSpec { Bot = o.Bot, Loadout = LoadoutChoice.OfPreset(p.Id), Seeds = o.Seeds, Settings = o.Settings, KeepRecords = true };
                var rows = Batch.RunBatchParallel(spec, c, o.Workers, Progress($"{p.Id} x {o.Bot}"));
                var stats = Report.Aggregate(rows, c);
                var text = Report.FormatReport($"Preset {p.Name}: {o.Bot} bot, {o.Seeds.Count} seeds{o.InRoom}{DescribeOverrides(o.Overrides)}", stats);
                log.WriteLine(text);
                ReportErrors(rows, log);
                var win = rows.FirstOrDefault(r => r.Outcome == FightOutcome.Win && r.Record != null);
                if (win != null)
                {
                    Directory.CreateDirectory(dir);
                    var file = Path.Combine(dir, $"winning-line-{p.Id}.json");
                    File.WriteAllText(file, win.Record!.ToJson());
                    lines.Add($"{p.Name}: winning line found (seed {win.Seed}, {win.Rounds} rounds) -> {file}");
                }
                else
                {
                    lines.Add($"{p.Name}: no winning line in {o.Seeds.Count} seeds");
                }
                WriteOutputs(dir, null, $"preset-{p.Id}-{o.Bot}", rows, text, stats, log);
            }
            log.WriteLine($"\nPaper check (does each preset have a winning line?)\n  {string.Join("\n  ", lines)}");
        }

        private static void RunSweep(RunOptions o, string? outDir, TextWriter log)
        {
            var c = Batch.ContentFor(o.Files, o.Overrides);
            var size = o.Size ?? c.Rules.Loadout.Abilities;
            var spec = new BatchSpec { Bot = o.Bot, Loadout = LoadoutChoice.OfSweep(size), Seeds = o.Seeds, Settings = o.Settings };
            var rows = Batch.RunBatchParallel(spec, c, o.Workers, Progress($"sweep {size} of {c.AbilityOrder.Count}"));
            var stats = Report.Aggregate(rows, c);
            var text = Report.FormatReport($"Loadout sweep: {size} of {c.AbilityOrder.Count} abilities, {o.Bot} bot, {o.Seeds.Count} random loadouts{o.InRoom}{DescribeOverrides(o.Overrides)}", stats);
            log.WriteLine(text);
            ReportErrors(rows, log);
            WriteOutputs(outDir, o.Csv, $"sweep-{size}-{o.Bot}", rows, text, stats, log, new[] { new KeyValuePair<string, string>("lift.csv", Report.LiftToCsv(stats.Lift)) });
        }

        private static void RunTune(RunOptions o, string? param, string? values)
        {
            if (string.IsNullOrEmpty(param) || string.IsNullOrEmpty(values)) throw new ArgumentException("tune needs --param PATH and --values A,B,C");
            var lines = new List<string>
            {
                $"Tuning {param} on the {o.Preset} preset{o.InRoom} ({o.Bot} bot, {o.Seeds.Count} seeds each)",
                $"{"value".PadRight(10)} {"win rate".PadRight(26)} rounds(win)  hazard fires  own-damage share",
            };
            foreach (var raw in values.Split(','))
            {
                var overrides = new OrderedMap<string, JToken>(StringComparer.Ordinal);
                foreach (var pair in o.Overrides) overrides[pair.Key] = pair.Value;
                overrides[param] = ParseValue(raw);
                var c = Batch.ContentFor(o.Files, overrides);
                var spec = new BatchSpec { Bot = o.Bot, Loadout = LoadoutChoice.OfPreset(o.Preset), Seeds = o.Seeds, Settings = o.Settings };
                var rows = Batch.RunBatchParallel(spec, c, o.Workers, Progress($"{param}={raw}"));
                var stats = Report.Aggregate(rows, c);
                // A bad value can break only some seeds; aggregate leaves those out, so the row has to say they happened.
                lines.AddRange(Report.FormatTuneRow(raw, stats, rows.FirstOrDefault(r => r.Outcome == FightOutcome.Error)?.Error));
            }
            Console.WriteLine(string.Join("\n", lines));
        }

        private static int RunGolden(RunOptions o, bool record)
        {
            const string dir = "golden";
            var files = ContentLoader.FilesFromDirectory("content");
            var fights = Golden.Fights(files, StrictJson.Parse(File.ReadAllText(Path.Combine(dir, "rooms.json"))));
            if (record)
            {
                Directory.CreateDirectory(dir);
                foreach (var f in fights)
                {
                    var (golden, events) = Golden.Record(f.Content, f.Preset, f.Settings);
                    File.WriteAllText(Path.Combine(dir, $"{f.Name}.json"), Hash.Stringify(golden, 2) + "\n");
                    File.WriteAllText(Path.Combine(dir, $"{f.Name}.events.jsonl"), Records.EventsToJsonl(events));
                    Console.WriteLine($"Recorded golden {f.Name}: {golden["outcome"]} in {golden["rounds"]} rounds, {golden["eventCount"]} events, hash {golden["eventsHash"]}");
                }
                return 0;
            }
            var failed = 0;
            foreach (var f in fights)
            {
                var file = Path.Combine(dir, $"{f.Name}.json");
                if (!File.Exists(file))
                {
                    Console.WriteLine($"{f.Name}: missing ({file}); run dotnet run --project core/Sim -c Release -- golden --record");
                    failed++;
                    continue;
                }
                var eventsFile = Path.Combine(dir, $"{f.Name}.events.jsonl");
                var expected = File.Exists(eventsFile) ? JsText.Trim(File.ReadAllText(eventsFile)).Split('\n') : null;
                var r = Golden.Verify(f.Content, StrictJson.Parse(File.ReadAllText(file)), expected);
                Console.WriteLine($"{f.Name}: {(r.Ok ? "ok" : $"FAILED - {r.Problem}")}");
                if (!r.Ok) failed++;
            }
            return failed > 0 ? 1 : 0;
        }

        private static void RunReplay(RunOptions o, string? file)
        {
            if (file == null) throw new ArgumentException("replay needs a FILE");
            var c = ContentLoader.Build(o.Files);
            var rec = Records.CheckCompatible(c, StrictJson.Parse(File.ReadAllText(file)));
            var r = Records.Replay(c, rec);
            var s = Summaries.Summarize(r.Events, r.State).ToToken();
            var output = new JObject();
            foreach (var key in new[] { "outcome", "cause", "rounds", "loadout", "playerDamageTaken", "kills", "peakMinions" }) output[key] = s[key];
            output["hazardFires"] = ((JArray)s["hazardFires"]!).Count;
            Console.WriteLine(Hash.Stringify(output, 2));
        }

        private static void RunTelemetry(RunOptions o, ParsedArgs a)
        {
            var dir = a.Positionals.ElementAtOrDefault(1) ?? "telemetry";
            var c = ContentLoader.Build(o.Files);
            var presetIds = c.Presets.Select(p => p.Id).ToList();
            var defaults = new PlaytestOptions();
            var informedPreset = a.Value("informed-preset") ?? defaults.InformedPreset;
            if (!presetIds.Contains(informedPreset)) throw new ArgumentException($"--informed-preset must be one of {string.Join(", ", presetIds)} (got \"{informedPreset}\")");
            var opts = new PlaytestOptions
            {
                InformedFrom = Options.ParseInteger("informed-from", a.Value("informed-from"), 1) ?? defaults.InformedFrom,
                InformedAttempts = Options.ParseInteger("informed-attempts", a.Value("informed-attempts"), 1) ?? defaults.InformedAttempts,
                InformedPreset = informedPreset,
            };
            Console.WriteLine(Playtest.FormatPlaytest(dir, Playtest.LoadTelemetry(c, dir, a.Flag("include-unlabelled"), a.List("exclude-tester").ToList()), opts));
        }

        /// <summary>Whether a generated file is one that is committed: every report except the per-fight CSVs.</summary>
        private static bool IsCommittedKind(string name)
        {
            return name.EndsWith(".txt", StringComparison.Ordinal) || name.EndsWith(".stats.json", StringComparison.Ordinal) || name.EndsWith(".lift.csv", StringComparison.Ordinal)
                || (name.StartsWith("winning-line-", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal));
        }

        private static IEnumerable<string> CommittedKinds(string root)
        {
            return Directory.GetFiles(root, "*", SearchOption.AllDirectories).Where(f => IsCommittedKind(Path.GetFileName(f))).Select(f => Path.GetRelativePath(root, f));
        }

        /// <summary>
        /// Regenerates every report the repository commits (see "Regenerating generated files" in CLAUDE.md) and compares each
        /// with the committed file, byte for byte. It proved the port against the TypeScript simulator's reports, and now
        /// that a rule or content change came with its reports regenerated.
        /// </summary>
        private static int RunConformance(RunOptions o)
        {
            var scratch = Path.Combine(Path.GetTempPath(), "lab-escape-conformance-" + Guid.NewGuid().ToString("N"));
            try
            {
                var seeds = Batch.SeedRange(1, ConformanceSeeds);
                var settings = new FightSettings();
                var overrides = new OrderedMap<string, JToken>(StringComparer.Ordinal);
                var clock = Stopwatch.StartNew();
                RunPresets(new RunOptions { Bot = Batch.GreedyKind, Seeds = seeds, Settings = settings, Overrides = overrides, Workers = o.Workers, Files = o.Files }, scratch, TextWriter.Null);
                RunPresets(new RunOptions { Bot = Batch.RandomKind, Seeds = seeds, Settings = settings, Overrides = overrides, Workers = o.Workers, Files = o.Files }, Path.Combine(scratch, "random"), TextWriter.Null);
                RunSweep(new RunOptions { Bot = Batch.GreedyKind, Seeds = seeds, Settings = settings, Overrides = overrides, Workers = o.Workers, Files = o.Files }, scratch, TextWriter.Null);
                var names = new SortedSet<string>(CommittedKinds(scratch).Concat(CommittedKinds("reports")), StringComparer.Ordinal);
                var failed = 0;
                foreach (var name in names)
                {
                    var problem = CompareReport(Path.Combine("reports", name), Path.Combine(scratch, name));
                    if (problem != null) failed++;
                    Console.WriteLine($"{name}: {problem ?? "identical"}");
                }
                Console.WriteLine($"\n{names.Count - failed} of {names.Count} reports identical ({JsText.ToFixed(clock.Elapsed.TotalSeconds, 1)} s, {Environment.ProcessorCount} cores)");
                return failed > 0 ? 1 : 0;
            }
            finally
            {
                if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
            }
        }

        private static string? CompareReport(string committed, string generated)
        {
            if (!File.Exists(committed)) return "generated, but not committed";
            if (!File.Exists(generated)) return "committed, but not generated";
            var want = File.ReadAllText(committed);
            var got = File.ReadAllText(generated);
            if (want == got) return null;
            var a = want.Split('\n');
            var b = got.Split('\n');
            var line = 0;
            while (line < a.Length && line < b.Length && a[line] == b[line]) line++;
            return $"DIFFERS at line {line + 1}\n  committed: {(line < a.Length ? a[line] : "(end of file)")}\n  generated: {(line < b.Length ? b[line] : "(end of file)")}";
        }
    }
}
