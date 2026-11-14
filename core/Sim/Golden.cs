#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using LabEscape.Core;
using Newtonsoft.Json.Linq;

namespace LabEscape.Sim
{
    /// <summary>One golden replay to record or check: each file in golden/ is named after Name.</summary>
    public sealed class GoldenFight
    {
        public string Name = "";
        public Content Content = null!;
        public string Preset = "";
        public FightSettings Settings = new FightSettings();
    }

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

        /// <summary>The preset that fights the golden rooms: it uses all three kits.</summary>
        public const string RoomPreset = "hybrid";
        private const string Note = "Scripted regression fight. Re-record on purpose when content values or rules change: dotnet run --project core/Sim -c Release -- golden --record";

        public static string HashEvents(IEnumerable<GameEvent> events) => Hash.HashJson(new JArray(events.Select(e => JsonIn.Parse(e.ToJson()))));
        public static string HashState(GameState state) => Hash.HashJson(JsonIn.Parse(state.ToJson()));

        /// <summary>
        /// Every golden fight: each preset in the Armory, then the golden room preset in each room of golden/rooms.json. The rooms are
        /// not game content. They exist to pin down fights of other sizes, so they join the content only for their own fights.
        /// </summary>
        public static List<GoldenFight> Fights(ContentFiles files, JToken rooms)
        {
            var armory = ContentLoader.Build(files.DeepClone());
            var extended = files.DeepClone();
            extended.Root["rooms"] = rooms.DeepClone();
            var withRooms = ContentLoader.Build(extended);
            var fights = armory.Presets.Select(p => new GoldenFight { Name = p.Id, Content = armory, Preset = p.Id }).ToList();
            foreach (var room in (JArray)rooms)
            {
                var id = room["id"]!.Value<string>()!;
                fights.Add(new GoldenFight { Name = "room-" + id, Content = withRooms, Preset = RoomPreset, Settings = new FightSettings { Room = id } });
            }
            return fights;
        }

        /// <summary>The scripted fight for a preset: a greedy-bot fight frozen into a command list (spec, Headless simulation).</summary>
        public static (JObject Golden, List<GameEvent> Events) Record(Content c, string presetId, FightSettings? settings = null)
        {
            var p = c.Presets.FirstOrDefault(x => x.Id == presetId);
            if (p == null) throw new ArgumentException($"Unknown preset {presetId}");
            var fight = Runner.RunFight(c, new Loadout(p.Abilities, p.Upgrades), new GreedyBot(Seed), settings, meta: new RecordMeta { Preset = p.Id, Label = settings?.Room != null ? $"golden {p.Id} in {settings.Room}" : $"golden {p.Id}" });
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
                return new GoldenCheck { Preset = preset, Problem = $"{e.Message}. Re-record with: dotnet run --project core/Sim -c Release -- golden --record" };
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
