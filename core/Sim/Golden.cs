#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using LabEscape.Core;
using Newtonsoft.Json.Linq;

namespace LabEscape.Sim
{
    public sealed class GoldenCheck
    {
        public string Preset = "";
        public bool Ok;
        public string? Problem;
        public int? FirstDifference;
    }

    /// <summary>
    /// A golden replay: a fixed command script for a preset and the event log it must produce. If a build changes the event log
    /// without an intended rule change, the golden check fails.
    /// </summary>
    public static class Golden
    {
        public const long Seed = 1;
        private const string Note = "Scripted regression fight. Re-record on purpose when content values or rules change: npm run golden:record";

        public static string HashEvents(IEnumerable<GameEvent> events) => Hash.HashJson(new JArray(events.Select(e => JsonIn.Parse(e.ToJson()))));
        public static string HashState(GameState state) => Hash.HashJson(JsonIn.Parse(state.ToJson()));

        /// <summary>The scripted fight for a preset: a greedy-bot fight frozen into a command list (spec, Headless simulation).</summary>
        public static (JObject Golden, List<GameEvent> Events) Record(Content c, string presetId)
        {
            var p = c.Presets.FirstOrDefault(x => x.Id == presetId);
            if (p == null) throw new ArgumentException($"Unknown preset {presetId}");
            var fight = Runner.RunFight(c, new Loadout(p.Abilities, p.Upgrades), new GreedyBot(Seed), meta: new RecordMeta { Preset = p.Id, Label = $"golden {p.Id}" });
            var rep = Records.Replay(c, fight.Record);
            var golden = new JObject
            {
                ["preset"] = p.Id,
                ["note"] = Note,
                ["record"] = fight.Record.ToToken(),
                ["eventCount"] = rep.Events.Count,
                ["eventsHash"] = HashEvents(rep.Events),
                ["finalStateHash"] = HashState(rep.State),
                ["outcome"] = Names.Of(fight.Summary.Outcome),
                ["rounds"] = fight.Summary.Rounds,
            };
            return (golden, rep.Events);
        }

        /// <summary>Checks a golden file. expected holds the lines of its event log, when there is one, to say where it first differs.</summary>
        public static GoldenCheck Verify(Content c, JToken golden, IReadOnlyList<string>? expected = null)
        {
            var preset = golden["preset"]?.Value<string>() ?? "";
            FightRecord record;
            try
            {
                record = Records.CheckCompatible(c, golden["record"]);
            }
            catch (ReplayMismatch e)
            {
                return new GoldenCheck { Preset = preset, Problem = $"{e.Message}. Re-record with: npm run golden:record" };
            }
            ReplayResult rep;
            try
            {
                rep = Records.Replay(c, record);
            }
            catch (Exception e)
            {
                return new GoldenCheck { Preset = preset, Problem = e.Message };
            }
            if (HashEvents(rep.Events) == golden["eventsHash"]?.Value<string>() && HashState(rep.State) == golden["finalStateHash"]?.Value<string>())
            {
                return new GoldenCheck { Preset = preset, Ok = true };
            }
            int? first = null;
            if (expected != null)
            {
                var n = Math.Max(expected.Count, rep.Events.Count);
                for (var i = 0; i < n && !first.HasValue; i++)
                {
                    if (i >= expected.Count || i >= rep.Events.Count || expected[i] != rep.Events[i].ToJson()) first = i;
                }
            }
            var where = first.HasValue ? $"; first difference at event {first}" : "";
            return new GoldenCheck { Preset = preset, Problem = $"Event log changed ({rep.Events.Count} events, expected {golden["eventCount"]}){where}", FirstDifference = first };
        }
    }
}
