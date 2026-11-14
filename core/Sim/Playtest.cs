#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LabEscape.Core;

namespace LabEscape.Sim
{
    /// <summary>What the playtest metrics need from one saved fight.</summary>
    public sealed class PlaytestFight
    {
        /// <summary>The telemetry folder it came from.</summary>
        public string Name = "";
        public string? Tester;
        public double? Attempt;
        public string? Preset;
        public FightResult Outcome;
        public int Rounds;
        /// <summary>Share of the player's damage that came from the player's own side (0 to 1).</summary>
        public double FriendlyFireShare;
        /// <summary>Total damage the player took; a fight with none has no friendly-fire share to average.</summary>
        public int PlayerDamage;
        public SourceCategory? CauseCategory;
        public int HazardFires;
        /// <summary>Seconds the player took on each turn that ended with a full swarm (minions at the cap).</summary>
        public List<double> FullSwarmTurnSeconds = new List<double>();
        /// <summary>False when the replay has fewer or more turn times than turns, so no turn can be matched to its time and none is used.</summary>
        public bool TurnTimesComplete = true;
    }

    public sealed class SkippedFight
    {
        public string Name = "";
        public string Reason = "";
    }

    public sealed class TelemetryLoad
    {
        public List<PlaytestFight> Fights = new List<PlaytestFight>();
        /// <summary>Folders left out, with the reason: not a replay, an old engine or content version, unfinished, or played with debug commands.</summary>
        public List<SkippedFight> Skipped = new List<SkippedFight>();
    }

    public sealed class PlaytestOptions
    {
        /// <summary>The first attempt number counted as an informed attempt.</summary>
        public double InformedFrom = 2;
        /// <summary>How many attempts per tester the informed round has (the spec: 3), counted from InformedFrom.</summary>
        public double InformedAttempts = 3;
        public string InformedPreset = "hybrid";
    }

    public static class Playtest
    {
        /// <summary>
        /// Reads every DIR/fight/replay.json and replays it, so every figure comes from the fight itself and a replay from another
        /// engine or content version is refused, not misread. Fights that did not finish, or used debug commands, are skipped and
        /// reported.
        /// </summary>
        public static TelemetryLoad LoadTelemetry(Content c, string dir, bool includeUnlabelled = false, IReadOnlyCollection<string>? excludeTesters = null)
        {
            if (!Directory.Exists(dir)) throw new DirectoryNotFoundException($"No telemetry folder at {dir}. Play some fights in the Unity editor first, or name the folder a build saved them in.");
            var output = new TelemetryLoad();
            var names = new DirectoryInfo(dir).GetDirectories().Where(d => d.LinkTarget == null).Select(d => d.Name);
            foreach (var name in Sequences.StableSorted(names, LocaleOrder.Compare))
            {
                var file = Path.Combine(dir, name, "replay.json");
                if (!File.Exists(file))
                {
                    output.Skipped.Add(new SkippedFight { Name = name, Reason = "no replay.json" });
                    continue;
                }
                try
                {
                    var rec = Records.CheckCompatible(c, StrictJson.Parse(File.ReadAllText(file)));
                    var f = AnalyseFight(c, rec, name, out var why);
                    if (f == null) output.Skipped.Add(new SkippedFight { Name = name, Reason = why! });
                    else if (f.Tester != null && !f.Attempt.HasValue) output.Skipped.Add(new SkippedFight { Name = name, Reason = "has a tester ID but no attempt number" });
                    // Every fight the dev server finishes is saved here, so ordinary play with no tester ID sits beside the playtest's fights.
                    else if (f.Tester == null && !includeUnlabelled) output.Skipped.Add(new SkippedFight { Name = name, Reason = "no tester ID (--include-unlabelled counts it)" });
                    else if (f.Tester != null && excludeTesters != null && excludeTesters.Contains(f.Tester)) output.Skipped.Add(new SkippedFight { Name = name, Reason = $"tester {f.Tester} excluded (--exclude-tester)" });
                    else output.Fights.Add(f);
                }
                catch (Exception e)
                {
                    var lines = e.Message.Split('\n').Select(JsText.Trim).Where(l => l.Length > 0).Take(2);
                    output.Skipped.Add(new SkippedFight { Name = name, Reason = string.Join(" ", lines) });
                }
            }
            return output;
        }

        private static bool SameSet(IReadOnlyCollection<string> a, IReadOnlyCollection<string> b) => a.Count == b.Count && a.All(b.Contains);

        /// <summary>The figures for one fight, or null with the reason it does not count.</summary>
        public static PlaytestFight? AnalyseFight(Content c, FightRecord rec, string name, out string? reason)
        {
            reason = null;
            if (rec.Commands.Any(cmd => cmd is DebugCommand))
            {
                reason = "used debug commands";
                return null;
            }
            // The shortened fight (wave 3 held back) has another win condition, so it would skew every figure here.
            if (rec.Settings.MaxWaves.HasValue)
            {
                reason = "played with wave 3 held back";
                return null;
            }
            var r = Records.Replay(c, rec, keepStates: true);
            var s = Summaries.Summarize(r.Events, r.State);
            if (s.Outcome == FightOutcome.Unfinished)
            {
                reason = "unfinished";
                return null;
            }
            // A turn ends at each End Turn, and at the action that ends the fight if that was not an End Turn. The client records
            // one time per turn, in that order, so turn k's time pairs with the state just before its last command.
            var ends = new List<int>();
            for (var i = 0; i < rec.Commands.Count; i++)
            {
                if (rec.Commands[i] is EndTurnCommand) ends.Add(i);
            }
            if (rec.Commands.Count > 0 && !(rec.Commands[rec.Commands.Count - 1] is EndTurnCommand)) ends.Add(rec.Commands.Count);
            var times = rec.Meta?.TurnTimesMs ?? new List<double>();
            var cap = c.Rules.Caps.Minions;
            var swarm = new List<double>();
            // A missing or extra time shifts every later one onto the wrong turn, so a fight whose count is off contributes none.
            var complete = times.Count == ends.Count;
            if (complete)
            {
                for (var k = 0; k < ends.Count; k++)
                {
                    var minions = r.States[ends[k]].Units.Count(u => u.Kind == UnitKind.Minion && !u.Dead);
                    if (minions >= cap) swarm.Add(times[k] / 1000);
                }
            }
            return new PlaytestFight
            {
                Name = name,
                Tester = rec.Meta?.Tester,
                Attempt = rec.Meta?.Attempt,
                // From the loadout the fight was played with, not from the replay's own claim about it.
                Preset = c.Presets.FirstOrDefault(p => SameSet(p.Abilities, rec.Loadout.Abilities) && SameSet(p.Upgrades, rec.Loadout.Upgrades))?.Id,
                Outcome = s.Outcome == FightOutcome.Win ? FightResult.Win : FightResult.Lose,
                Rounds = s.Rounds,
                FriendlyFireShare = s.FriendlyFireShare,
                PlayerDamage = s.PlayerDamageTaken.Total,
                CauseCategory = s.CauseOfDeath?.Category,
                HazardFires = s.HazardFires.Count,
                FullSwarmTurnSeconds = swarm,
                TurnTimesComplete = complete,
            };
        }

        private static double Mean(IReadOnlyList<double> xs) => xs.Count > 0 ? xs.Sum() / xs.Count : 0;
        private static double Median(IEnumerable<int> xs) => Stats.Median(xs.Select(x => (double)x).ToList());
        private static MetricCheck Na(string metric, string target, string alarm, string why) => new MetricCheck { Metric = metric, Value = why, Target = target, Alarm = alarm, Status = "n/a" };

        private static List<PlaytestFight> OnePerKey(IEnumerable<PlaytestFight> fights, Func<PlaytestFight, bool> counted)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var kept = new List<PlaytestFight>();
            foreach (var f in fights)
            {
                if (!counted(f) || f.Tester == null || !f.Attempt.HasValue) continue;
                if (seen.Add($"{f.Tester}\u0000{JsText.Number(f.Attempt.Value)}")) kept.Add(f);
            }
            return kept;
        }

        /// <summary>The spec's "Metrics and alarms" that telemetry can answer, from the fights loaded. Same status words as the simulator's report.</summary>
        public static List<MetricCheck> PlaytestChecks(IReadOnlyList<PlaytestFight> fights, PlaytestOptions? options = null)
        {
            var opts = options ?? new PlaytestOptions();
            var last = JsText.Number(opts.InformedFrom + opts.InformedAttempts - 1);
            var from = JsText.Number(opts.InformedFrom);
            var output = new List<MetricCheck>();
            var losses = fights.Where(f => f.Outcome == FightResult.Lose).ToList();
            var wins = fights.Where(f => f.Outcome == FightResult.Win).ToList();

            // One first attempt per tester: attempt numbers are counted per browser, so a tester who plays on another browser, or
            // clears storage, can leave several fights numbered 1. The earliest counts (folder names begin with the time).
            var testersSeen = new HashSet<string>(StringComparer.Ordinal);
            var first = fights.Where(f => f.Tester != null && f.Attempt == 1 && testersSeen.Add(f.Tester)).ToList();
            var firstLost = first.Where(f => f.Outcome == FightResult.Lose).ToList();
            // A tester whose only recorded fight is a lost first attempt may have quit there (or may not have played again yet).
            var stopped = firstLost.Where(f => !fights.Any(g => g.Tester == f.Tester && (g.Attempt ?? 0) > 1)).ToList();
            if (first.Count > 0)
            {
                var share = (double)firstLost.Count / first.Count;
                output.Add(new MetricCheck { Metric = "First attempts lost", Value = $"{firstLost.Count} of {first.Count} testers ({Report.Pct(share)}); {stopped.Count} with no fight after the loss", Target = "about 70% (4 of 5, up to 6 of 8)", Alarm = "fewer than half, or players quit after one loss", Status = share < 0.5 ? "alarm" : share < 0.6 || share > 0.8 ? "warn" : "ok" });
            }
            else
            {
                output.Add(Na("First attempts lost", "about 70% (4 of 5, up to 6 of 8)", "fewer than half", "no fights with a tester and attempt 1"));
            }

            // The first-time round is every attempt before the informed round, so a tester who made several first-time attempts has them all counted.
            var firstTimeLosses = OnePerKey(fights, f => f.Attempt < opts.InformedFrom && f.Outcome == FightResult.Lose);
            if (firstTimeLosses.Count > 0)
            {
                var m = Median(firstTimeLosses.Select(f => f.Rounds));
                output.Add(new MetricCheck { Metric = "First-time losses that reach wave 2", Value = $"median loss in round {JsText.Number(m)}", Target = "most reach round 4", Alarm = "median loss before round 3", Status = m < 3 ? "alarm" : m < 4 ? "warn" : "ok" });
            }
            else
            {
                output.Add(Na("First-time losses that reach wave 2", "most reach round 4", "median loss before round 3", "no lost first-time attempts"));
            }

            // One fight per tester and attempt number, the earliest, as for first attempts: the number is counted per browser.
            var informed = OnePerKey(fights, f => f.Preset == opts.InformedPreset && f.Attempt >= opts.InformedFrom && f.Attempt < opts.InformedFrom + opts.InformedAttempts);
            if (informed.Count > 0)
            {
                var won = informed.Count(f => f.Outcome == FightResult.Win);
                var groups = informed.GroupBy(f => f.Attempt!.Value).OrderBy(g => g.Key).Select(g => $"attempt {JsText.Number(g.Key)}: {g.Count(f => f.Outcome == FightResult.Win)}/{g.Count()}");
                var rate = (double)won / informed.Count;
                output.Add(new MetricCheck { Metric = $"Informed win rate ({opts.InformedPreset} preset, attempts {from} to {last})", Value = $"{won} of {informed.Count} ({Report.Pct(rate)}); {string.Join(", ", groups)}", Target = "30% to 50%", Alarm = "below 15% or above 70%", Status = rate < 0.15 || rate > 0.7 ? "alarm" : rate < 0.3 || rate > 0.5 ? "warn" : "ok" });
            }
            else
            {
                output.Add(Na($"Informed win rate ({opts.InformedPreset} preset)", "30% to 50%", "below 15% or above 70%", $"no {opts.InformedPreset} fights at attempts {from} to {last}"));
            }

            if (wins.Count > 0)
            {
                var m = Median(wins.Select(f => f.Rounds));
                output.Add(new MetricCheck { Metric = "Length of winning fights", Value = $"median {JsText.Number(m)} rounds ({wins.Count} wins)", Target = "8 to 12", Alarm = "over 16", Status = m > 16 ? "alarm" : m < 8 || m > 12 ? "warn" : "ok" });
            }
            else
            {
                output.Add(Na("Length of winning fights", "8 to 12", "over 16", "no wins"));
            }

            var swarm = fights.SelectMany(f => f.FullSwarmTurnSeconds).ToList();
            var untimed = fights.Count(f => !f.TurnTimesComplete);
            var untimedNote = untimed > 0 ? $"; {untimed} of {fights.Count} fights left out for missing or incomplete turn times" : "";
            if (swarm.Count > 0)
            {
                // Rounded before it is shown and judged, so the figure on the page is the one the status was decided on.
                var m = Stats.RoundHalfUp(Stats.Median(swarm) * 10) / 10;
                output.Add(new MetricCheck { Metric = "Median turn time with a full swarm", Value = $"{JsText.ToFixed(m, 1)} s over {swarm.Count} turns{untimedNote}", Target = "under 60 s", Alarm = "over 90 s", Status = m > 90 ? "alarm" : m >= 60 ? "warn" : "ok" });
            }
            else
            {
                output.Add(Na("Median turn time with a full swarm", "under 60 s", "over 90 s", $"no turn ended with the minion cap reached{untimedNote}"));
            }

            // A fight where the player took no damage has no share, and counting it as 0% would drag the mean down.
            var damaged = fights.Where(f => f.PlayerDamage > 0).ToList();
            if (damaged.Count > 0)
            {
                var ff = Mean(damaged.Select(f => f.FriendlyFireShare).ToList());
                output.Add(new MetricCheck { Metric = "Share of player damage from own effects", Value = $"{Report.Pct(ff)} ({damaged.Count} fights where the player took damage)", Target = "5% to 30%", Alarm = "over 35%, or under 3%", Status = ff > 0.35 || ff < 0.03 ? "alarm" : ff < 0.05 || ff > 0.3 ? "warn" : "ok" });
            }
            else
            {
                output.Add(Na("Share of player damage from own effects", "5% to 30%", "over 35%, or under 3%", fights.Count > 0 ? "the player took no damage in any fight" : "no fights"));
            }

            if (losses.Count > 0)
            {
                var counts = new OrderedMap<string, int>(StringComparer.Ordinal);
                foreach (var f in losses)
                {
                    var key = f.CauseCategory.HasValue ? Names.Of(f.CauseCategory.Value) : "round cap";
                    counts[key] = (counts.TryGetValue(key, out var n) ? n : 0) + 1;
                }
                var ranked = Sequences.StableSorted(counts, (a, b) => a.Value != b.Value ? b.Value - a.Value : LocaleOrder.Compare(a.Key, b.Key));
                var hazard = (double)(counts.TryGetValue("hazard", out var h) ? h : 0) / losses.Count;
                output.Add(new MetricCheck { Metric = "Share of deaths from one source", Value = string.Join(", ", ranked.Select(p => $"{p.Key} {Report.Pct((double)p.Value / losses.Count)}")), Target = "no source above 60%", Alarm = "hazards above 60%", Status = hazard > 0.6 ? "alarm" : (double)ranked[0].Value / losses.Count > 0.6 ? "warn" : "ok" });
            }
            else
            {
                output.Add(Na("Share of deaths from one source", "no source above 60%", "hazards above 60%", "no lost fights"));
            }

            if (fights.Count > 0)
            {
                var h = Mean(fights.Select(f => (double)f.HazardFires).ToList());
                output.Add(new MetricCheck { Metric = "Hazard fires per fight", Value = JsText.ToFixed(h, 2), Target = "at least 2", Alarm = "under 1 on average", Status = h < 1 ? "alarm" : h < 2 ? "warn" : "ok" });
            }
            else
            {
                output.Add(Na("Hazard fires per fight", "at least 2", "under 1 on average", "no fights"));
            }
            return output;
        }

        /// <summary>The report text: what was read, each metric against its target, then each tester's attempts.</summary>
        public static string FormatPlaytest(string dir, TelemetryLoad load, PlaytestOptions? options = null)
        {
            var title = $"Playtest telemetry: {dir}";
            var lines = new List<string> { title, new string('=', title.Length), $"Fights counted: {load.Fights.Count}; skipped: {load.Skipped.Count}" };
            lines.AddRange(load.Skipped.Select(s => $"  skipped {s.Name}: {s.Reason}"));
            lines.Add("");
            lines.Add("Spec metrics:");
            lines.AddRange(PlaytestChecks(load.Fights, options).Select(Report.CheckLine));
            if (load.Fights.Count == 0) return string.Join("\n", lines);
            var testers = new OrderedMap<string, List<PlaytestFight>>(StringComparer.Ordinal);
            foreach (var f in load.Fights)
            {
                var key = f.Tester ?? "(no tester)";
                if (!testers.ContainsKey(key)) testers[key] = new List<PlaytestFight>();
                testers[key].Add(f);
            }
            lines.Add("");
            lines.Add("By tester:");
            foreach (var pair in Sequences.StableSorted(testers, (a, b) => LocaleOrder.Compare(a.Key, b.Key)))
            {
                var attempts = Sequences.StableSorted(pair.Value, (a, b) => (a.Attempt ?? 0).CompareTo(b.Attempt ?? 0))
                    .Select(f => $"{(f.Attempt.HasValue ? "a" + JsText.Number(f.Attempt.Value) : "?")} {f.Preset ?? "custom"} {(f.Outcome == FightResult.Win ? "win" : "lose")} r{f.Rounds}");
                lines.Add($"  {pair.Key}: {string.Join(", ", attempts)}");
            }
            return string.Join("\n", lines);
        }
    }
}
