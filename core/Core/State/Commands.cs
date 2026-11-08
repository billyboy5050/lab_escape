#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace LabEscape.Core
{
    // Commands. Tiles are written as names ("D5") so command logs are readable and hand-editable.

    public abstract class Command
    {
        protected abstract void WriteFields(JsonOut o);

        public abstract string Type { get; }

        /// <summary>This command as it appears in a replay's command list.</summary>
        public string ToJson()
        {
            var o = new JsonOut();
            o.BeginObject().Key("type").Value(Type);
            WriteFields(o);
            o.EndObject();
            return o.ToString();
        }

        /// <summary>Reads a command from a replay. Throws FormatException for anything that is not a command.</summary>
        public static Command Parse(JToken token)
        {
            var type = J.StrOf(J.Get(token, "type"));
            switch (type)
            {
                case "move":
                    return new MoveCommand(J.Get(token, "path") is JArray path ? path.Select(t => J.StrOf(t) ?? "").ToList() : new List<string>());
                case "sprint":
                    return new SprintCommand();
                case "ability":
                    return new AbilityCommand(J.StrOf(J.Get(token, "ability")) ?? "", J.StrOf(J.Get(token, "target")) ?? "", J.StrOf(J.Get(token, "target2")), J.StrOf(J.Get(token, "mode")));
                case "reload":
                    return new ReloadCommand(J.StrOf(J.Get(token, "target")) ?? "");
                case "pickUpMine":
                    return new PickUpMineCommand(J.StrOf(J.Get(token, "target")) ?? "");
                case "redeploy":
                    return new RedeployCommand(WholeOr(J.Get(token, "drone"), -1), J.StrOf(J.Get(token, "target")) ?? "");
                case "endTurn":
                    return new EndTurnCommand();
                case "debug":
                    return ParseDebug(token);
                default:
                    throw new FormatException($"Unknown command type \"{type}\"");
            }
        }

        private static int WholeOr(JToken? token, int fallback) => J.IsInteger(token) ? (int)J.Number(token!) : fallback;
        private static double NumberOr(JToken? token, double fallback) => J.IsNumber(token) ? J.Number(token!) : fallback;

        private static Command ParseDebug(JToken token)
        {
            var op = J.StrOf(J.Get(token, "op"));
            switch (op)
            {
                case "setHp":
                    return new DebugSetHp(WholeOr(J.Get(token, "unit"), -1), NumberOr(J.Get(token, "value"), 0));
                case "setAp":
                    return new DebugSetAp(NumberOr(J.Get(token, "ap"), 0), J.IsNumber(J.Get(token, "movement")) ? J.Number(J.Get(token, "movement")!) : (double?)null);
                case "spawn":
                    return new DebugSpawn(J.StrOf(J.Get(token, "def")) ?? "", J.StrOf(J.Get(token, "target")) ?? "");
                case "forceWave":
                    return new DebugForceWave();
                default:
                    throw new FormatException($"Unknown debug operation \"{op}\"");
            }
        }
    }

    public sealed class MoveCommand : Command
    {
        public readonly IReadOnlyList<string> Path;
        public MoveCommand(IEnumerable<string> path) { Path = path.ToList(); }
        public override string Type => "move";
        protected override void WriteFields(JsonOut o) => o.Key("path").Texts(Path);
    }

    public sealed class SprintCommand : Command
    {
        public override string Type => "sprint";
        protected override void WriteFields(JsonOut o) { }
    }

    public sealed class AbilityCommand : Command
    {
        public readonly string Ability;
        public readonly string Target;
        public readonly string? Target2;
        public readonly string? Mode;

        public AbilityCommand(string ability, string target, string? target2 = null, string? mode = null)
        {
            Ability = ability;
            Target = target;
            Target2 = target2;
            Mode = mode;
        }

        public override string Type => "ability";
        protected override void WriteFields(JsonOut o) => o.Key("ability").Value(Ability).Key("target").Value(Target).OptText("target2", Target2).OptText("mode", Mode);
    }

    public sealed class ReloadCommand : Command
    {
        public readonly string Target;
        public ReloadCommand(string target) { Target = target; }
        public override string Type => "reload";
        protected override void WriteFields(JsonOut o) => o.Key("target").Value(Target);
    }

    public sealed class PickUpMineCommand : Command
    {
        public readonly string Target;
        public PickUpMineCommand(string target) { Target = target; }
        public override string Type => "pickUpMine";
        protected override void WriteFields(JsonOut o) => o.Key("target").Value(Target);
    }

    public sealed class RedeployCommand : Command
    {
        public readonly int Drone;
        public readonly string Target;

        public RedeployCommand(int drone, string target)
        {
            Drone = drone;
            Target = target;
        }

        public override string Type => "redeploy";
        protected override void WriteFields(JsonOut o) => o.Key("drone").Value(Drone).Key("target").Value(Target);
    }

    public sealed class EndTurnCommand : Command
    {
        public override string Type => "endTurn";
        protected override void WriteFields(JsonOut o) { }
    }

    public abstract class DebugCommand : Command
    {
        public override string Type => "debug";
    }

    public sealed class DebugSetHp : DebugCommand
    {
        public readonly int Unit;
        /// <summary>A double because a command log may hold any number; the rule floors it.</summary>
        public readonly double Value;

        public DebugSetHp(int unit, double value)
        {
            Unit = unit;
            Value = value;
        }

        protected override void WriteFields(JsonOut o) => o.Key("op").Value("setHp").Key("unit").Value(Unit).Key("value").Value(Value);
    }

    public sealed class DebugSetAp : DebugCommand
    {
        public readonly double Ap;
        public readonly double? Movement;

        public DebugSetAp(double ap, double? movement = null)
        {
            Ap = ap;
            Movement = movement;
        }

        protected override void WriteFields(JsonOut o)
        {
            o.Key("op").Value("setAp").Key("ap").Value(Ap);
            if (Movement.HasValue) o.Key("movement").Value(Movement.Value);
        }
    }

    public sealed class DebugSpawn : DebugCommand
    {
        public readonly string Def;
        public readonly string Target;

        public DebugSpawn(string def, string target)
        {
            Def = def;
            Target = target;
        }

        protected override void WriteFields(JsonOut o) => o.Key("op").Value("spawn").Key("def").Value(Def).Key("target").Value(Target);
    }

    public sealed class DebugForceWave : DebugCommand
    {
        protected override void WriteFields(JsonOut o) => o.Key("op").Value("forceWave");
    }
}
