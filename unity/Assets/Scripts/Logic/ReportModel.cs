#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using LabEscape.Core;

namespace LabEscape.Client
{
    /// <summary>One bar of a report: who did how much damage.</summary>
    public sealed class ReportBar
    {
        public string Label = "";
        public int Value;
        public SourceCategory Category;
    }

    /// <summary>A group of bars with its title and the line under it, which can be a warning.</summary>
    public sealed class ReportSection
    {
        public string Title = "";
        public List<ReportBar> Bars = new List<ReportBar>();
        public string? Note;
        public bool NoteWarns;
        /// <summary>The most any bar of the section shows against, so its longest bar fills the track.</summary>
        public int Max => Math.Max(1, Bars.Select(b => b.Value).DefaultIfEmpty(0).Max());
    }

    /// <summary>
    /// The incident report a fight ends with, as the web client's result screen has it (`ResultScreen.tsx`): the verdict, the
    /// damage taken and dealt by source, and the fight's facts. It has no Unity code and reads everything from the summary the
    /// telemetry already makes, so it agrees with the files saved.
    /// </summary>
    public sealed class ReportModel
    {
        /// <summary>The share of the damage you take that comes from your own side past which the report warns (the spec's alarm).</summary>
        public const int FriendlyFireAlarmPercent = 35;

        public static readonly SourceCategory[] CategoryOrder = { SourceCategory.Enemy, SourceCategory.Hazard, SourceCategory.Player, SourceCategory.Minion };

        public bool Won;
        public string Kicker = "";
        public string Heading = "";
        public string Verdict = "";
        public List<string> Pills = new List<string>();
        public bool FriendlyFireWarns;
        public string Loadout = "";
        public List<ReportSection> Sections = new List<ReportSection>();
        public List<(string Label, string Value)> Facts = new List<(string, string)>();
        public string Telemetry = "";
        public List<(int Round, string Text)> Intercom = new List<(int, string)>();

        public static string CategoryLabel(SourceCategory c)
        {
            switch (c)
            {
                case SourceCategory.Enemy: return "Enemies";
                case SourceCategory.Hazard: return "Lab hazards";
                case SourceCategory.Player: return "Your own abilities";
                default: return "Your own minions";
            }
        }

        public static ReportModel From(Content c, RoomDef room, FightSummary s, GameState final, FightRecord record)
        {
            var won = s.Outcome == FightOutcome.Win;
            string Name(string id) => c.Abilities.TryGetValue(id, out var a) ? a.Name : c.Upgrades.TryGetValue(id, out var u) ? u.Name : c.Utilities.TryGetValue(id, out var t) ? t.Name : id;
            var playerTotal = s.PlayerDamageTaken.Total;
            var ffPercent = playerTotal > 0 ? (int)Stats.RoundHalfUp(100.0 * s.FriendlyFireToPlayer / playerTotal) : 0;
            var m = new ReportModel
            {
                Won = won,
                Kicker = $"Incident report · {room.Name}",
                Heading = won ? "Lockdown broken" : "Intruder down",
                FriendlyFireWarns = ffPercent > FriendlyFireAlarmPercent,
            };
            m.Verdict = won
                ? "The room is compromised."
                : $"Cause of death: {(s.CauseOfDeath != null ? $"{s.CauseOfDeath.Source} ({s.CauseOfDeath.Via}), {CategoryLabel(s.CauseOfDeath.Category).ToLowerInvariant()}" : s.Cause ?? "unknown")}";
            m.Pills.Add($"{(won ? "Won" : "Survived")} {s.Rounds} round{(s.Rounds == 1 ? "" : "s")}");
            m.Pills.Add($"Waves seen {s.WavesSpawned}/{room.Waves.Count}");
            m.Pills.Add($"Friendly-fire share of your damage {ffPercent}%");
            m.Loadout = "Loadout: " + string.Join(", ", s.Loadout.Abilities.Select(Name)) + (s.Loadout.Upgrades.Count > 0 ? " · " + string.Join(", ", s.Loadout.Upgrades.Select(Name)) : "");

            ReportSection Section(string title, DamageTally tally, string? note, bool warns = false)
            {
                var section = new ReportSection { Title = title, Note = note, NoteWarns = warns };
                foreach (var cat in CategoryOrder) section.Bars.Add(new ReportBar { Label = CategoryLabel(cat), Value = tally[cat], Category = cat });
                return section;
            }
            m.Sections.Add(Section("Damage you took, by source", s.PlayerDamageTaken, $"Friendly fire: {s.FriendlyFireToPlayer} of {playerTotal} ({ffPercent}%)", m.FriendlyFireWarns));
            m.Sections.Add(Section("Damage dealt to enemies, by source", s.EnemyDamageTaken, null));
            m.Sections.Add(Section("Damage your side took (minions, shields, eggs)", s.SideDamageTaken, $"Friendly fire to your side: {s.FriendlyFireToSide}"));

            var turnTimes = record.Meta?.TurnTimesMs;
            m.Facts.Add(("Kills", s.Kills.Count == 0 ? "none" : string.Join(", ", s.Kills.Select(k => $"{k.Value} {(c.Units.TryGetValue(k.Key, out var u) ? u.Name : k.Key)}"))));
            m.Facts.Add(("Peak minions", $"{s.PeakMinions} of {c.Rules.Caps.Minions} (lost {s.MinionsLost})"));
            m.Facts.Add(("Hazard fires", s.HazardFires.Count == 0 ? "none" : string.Join("; ", s.HazardFires.Select(h => $"R{h.Round} {h.Hazard}->{string.Join("/", h.Victims)}"))));
            m.Facts.Add(("Abilities used", s.AbilitiesUsed.Count == 0 ? "none" : string.Join(", ", s.AbilitiesUsed.Select(a => $"{Name(a.Key)} x{a.Value}"))));
            m.Facts.Add(("Median turn time", turnTimes != null && turnTimes.Count > 0 ? $"{Stats.Median(turnTimes) / 1000.0:0.0} s" : "-"));
            m.Facts.Add(("Warden", (s.WardenReachedPlayerRound.HasValue ? $"reached you in round {s.WardenReachedPlayerRound}" : "never reached you") + (s.WardenDiedRound.HasValue ? $", died in round {s.WardenDiedRound}" : "")));
            m.Telemetry = $"A replay file reproduces this fight exactly (engine {record.EngineVersion}, content {record.ContentHash}).";
            foreach (var line in final.Intercom.Log) m.Intercom.Add((line.Round, line.Text));
            return m;
        }
    }
}
