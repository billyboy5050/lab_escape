#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LabEscape.Core;
using Newtonsoft.Json.Linq;

namespace LabEscape.Sim
{
    public sealed class LiftRow
    {
        public string Pick = "";
        public string Name = "";
        public string Kind = "";
        public int WithN;
        public int WithoutN;
        public double WithWinRate;
        public double WithoutWinRate;
        /// <summary>Win rate with the pick minus win rate without it, in percentage points.</summary>
        public double Lift;
    }

    public sealed class MetricCheck
    {
        public string Metric = "";
        public string Value = "";
        public string Target = "";
        public string Alarm = "";
        /// <summary>ok, warn, alarm or n/a.</summary>
        public string Status = "";
    }

    public sealed class RoundsFigures
    {
        public double Median;
        public double Mean;
        /// <summary>Wins only.</summary>
        public double? Min;
        public double? Max;
    }

    public sealed class MeanDamage
    {
        public double Player;
        public double Minion;
        public double Enemy;
        public double Hazard;
    }

    public sealed class BatchStats
    {
        public int Fights;
        public int Errors;
        public int Wins;
        public double WinRate;
        public (double Low, double High) WinRateCi;
        public RoundsFigures? RoundsWin;
        public RoundsFigures? RoundsLoss;
        public int LossesReachingWave2;
        public OrderedMap<string, int> CausesByCategory = new OrderedMap<string, int>(StringComparer.Ordinal);
        public List<KeyValuePair<string, int>> TopCauses = new List<KeyValuePair<string, int>>();
        public MeanDamage MeanPlayerDamage = new MeanDamage();
        public double MeanFriendlyFireShare;
        public double MeanHazardFires;
        public int FightsWithTwoHazardFires;
        public double MeanPeakMinions;
        public int MaxPeakMinions;
        public double? WardenReachedShare;
        public double? WardenEarlyDeathShare;
        public OrderedMap<string, double> AbilityUse = new OrderedMap<string, double>(StringComparer.Ordinal);
        public List<LiftRow> Lift = new List<LiftRow>();
        public List<MetricCheck> Checks = new List<MetricCheck>();

        /// <summary>The same fields, in the same order, as the TypeScript object the .stats.json report is written from.</summary>
        public JObject ToToken()
        {
            var abilityUse = new JObject();
            foreach (var pair in AbilityUse) abilityUse[pair.Key] = pair.Value;
            return new JObject
            {
                ["fights"] = Fights,
                ["errors"] = Errors,
                ["wins"] = Wins,
                ["winRate"] = WinRate,
                ["winRateCI"] = new JArray(WinRateCi.Low, WinRateCi.High),
                ["roundsWin"] = RoundsWin == null ? JValue.CreateNull() : new JObject { ["median"] = RoundsWin.Median, ["mean"] = RoundsWin.Mean, ["min"] = RoundsWin.Min, ["max"] = RoundsWin.Max },
                ["roundsLoss"] = RoundsLoss == null ? JValue.CreateNull() : new JObject { ["median"] = RoundsLoss.Median, ["mean"] = RoundsLoss.Mean },
                ["lossesReachingWave2"] = LossesReachingWave2,
                ["causesByCategory"] = Summaries.Counts(CausesByCategory),
                ["topCauses"] = new JArray(TopCauses.Select(t => new JArray(t.Key, t.Value))),
                ["meanPlayerDamage"] = new JObject { ["player"] = MeanPlayerDamage.Player, ["minion"] = MeanPlayerDamage.Minion, ["enemy"] = MeanPlayerDamage.Enemy, ["hazard"] = MeanPlayerDamage.Hazard },
                ["meanFriendlyFireShare"] = MeanFriendlyFireShare,
                ["meanHazardFires"] = MeanHazardFires,
                ["fightsWithTwoHazardFires"] = FightsWithTwoHazardFires,
                ["meanPeakMinions"] = MeanPeakMinions,
                ["maxPeakMinions"] = MaxPeakMinions,
                ["wardenReachedShare"] = WardenReachedShare,
                ["wardenEarlyDeathShare"] = WardenEarlyDeathShare,
                ["abilityUse"] = abilityUse,
                ["lift"] = new JArray(Lift.Select(r => new JObject { ["pick"] = r.Pick, ["name"] = r.Name, ["kind"] = r.Kind, ["withN"] = r.WithN, ["withoutN"] = r.WithoutN, ["withWinRate"] = r.WithWinRate, ["withoutWinRate"] = r.WithoutWinRate, ["lift"] = r.Lift })),
                ["checks"] = new JArray(Checks.Select(c => new JObject { ["metric"] = c.Metric, ["value"] = c.Value, ["target"] = c.Target, ["alarm"] = c.Alarm, ["status"] = c.Status })),
            };
        }
    }

    public static class Report
    {
        private static double Mean(IEnumerable<double> xs)
        {
            var sum = 0.0;
            var n = 0;
            foreach (var x in xs)
            {
                sum += x;
                n++;
            }
            return n > 0 ? sum / n : 0;
        }

        private static double Mean(IEnumerable<int> xs) => Mean(xs.Select(x => (double)x));
        private static double Median(IEnumerable<int> xs) => Stats.Median(xs.Select(x => (double)x).ToList());
        internal static string Pct(double x) => JsText.ToFixed(x * 100, 1) + "%";
        private static string Fixed1(double x) => JsText.ToFixed(x, 1);
        private static string Fixed2(double x) => JsText.ToFixed(x, 2);

        /// <summary>Wilson score interval for a proportion (95%).</summary>
        public static (double Low, double High) Wilson(int k, int n)
        {
            if (n == 0) return (0, 0);
            var z = 1.96;
            double nd = n;
            var p = k / nd;
            var den = 1 + (z * z) / nd;
            var centre = (p + (z * z) / (2 * nd)) / den;
            var half = (z * Math.Sqrt((p * (1 - p)) / nd + (z * z) / (4 * nd * nd))) / den;
            return (Math.Max(0, centre - half), Math.Min(1, centre + half));
        }

        private static void Bump(OrderedMap<string, int> counts, string key)
        {
            counts[key] = (counts.TryGetValue(key, out var n) ? n : 0) + 1;
        }

        public static BatchStats Aggregate(IReadOnlyList<FightRow> rows, Content c)
        {
            var ok = rows.Where(r => r.Outcome != FightOutcome.Error).ToList();
            var wins = ok.Where(r => r.Outcome == FightOutcome.Win).ToList();
            var losses = ok.Where(r => r.Outcome == FightOutcome.Lose).ToList();
            var causes = new OrderedMap<string, int>(StringComparer.Ordinal);
            var sources = new OrderedMap<string, int>(StringComparer.Ordinal);
            foreach (var r in losses)
            {
                var cat = r.DeathCategory.HasValue ? Names.Of(r.DeathCategory.Value) : (r.Cause != null && r.Cause.StartsWith("Round", StringComparison.Ordinal) ? "round cap" : "other");
                Bump(causes, cat);
                if (r.DeathSource != null) Bump(sources, r.DeathSource);
            }
            var reached7 = ok.Where(r => r.Rounds >= 7 && r.WavesSpawned >= 3).ToList();
            var abilityUse = new OrderedMap<string, double>(StringComparer.Ordinal);
            foreach (var r in ok)
            {
                foreach (var pair in r.AbilitiesUsed) abilityUse[pair.Key] = (abilityUse.TryGetValue(pair.Key, out var n) ? n : 0) + pair.Value;
            }
            foreach (var key in abilityUse.Keys.ToList()) abilityUse[key] = abilityUse[key] / Math.Max(1, ok.Count);
            var stats = new BatchStats
            {
                Fights = rows.Count,
                Errors = rows.Count - ok.Count,
                Wins = wins.Count,
                WinRate = ok.Count > 0 ? (double)wins.Count / ok.Count : 0,
                WinRateCi = Wilson(wins.Count, ok.Count),
                RoundsWin = wins.Count > 0 ? new RoundsFigures { Median = Median(wins.Select(r => r.Rounds)), Mean = Mean(wins.Select(r => r.Rounds)), Min = wins.Min(r => r.Rounds), Max = wins.Max(r => r.Rounds) } : null,
                RoundsLoss = losses.Count > 0 ? new RoundsFigures { Median = Median(losses.Select(r => r.Rounds)), Mean = Mean(losses.Select(r => r.Rounds)) } : null,
                LossesReachingWave2 = losses.Count(r => r.WavesSpawned >= 2),
                CausesByCategory = causes,
                TopCauses = Sequences.StableSorted(sources, (a, b) => b.Value - a.Value).Take(6).ToList(),
                MeanPlayerDamage = new MeanDamage { Player = Mean(ok.Select(r => r.PlayerDamage.Player)), Minion = Mean(ok.Select(r => r.PlayerDamage.Minion)), Enemy = Mean(ok.Select(r => r.PlayerDamage.Enemy)), Hazard = Mean(ok.Select(r => r.PlayerDamage.Hazard)) },
                MeanFriendlyFireShare = Mean(ok.Where(HasAnyDamage).Select(r => r.FriendlyFireShare)),
                MeanHazardFires = Mean(ok.Select(r => r.HazardFires)),
                FightsWithTwoHazardFires = ok.Count(r => r.HazardFires >= 2),
                MeanPeakMinions = Mean(ok.Select(r => r.PeakMinions)),
                MaxPeakMinions = ok.Count > 0 ? ok.Max(r => r.PeakMinions) : 0,
                WardenReachedShare = reached7.Count > 0 ? (double)reached7.Count(r => r.WardenReachedPlayerRound.HasValue) / reached7.Count : (double?)null,
                WardenEarlyDeathShare = reached7.Count > 0 ? (double)reached7.Count(r => r.WardenDiedRound.HasValue && r.WardenDiedRound.Value < 9) / reached7.Count : (double?)null,
                AbilityUse = abilityUse,
                Lift = WinRateLift(ok, c),
            };
            stats.Checks = MetricChecks(stats, losses.Count);
            return stats;
        }

        private static bool HasAnyDamage(FightRow r) => r.PlayerDamage.Player > 0 || r.PlayerDamage.Minion > 0 || r.PlayerDamage.Enemy > 0 || r.PlayerDamage.Hazard > 0;

        /// <summary>Win-rate lift of each ability and upgrade across a set of fights with varied loadouts.</summary>
        public static List<LiftRow> WinRateLift(IReadOnlyList<FightRow> rows, Content c)
        {
            var loadouts = new HashSet<string>(rows.Select(r => string.Join(",", r.Abilities) + "|" + string.Join(",", r.Upgrades)), StringComparer.Ordinal);
            var output = new List<LiftRow>();
            if (loadouts.Count < 2) return output;
            var picks = c.AbilityOrder.Select(id => (Id: id, Kind: "ability", Name: c.Abilities[id].Name)).Concat(c.UpgradeOrder.Select(id => (Id: id, Kind: "upgrade", Name: c.Upgrades[id].Name)));
            foreach (var (id, kind, name) in picks)
            {
                bool Has(FightRow r) => kind == "ability" ? r.Abilities.Contains(id) : r.Upgrades.Contains(id);
                var with = rows.Where(Has).ToList();
                var without = rows.Where(r => !Has(r)).ToList();
                if (with.Count == 0 || without.Count == 0) continue;
                var wr = (double)with.Count(r => r.Outcome == FightOutcome.Win) / with.Count;
                var wor = (double)without.Count(r => r.Outcome == FightOutcome.Win) / without.Count;
                output.Add(new LiftRow { Pick = id, Name = name, Kind = kind, WithN = with.Count, WithoutN = without.Count, WithWinRate = wr, WithoutWinRate = wor, Lift = (wr - wor) * 100 });
            }
            return Sequences.StableSorted(output, (a, b) => b.Lift.CompareTo(a.Lift));
        }

        /// <summary>The spec's metric targets and alarms that a bot batch can speak to.</summary>
        private static List<MetricCheck> MetricChecks(BatchStats s, int lossCount)
        {
            var output = new List<MetricCheck>();
            if (s.RoundsWin != null)
            {
                var m = s.RoundsWin.Median;
                output.Add(new MetricCheck { Metric = "Length of winning fights", Value = $"median {JsText.Number(m)}, mean {Fixed1(s.RoundsWin.Mean)}", Target = "8 to 12", Alarm = "over 16", Status = m > 16 ? "alarm" : m < 8 || m > 12 ? "warn" : "ok" });
            }
            // The same per-fight mean the report prints as its friendly-fire headline, so the two never disagree.
            var own = s.MeanFriendlyFireShare;
            output.Add(new MetricCheck { Metric = "Share of player damage from own effects", Value = Pct(own), Target = "5% to 30%", Alarm = "over 35%, or under 3%", Status = own > 0.35 || own < 0.03 ? "alarm" : own < 0.05 || own > 0.3 ? "warn" : "ok" });
            if (lossCount > 0)
            {
                var top = Sequences.StableSorted(s.CausesByCategory.Select(p => (Key: p.Key, Share: (double)p.Value / lossCount)), (a, b) => b.Share.CompareTo(a.Share))[0];
                var hazard = s.CausesByCategory.TryGetValue("hazard", out var h) ? h : 0;
                output.Add(new MetricCheck { Metric = "Share of deaths from one source", Value = $"{top.Key} {Pct(top.Share)}", Target = "no source above 60%", Alarm = "hazards above 60%", Status = (double)hazard / lossCount > 0.6 ? "alarm" : top.Share > 0.6 ? "warn" : "ok" });
            }
            output.Add(new MetricCheck
            {
                Metric = "Hazard fires per fight",
                Value = $"{Fixed2(s.MeanHazardFires)} ({Pct((double)s.FightsWithTwoHazardFires / Math.Max(1, s.Fights - s.Errors))} of fights at least 2)",
                Target = "at least 2",
                Alarm = "under 1 on average",
                Status = s.MeanHazardFires < 1 ? "alarm" : s.MeanHazardFires < 2 ? "warn" : "ok",
            });
            if (s.Lift.Count > 0)
            {
                var top = s.Lift[0];
                output.Add(new MetricCheck { Metric = "Win-rate lift of each ability and upgrade", Value = $"max {Fixed1(top.Lift)} points ({top.Name})", Target = "under 25 points", Alarm = "any above 40 points", Status = top.Lift > 40 ? "alarm" : top.Lift >= 25 ? "warn" : "ok" });
            }
            if (s.WardenReachedShare.HasValue)
            {
                var early = s.WardenEarlyDeathShare ?? 0;
                output.Add(new MetricCheck
                {
                    Metric = "Warden reaches the player",
                    Value = $"{Pct(s.WardenReachedShare.Value)} of fights that reach round 7; dies before round 9 in {Pct(early)}",
                    Target = "in most fights that reach round 7",
                    Alarm = "dies before round 9 in most fights",
                    Status = early > 0.5 ? "alarm" : s.WardenReachedShare.Value < 0.5 ? "warn" : "ok",
                });
            }
            return output;
        }

        internal static string CheckLine(MetricCheck ch) => $"  [{ch.Status.ToUpperInvariant().PadRight(5)}] {ch.Metric}: {ch.Value}  (target {ch.Target}; alarm {ch.Alarm})";

        private static string Joined(IEnumerable<KeyValuePair<string, int>> items) => string.Join(", ", items.Select(p => $"{p.Key} {p.Value}"));

        public static string FormatReport(string title, BatchStats s)
        {
            var lines = new List<string>
            {
                "\n" + title,
                new string('=', title.Length),
                $"Fights: {s.Fights}{(s.Errors > 0 ? $"  (errors: {s.Errors})" : "")}",
                $"Win rate: {Pct(s.WinRate)}  (95% CI {Pct(s.WinRateCi.Low)} to {Pct(s.WinRateCi.High)}), {s.Wins} wins",
            };
            if (s.RoundsWin != null) lines.Add($"Rounds, wins:   median {JsText.Number(s.RoundsWin.Median)}, mean {Fixed1(s.RoundsWin.Mean)}, range {JsText.Number(s.RoundsWin.Min!.Value)} to {JsText.Number(s.RoundsWin.Max!.Value)}");
            if (s.RoundsLoss != null) lines.Add($"Rounds, losses: median {JsText.Number(s.RoundsLoss.Median)}, mean {Fixed1(s.RoundsLoss.Mean)}; {s.LossesReachingWave2} reached wave 2");
            var causes = Sequences.StableSorted(s.CausesByCategory, (a, b) => b.Value - a.Value);
            if (causes.Count > 0) lines.Add($"Causes of death: {Joined(causes)}");
            if (s.TopCauses.Count > 0) lines.Add($"Top killers: {Joined(s.TopCauses)}");
            var d = s.MeanPlayerDamage;
            lines.Add($"Mean damage to the player per fight: player {Fixed2(d.Player)}, minion {Fixed2(d.Minion)}, enemy {Fixed2(d.Enemy)}, hazard {Fixed2(d.Hazard)}");
            lines.Add($"Mean friendly-fire share of the player's damage: {Pct(s.MeanFriendlyFireShare)}");
            lines.Add($"Hazard fires per fight: {Fixed2(s.MeanHazardFires)}; peak minions: mean {Fixed2(s.MeanPeakMinions)}, max {s.MaxPeakMinions}");
            var use = Sequences.StableSorted(s.AbilityUse, (a, b) => b.Value.CompareTo(a.Value));
            if (use.Count > 0) lines.Add($"Uses per fight: {string.Join(", ", use.Select(p => $"{p.Key} {Fixed1(p.Value)}"))}");
            if (s.Lift.Count > 0)
            {
                lines.Add("\nWin-rate lift (with minus without, points):");
                foreach (var r in s.Lift) lines.Add($"  {(r.Lift >= 0 ? "+" : "")}{Fixed1(r.Lift).PadLeft(5)}  {r.Name.PadRight(22)} {r.Kind.PadRight(8)} with {Pct(r.WithWinRate)} (n={r.WithN}), without {Pct(r.WithoutWinRate)} (n={r.WithoutN})");
            }
            if (s.Checks.Count > 0)
            {
                lines.Add("\nSpec metrics (bot proxy):");
                foreach (var ch in s.Checks) lines.Add(CheckLine(ch));
            }
            return string.Join("\n", lines);
        }

        /// <summary>
        /// The lines a tune table shows for one tested value. Fights that raised errors are left out of every figure, so a value
        /// where some or all of them did says so, with the first error, instead of presenting what is left as the result.
        /// </summary>
        public static List<string> FormatTuneRow(string value, BatchStats s, string? firstError = null)
        {
            var indent = new string(' ', 11);
            string Detail(string extra) => $"{indent}first error: {firstError ?? "unknown"}{extra}";
            if (s.Errors == s.Fights) return new List<string> { $"{value.PadRight(10)} all {s.Fights} fights errored", Detail("") };
            var own = s.MeanPlayerDamage.Player + s.MeanPlayerDamage.Minion;
            var all = own + s.MeanPlayerDamage.Enemy + s.MeanPlayerDamage.Hazard;
            var rate = $"{JsText.ToFixed(s.WinRate * 100, 1)}% ({JsText.ToFixed(s.WinRateCi.Low * 100, 0)} to {JsText.ToFixed(s.WinRateCi.High * 100, 0)})";
            var share = all != 0 ? Fixed1((own / all) * 100) : "0.0";
            var figures = $"{value.PadRight(10)} {rate.PadRight(26)} {(s.RoundsWin != null ? JsText.Number(s.RoundsWin.Median) : "-").PadRight(12)} {Fixed2(s.MeanHazardFires).PadRight(13)} {share}%";
            if (s.Errors == 0) return new List<string> { figures };
            return new List<string> { $"{figures}  ! {s.Errors} of {s.Fights} fights errored", Detail($"; the figures cover the {s.Fights - s.Errors} that finished") };
        }

        private static string Esc(string? v)
        {
            var s = v ?? "";
            return s.IndexOfAny(new[] { '"', ',', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }

        private static string? Whole(int? n) => n.HasValue ? JsText.Number(n.Value) : null;

        /// <summary>One row per fight, for spreadsheets.</summary>
        public static string RowsToCsv(IEnumerable<FightRow> rows)
        {
            var sb = new StringBuilder("seed,bot,preset,abilities,upgrades,outcome,rounds,cause,death_category,death_source,player_dmg_player,player_dmg_minion,player_dmg_enemy,player_dmg_hazard,friendly_fire_share,enemy_dmg_player,enemy_dmg_minion,enemy_dmg_enemy,enemy_dmg_hazard,hazard_fires,hazard_fires_hit_player,peak_minions,waves_spawned,warden_reached_player_round,warden_died_round,kills,commands,error\n");
            foreach (var r in rows)
            {
                var cells = new[]
                {
                    JsText.Number((double)r.Seed), r.Bot, r.Preset ?? "", string.Join(" ", r.Abilities), string.Join(" ", r.Upgrades), Names.Of(r.Outcome), Whole(r.Rounds), r.Cause,
                    r.DeathCategory.HasValue ? Names.Of(r.DeathCategory.Value) : null, r.DeathSource,
                    Whole(r.PlayerDamage.Player), Whole(r.PlayerDamage.Minion), Whole(r.PlayerDamage.Enemy), Whole(r.PlayerDamage.Hazard),
                    JsText.ToFixed(r.FriendlyFireShare, 3),
                    Whole(r.EnemyDamage.Player), Whole(r.EnemyDamage.Minion), Whole(r.EnemyDamage.Enemy), Whole(r.EnemyDamage.Hazard),
                    Whole(r.HazardFires), Whole(r.HazardFiresHitPlayer), Whole(r.PeakMinions), Whole(r.WavesSpawned), Whole(r.WardenReachedPlayerRound), Whole(r.WardenDiedRound), Whole(r.Kills), Whole(r.Commands), r.Error,
                };
                sb.Append(string.Join(",", cells.Select(Esc))).Append('\n');
            }
            return sb.ToString();
        }

        public static string LiftToCsv(IEnumerable<LiftRow> lift)
        {
            var lines = new List<string> { "pick,name,kind,with_n,without_n,with_win_rate,without_win_rate,lift_points" };
            foreach (var r in lift) lines.Add(string.Join(",", r.Pick, r.Name, r.Kind, JsText.Number(r.WithN), JsText.Number(r.WithoutN), JsText.ToFixed(r.WithWinRate, 4), JsText.ToFixed(r.WithoutWinRate, 4), JsText.ToFixed(r.Lift, 2)));
            return string.Join("\n", lines) + "\n";
        }
    }
}
