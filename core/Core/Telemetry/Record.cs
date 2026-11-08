#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace LabEscape.Core
{
    /// <summary>
    /// What a replay says about itself: telemetry only, ignored when it is replayed. Held as JSON so the fields keep the order
    /// they were set in and a file's own fields survive, as they do in the TypeScript object. Setting a field to null removes it.
    /// </summary>
    public sealed class RecordMeta
    {
        private readonly JObject _fields;

        public RecordMeta()
        {
            _fields = new JObject();
        }

        private RecordMeta(JObject fields)
        {
            _fields = fields;
        }

        public static RecordMeta FromToken(JObject fields) => new RecordMeta((JObject)fields.DeepClone());

        public JObject ToToken() => (JObject)_fields.DeepClone();

        public string? Created { get => Text("created"); set => SetText("created", value); }
        public string? Label { get => Text("label"); set => SetText("label", value); }
        public string? Bot { get => Text("bot"); set => SetText("bot", value); }
        public double? Seed { get => Num("seed"); set => SetNum("seed", value); }
        public string? Preset { get => Text("preset"); set => SetText("preset", value); }
        /// <summary>Who played it, from the playtest: a short ID such as P3.</summary>
        public string? Tester { get => Text("tester"); set => SetText("tester", value); }
        /// <summary>This tester's attempt number, counting from 1.</summary>
        public double? Attempt { get => Num("attempt"); set => SetNum("attempt", value); }
        /// <summary>How the board was drawn: top, iso or both.</summary>
        public string? View { get => Text("view"); set => SetText("view", value); }

        /// <summary>Wall-clock milliseconds the player spent on each turn. A copy: use AddTurnTime to append.</summary>
        public List<double>? TurnTimesMs
        {
            get => _fields["turnTimesMs"] is JArray times ? times.Select(J.Number).ToList() : null;
            set
            {
                if (value == null) _fields.Remove("turnTimesMs");
                else _fields["turnTimesMs"] = new JArray(value);
            }
        }

        public void AddTurnTime(double milliseconds)
        {
            if (_fields["turnTimesMs"] is JArray times) times.Add(milliseconds);
        }

        /// <summary>A copy with the other meta's fields laid over it, as { ...this, ...other } does.</summary>
        public RecordMeta MergedWith(RecordMeta? other)
        {
            var merged = ToToken();
            if (other != null)
            {
                foreach (var property in other._fields.Properties()) merged[property.Name] = property.Value.DeepClone();
            }
            return new RecordMeta(merged);
        }

        private string? Text(string key) => J.StrOf(_fields[key]);
        private double? Num(string key) => J.IsNumber(_fields[key]) ? J.Number(_fields[key]!) : (double?)null;

        private void SetText(string key, string? value)
        {
            if (value == null) _fields.Remove(key);
            else _fields[key] = value;
        }

        private void SetNum(string key, double? value)
        {
            if (!value.HasValue) _fields.Remove(key);
            else _fields[key] = value.Value;
        }
    }

    /// <summary>
    /// A fight is fully described by its loadout, settings and the ordered player commands. Replaying them against the same
    /// engine version and content hash reproduces the fight exactly.
    /// </summary>
    public sealed class FightRecord
    {
        public const string RecordFormat = "lab-escape/replay@1";

        public string Format = RecordFormat;
        public string EngineVersion = "";
        public string ContentHash = "";
        public Loadout Loadout = null!;
        public FightSettings Settings = new FightSettings();
        public List<Command> Commands = new List<Command>();
        public RecordMeta? Meta;

        public JObject ToToken()
        {
            var settings = new JObject();
            if (Settings.MaxWaves.HasValue) settings["maxWaves"] = Settings.MaxWaves.Value;
            var o = new JObject
            {
                ["format"] = Format,
                ["engineVersion"] = EngineVersion,
                ["contentHash"] = ContentHash,
                ["loadout"] = new JObject { ["abilities"] = new JArray(Loadout.Abilities), ["upgrades"] = new JArray(Loadout.Upgrades) },
                ["settings"] = settings,
                ["commands"] = new JArray(Commands.Select(c => JsonIn.Parse(c.ToJson()))),
            };
            if (Meta != null) o["meta"] = Meta.ToToken();
            return o;
        }

        /// <summary>The replay file: JSON.stringify(record, null, 2).</summary>
        public string ToJson() => Hash.Stringify(ToToken(), 2);

        /// <summary>A record from JSON that has already passed Records.RecordProblems.</summary>
        internal static FightRecord FromChecked(JObject r)
        {
            var settings = new FightSettings();
            var maxWaves = r["settings"]!["maxWaves"];
            if (maxWaves != null) settings.MaxWaves = (int)Math.Min(J.Number(maxWaves), int.MaxValue); // A larger number holds nothing back either way.
            return new FightRecord
            {
                Format = J.StrOf(r["format"]) ?? "",
                EngineVersion = J.StrOf(r["engineVersion"]) ?? "",
                ContentHash = J.StrOf(r["contentHash"]) ?? "",
                Loadout = new Loadout(r["loadout"]!["abilities"]!.Select(x => x.Value<string>()!), r["loadout"]!["upgrades"]!.Select(x => x.Value<string>()!)),
                Settings = settings,
                Commands = r["commands"]!.Select(Command.Parse).ToList(),
                Meta = r["meta"] is JObject meta ? RecordMeta.FromToken(meta) : null,
            };
        }
    }

    public sealed class ReplayMismatch : Exception
    {
        public ReplayMismatch(string message) : base(message)
        {
        }
    }

    public sealed class ReplayResult
    {
        public GameState State = null!;
        public List<GameEvent> Events = new List<GameEvent>();
        public List<Frame> Frames = new List<Frame>();
        /// <summary>State after each command (index 0 is the start of round 1). Only filled when asked for.</summary>
        public List<GameState> States = new List<GameState>();
    }

    public static class Records
    {
        /// <summary>How many problems a rejection message lists; a file with thousands of bad commands would otherwise print them all.</summary>
        private const int MaxProblems = 6;

        private enum Kind { Text, Number, Whole, Tiles, Mode }

        private sealed class Fields
        {
            public readonly (string Key, Kind Kind)[] Need;
            public readonly (string Key, Kind Kind)[] May;

            public Fields((string, Kind)[] need, (string, Kind)[]? may = null)
            {
                Need = need;
                May = may ?? new (string, Kind)[0];
            }
        }

        private static readonly (string, Kind)[] None = new (string, Kind)[0];

        // The fields each command carries, by type: those it needs, and those it may have (null counts as absent).
        private static readonly OrderedMap<string, Fields> CommandFields = new OrderedMap<string, Fields>(StringComparer.Ordinal)
        {
            { "move", new Fields(new[] { ("path", Kind.Tiles) }) },
            { "sprint", new Fields(None) },
            { "ability", new Fields(new[] { ("ability", Kind.Text), ("target", Kind.Text) }, new[] { ("target2", Kind.Text), ("mode", Kind.Mode) }) },
            { "reload", new Fields(new[] { ("target", Kind.Text) }) },
            { "pickUpMine", new Fields(new[] { ("target", Kind.Text) }) },
            { "redeploy", new Fields(new[] { ("drone", Kind.Whole), ("target", Kind.Text) }) },
            { "endTurn", new Fields(None) },
        };

        private static readonly OrderedMap<string, Fields> DebugFields = new OrderedMap<string, Fields>(StringComparer.Ordinal)
        {
            { "setHp", new Fields(new[] { ("unit", Kind.Whole), ("value", Kind.Number) }) },
            { "setAp", new Fields(new[] { ("ap", Kind.Number) }, new[] { ("movement", Kind.Number) }) },
            { "spawn", new Fields(new[] { ("def", Kind.Text), ("target", Kind.Text) }) },
            { "forceWave", new Fields(None) },
        };

        private static bool KindOk(Kind kind, JToken? v)
        {
            switch (kind)
            {
                case Kind.Text: return J.StrOf(v) != null;
                case Kind.Number: return J.IsNumber(v) && J.IsFinite(v!);
                case Kind.Whole: return J.IsInteger(v);
                case Kind.Tiles: return v is JArray list && list.All(x => J.StrOf(x) != null);
                case Kind.Mode:
                    var mode = J.StrOf(v);
                    return mode == "self" || mode == "unit";
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        private static string Noun(Kind kind)
        {
            switch (kind)
            {
                case Kind.Text: return "text";
                case Kind.Number: return "a number";
                case Kind.Whole: return "a whole number";
                case Kind.Tiles: return "a list of tile names";
                case Kind.Mode: return "\"self\" or \"unit\"";
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        public static FightRecord NewRecord(Content c, Loadout loadout, FightSettings? settings = null, RecordMeta? meta = null)
        {
            return new FightRecord { EngineVersion = GameStates.EngineVersion, ContentHash = c.Hash, Loadout = loadout, Settings = settings ?? new FightSettings(), Meta = meta };
        }

        /// <summary>
        /// Structural problems in parsed replay JSON: the parts of a record that the loaders and screens read without checking
        /// must exist and have the right types, and so must the fields of each command. Whether a command is legal is the
        /// engine's business. Stops after a few problems.
        /// </summary>
        public static List<string> RecordProblems(JToken? rec)
        {
            var p = new List<string>();
            void Add(string message)
            {
                if (p.Count <= MaxProblems) p.Add(message);
            }
            if (!(rec is JObject r)) return new List<string> { "the file is not a replay file" };
            void CheckNames(JToken? v, string where)
            {
                if (!(v is JArray list))
                {
                    Add($"{where} must be a list");
                    return;
                }
                for (var i = 0; i < list.Count; i++)
                {
                    if (J.StrOf(list[i]) == null) Add($"{where}[{i}] must be a name (got {Hash.Stringify(list[i])})");
                }
            }
            if (!(r["loadout"] is JObject loadout))
            {
                Add("loadout must be an object with abilities and upgrades");
            }
            else
            {
                CheckNames(loadout["abilities"], "loadout.abilities");
                CheckNames(loadout["upgrades"], "loadout.upgrades");
            }
            if (!(r["settings"] is JObject settings))
            {
                Add("settings must be an object");
            }
            else
            {
                var w = settings["maxWaves"];
                if (w != null && (!J.IsInteger(w) || J.Number(w) < 1)) Add($"settings.maxWaves must be a whole number of at least 1 (got {Hash.Stringify(w)})");
            }
            if (!(r["commands"] is JArray commands)) Add("commands must be a list");
            else CheckCommands(commands, Add);
            if (r.ContainsKey("meta")) CheckMeta(r["meta"], Add);
            return p;
        }

        private static void CheckCommands(JArray commands, Action<string> add)
        {
            for (var i = 0; i < commands.Count; i++)
            {
                var type = commands[i] is JObject ? J.StrOf(commands[i]["type"]) : null;
                if (type == null)
                {
                    add($"commands[{i}] must be an object with a type");
                    continue;
                }
                var cmd = (JObject)commands[i];
                if (type != "debug" && !CommandFields.ContainsKey(type))
                {
                    add($"commands[{i}] has unknown type {Hash.Stringify(cmd["type"])}");
                    continue;
                }
                var op = J.StrOf(cmd["op"]);
                if (type == "debug" && (op == null || !DebugFields.ContainsKey(op)))
                {
                    add($"commands[{i}] has unknown debug op {Hash.Stringify(cmd["op"])}");
                    continue;
                }
                // The engine turns a command it will not allow into a message, but a bad number is not something it can refuse.
                var fields = type == "debug" ? DebugFields[op!] : CommandFields[type];
                foreach (var (key, kind) in fields.Need)
                {
                    if (!KindOk(kind, cmd[key])) add($"commands[{i}].{key} must be {Noun(kind)} (got {Hash.Stringify(cmd[key])})");
                }
                foreach (var (key, kind) in fields.May)
                {
                    if (!J.IsNullish(cmd[key]) && !KindOk(kind, cmd[key])) add($"commands[{i}].{key} must be {Noun(kind)} (got {Hash.Stringify(cmd[key])})");
                }
            }
        }

        private static void CheckMeta(JToken? metaToken, Action<string> add)
        {
            if (!(metaToken is JObject meta))
            {
                add("meta must be an object");
                return;
            }
            foreach (var k in new[] { "created", "label", "bot", "preset", "tester" })
            {
                if (meta.ContainsKey(k) && J.StrOf(meta[k]) == null) add($"meta.{k} must be text");
            }
            if (meta.ContainsKey("seed") && !J.IsNumber(meta["seed"])) add("meta.seed must be a number");
            if (meta.ContainsKey("attempt") && !(J.IsInteger(meta["attempt"]) && J.Number(meta["attempt"]!) >= 1)) add("meta.attempt must be a whole number of at least 1");
            if (meta.ContainsKey("view"))
            {
                var view = J.StrOf(meta["view"]);
                if (view != "top" && view != "iso" && view != "both") add("meta.view must be top, iso or both");
            }
            if (!meta.ContainsKey("turnTimesMs")) return;
            if (!(meta["turnTimesMs"] is JArray times))
            {
                add("meta.turnTimesMs must be a list of numbers");
                return;
            }
            for (var i = 0; i < times.Count; i++)
            {
                if (!J.IsNumber(times[i]) || !J.IsFinite(times[i]) || J.Number(times[i]) < 0) add($"meta.turnTimesMs[{i}] must be a number of milliseconds, 0 or more (got {Hash.Stringify(times[i])})");
            }
        }

        /// <summary>
        /// Throws a ReplayMismatch unless the parsed JSON is a replay of this engine version and content that has the whole
        /// record shape. Callers parse untrusted files. The header is checked first: an old file should be told it is old, not
        /// that its shape is wrong.
        /// </summary>
        public static FightRecord CheckCompatible(Content c, JToken? raw)
        {
            if (!(raw is JObject rec)) throw new ReplayMismatch("The file is not a replay file");
            if (J.StrOf(rec["format"]) != FightRecord.RecordFormat) throw new ReplayMismatch($"Unknown replay format \"{J.Show(rec["format"])}\"");
            if (J.StrOf(rec["engineVersion"]) != GameStates.EngineVersion) throw new ReplayMismatch($"Replay was recorded on engine {J.Show(rec["engineVersion"])}; this is {GameStates.EngineVersion}");
            if (J.StrOf(rec["contentHash"]) != c.Hash) throw new ReplayMismatch($"Replay was recorded with content {J.Show(rec["contentHash"])}; the current content is {c.Hash}");
            var problems = RecordProblems(rec);
            if (problems.Count > 0)
            {
                var shown = problems.Take(MaxProblems).ToList();
                if (problems.Count > MaxProblems) shown.Add("(further problems not shown)");
                throw new ReplayMismatch("The replay file is malformed:\n  - " + string.Join("\n  - ", shown));
            }
            return FightRecord.FromChecked(rec);
        }

        /// <summary>The same check on a record held in memory, which may have been edited since it was made.</summary>
        public static void CheckCompatible(Content c, FightRecord rec) => CheckCompatible(c, rec.ToToken());

        public static ReplayResult Replay(Content c, FightRecord rec, bool frames = false, bool strict = true, bool keepStates = false)
        {
            if (strict) CheckCompatible(c, rec);
            var g = Engine.NewGame(c, rec.Loadout, rec.Settings, frames);
            var result = new ReplayResult { State = g.State, Events = new List<GameEvent>(g.Events), Frames = new List<Frame>(g.Frames) };
            if (keepStates) result.States.Add(g.State);
            for (var i = 0; i < rec.Commands.Count; i++)
            {
                var cmd = rec.Commands[i];
                var r = Engine.Step(c, result.State, cmd, frames);
                if (!r.Ok) throw new ReplayMismatch($"Command {i + 1} ({cmd.Type}) was rejected: {r.Error}");
                result.State = r.State;
                result.Events.AddRange(r.Events);
                result.Frames.AddRange(r.Frames);
                if (keepStates) result.States.Add(r.State);
            }
            return result;
        }

        /// <summary>The event stream as JSON lines, one event per line.</summary>
        public static string EventsToJsonl(IEnumerable<GameEvent> events) => string.Join("\n", events.Select(e => e.ToJson())) + "\n";
    }
}
