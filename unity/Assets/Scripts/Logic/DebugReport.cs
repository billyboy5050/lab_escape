#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using LabEscape.Core;

namespace LabEscape.Client
{
    /// <summary>Which of the debug overlay's marks are drawn on the board, and whether the log writes its debug lines.</summary>
    public sealed class DebugToggles
    {
        public bool Ids = true;
        public bool Spawns = true;
        public bool Hazards = true;
        public bool Flamer = true;
        public bool Log;
    }

    /// <summary>A tile where a wave that has not arrived yet will put a unit.</summary>
    public readonly struct SpawnMark
    {
        public readonly Pos Tile;
        public readonly int Wave;
        public readonly string Def;

        public SpawnMark(Pos tile, int wave, string def)
        {
            Tile = tile;
            Wave = wave;
            Def = def;
        }
    }

    /// <summary>One unit's scored options, from the enemy phase that intents dry-run: the Flamer's, today.</summary>
    public sealed class ScoredOptions
    {
        public readonly int Id;
        public readonly string Def;
        public readonly IReadOnlyList<AiOption> Options;

        public ScoredOptions(int id, string def, IReadOnlyList<AiOption> options)
        {
            Id = id;
            Def = def;
            Options = options;
        }
    }

    /// <summary>
    /// What the debug overlay says, worked out from a state with no Unity code, so EditMode tests read it as the panel does. It
    /// reads what the engine recorded (each hazard's last decision, the AI's scored options) and decides nothing itself.
    /// </summary>
    public static class DebugReport
    {
        /// <summary>How many of a unit's options the panel lists, best first.</summary>
        public const int OptionsListed = 8;

        public static string Header(Content c) => $"engine {GameStates.EngineVersion} · content {c.Hash}";

        public static IReadOnlyList<WaveDef> Waves(Content c, GameState s) => c.RoomFor(s.Settings.Room).Waves;

        /// <summary>The hidden wave timer: the round, and how many waves have arrived (`spawned`, as the board has shown them).</summary>
        public static string WaveCount(Content c, GameState s, int spawned)
        {
            var waves = Waves(c, s);
            var held = s.Settings.MaxWaves.HasValue ? $" (in play: {s.Settings.MaxWaves})" : "";
            return $"Round {s.Round} · waves spawned {spawned}/{waves.Count}{held}";
        }

        /// <summary>When the next wave arrives, and whether an empty room can bring it forward.</summary>
        public static string NextWave(Content c, GameState s, int spawned)
        {
            var waves = Waves(c, s);
            if (spawned >= waves.Count) return "No waves left";
            var next = waves[Math.Max(0, spawned)];
            var left = Math.Max(0, next.Round - s.Round);
            var when = left == 0 ? "this round" : left == 1 ? "in 1 round" : $"in {left} rounds";
            var early = c.Rules.EarlyWaveWhenEmpty && spawned > 0 ? ", sooner if no enemy is left" : "";
            return $"Next: wave {spawned + 1} at round {next.Round} ({when}{early})";
        }

        /// <summary>Every wave: its round, and each unit's kind and spawn tile.</summary>
        public static IEnumerable<string> WaveList(Content c, GameState s) =>
            Waves(c, s).Select((w, i) => $"W{i + 1} @ R{w.Round}: {string.Join(", ", w.Units.Select(u => $"{u.Def} {u.Tile}"))}");

        /// <summary>The spawn tiles of the waves after the first `spawned`, as the board marks them.</summary>
        public static List<SpawnMark> Spawns(Content c, GameState s, int spawned)
        {
            var marks = new List<SpawnMark>();
            var waves = Waves(c, s);
            for (var i = Math.Max(0, spawned); i < waves.Count; i++)
            {
                foreach (var u in waves[i].Units) marks.Add(new SpawnMark(Tiles.Parse(u.Tile), i + 1, u.Def));
            }
            return marks;
        }

        /// <summary>A hazard's state as the panel names it.</summary>
        public static string StateText(Hazard h) => h.State == HazardState.Cooldown ? $"cooldown {h.Cooldown}" : Names.Of(h.State);

        /// <summary>A hazard's state as the board marks it, short enough to sit on its tile.</summary>
        public static string HazardMark(Hazard h) => h.State == HazardState.Cooldown ? $"cd{h.Cooldown}" : h.State == HazardState.Primed ? "PRIMED" : "ready";

        /// <summary>
        /// A hazard's last decision: when it was taken, at which stage, what it decided and which step of the value rule
        /// decided it, why, and who it would have hit.
        /// </summary>
        public static string Decision(Content c, GameState s, HazardDecision? d)
        {
            if (d == null) return "No decision yet";
            var step = d.Step.HasValue ? $", step {d.Step}" : "";
            var victims = d.Victims.Count > 0 ? $" {(d.Result == HazardResult.Fire ? "Hits" : "Would hit")}: {string.Join(", ", d.Victims.Select(v => VictimName(c, s, v)))}." : "";
            return $"R{d.Round} {Names.Of(d.Stage)}: {Names.Of(d.Result)}{step}. {d.Reason}.{victims}";
        }

        private static string VictimName(Content c, GameState s, EntityRef v)
        {
            if (v.Kind == EntityKind.Unit)
            {
                var u = s.Units.FirstOrDefault(x => x.Id == v.Id);
                return u != null ? Words.UnitName(c, u.Def, u.Id) : $"unit {v.Id}";
            }
            var o = s.Objects.FirstOrDefault(x => x.Id == v.Id);
            return o != null ? $"{Words.TargetName(c, EntityKind.Object, o.Id, Names.Of(o.Kind))} {o.Id}" : $"object {v.Id}";
        }

        /// <summary>Every unit whose decision in the intents' dry run was scored over options, in the order they acted.</summary>
        public static List<ScoredOptions> Scored(IntentReport? rep)
        {
            var found = new List<ScoredOptions>();
            if (rep == null) return found;
            foreach (var f in rep.Frames)
            {
                foreach (var e in f.Events)
                {
                    if (e is AiDecision d && d.Options != null && d.Options.Count > 0) found.Add(new ScoredOptions(d.Id, d.Def, d.Options));
                }
            }
            return found;
        }

        /// <summary>A unit's options, best first and at most `OptionsListed`: tile, facing, score and steps.</summary>
        public static IEnumerable<string> OptionLines(ScoredOptions o) =>
            o.Options.OrderByDescending(x => x.Score).Take(OptionsListed).Select(x => $"{Tiles.Name(x.Tile)} {Facing(x.Dir)}  score {x.Score} ({x.Steps} steps)");

        public static string OptionCount(ScoredOptions o) => $"{o.Options.Count(x => x.Score > 0)} positive of {o.Options.Count} non-zero";

        /// <summary>A grid direction by compass letter: north is up the rows.</summary>
        public static string Facing(Pos dir) => dir.Y < 0 ? "N" : dir.X > 0 ? "E" : dir.Y > 0 ? "S" : "W";

        /// <summary>
        /// The panel's text, as (text, style) lines, where the style is "section" for a heading, "head", "muted" or "faint".
        /// `hazards` and `spawned` are as the board shows them, so the panel and the board agree while a frame plays; `scored`
        /// comes from the intents, which exist only while the player can act.
        /// </summary>
        public static List<(string Text, string Style)> Panel(Content c, GameState s, IReadOnlyList<Hazard> hazards, int spawned, IReadOnlyList<ScoredOptions> scored, IEnumerable<GameEvent> events)
        {
            var lines = new List<(string, string)>
            {
                ("WAVE TIMER (HIDDEN FROM PLAYERS)", "section"),
                (WaveCount(c, s, spawned), ""),
                (NextWave(c, s, spawned), ""),
            };
            foreach (var w in WaveList(c, s)) lines.Add((w, "faint"));
            lines.Add(("HAZARDS", "section"));
            foreach (var h in hazards)
            {
                lines.Add(($"{Words.HazardName(c, s, h.Id)}: {StateText(h)}", "head"));
                lines.Add((Decision(c, s, h.Last), "muted"));
            }
            foreach (var o in scored)
            {
                lines.Add(($"{Words.UnitName(c, o.Def, o.Id).ToUpperInvariant()} OPTIONS (SCORED NOW)", "section"));
                lines.Add((OptionCount(o), "faint"));
                foreach (var l in OptionLines(o)) lines.Add((l, ""));
            }
            lines.Add(("CHAIN RULE", "section"));
            lines.Add((Chain(events), ""));
            return lines;
        }

        /// <summary>The chain rule: how many triggers it has stopped this fight.</summary>
        public static string Chain(IEnumerable<GameEvent> events)
        {
            var n = events.Count(e => e is ChainSuppressed);
            return n == 0 ? "No suppressed triggers this fight" : $"{n} suppressed trigger{(n == 1 ? "" : "s")} this fight";
        }
    }
}
