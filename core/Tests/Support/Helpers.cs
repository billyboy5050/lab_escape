#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace LabEscape.Core.Tests
{
    /// <summary>The content, loadouts and set-ups the tests share.</summary>
    public static class Helpers
    {
        /// <summary>The repository's top folder, found from where the tests run.</summary>
        public static readonly string RepoRoot = FindRepoRoot();

        private static readonly ContentFiles DefaultFiles = ContentLoader.FilesFromDirectory(Path.Combine(RepoRoot, "content"));

        public static readonly Content C = ContentLoader.Build(DefaultFiles.DeepClone());

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "content", "rules.json"))) dir = dir.Parent;
            if (dir == null) throw new DirectoryNotFoundException("Could not find content/rules.json above the test folder");
            return dir.FullName;
        }

        /// <summary>Fresh deep copy of the content files, safe to modify.</summary>
        public static ContentFiles FreshFiles() => DefaultFiles.DeepClone();

        public static Content ContentWith(params (string Path, object Value)[] overrides)
        {
            var pairs = overrides.Select(o => new KeyValuePair<string, JToken>(o.Path, o.Value is JToken t ? t : JToken.FromObject(o.Value)));
            return ContentLoader.Build(ContentLoader.WithOverrides(FreshFiles(), pairs));
        }

        public static readonly IReadOnlyList<string> AllAbilities = new[] { "sidearm", "grapple_hook", "proximity_mine", "auto_turret", "scout_drone", "barrier_shield", "acid_spit", "lunge", "spore_pod", "brood_egg", "parasite" };

        public static Loadout LoadoutOf(params string[] abilities) => new Loadout(abilities, new string[0]);

        public static Loadout PresetLoadout(string id)
        {
            var p = C.Presets.First(x => x.Id == id);
            return new Loadout(p.Abilities, p.Upgrades);
        }

        public sealed class UnitSpec
        {
            public string Def = "";
            public string At = "";
            public int? Hp;
            public Statuses? Statuses;
            public bool Arrival;
            public int? Ammo;
            public int? Charges;
            public DroneState? DroneState;
            public int? Armor;
        }

        public static UnitSpec U(string def, string at, int? hp = null, Statuses? statuses = null, bool arrival = false, int? ammo = null, int? charges = null, DroneState? droneState = null, int? armor = null)
        {
            return new UnitSpec { Def = def, At = at, Hp = hp, Statuses = statuses, Arrival = arrival, Ammo = ammo, Charges = charges, DroneState = droneState, Armor = armor };
        }

        /// <summary>An inert test subject: an empty turret never acts and never moves.</summary>
        public static UnitSpec Dummy(string at, int? ammo = 0, Statuses? statuses = null) => U("turret", at, ammo: ammo, statuses: statuses);

        public sealed class ObjectSpec
        {
            public ObjectKind Kind;
            public string At = "";
            public int? Hp;
            public int? Timer;
            public string? Host;
            public string? HatchInto;
            public string? Of;
            public int? Decay;
            public int? Placement;
        }

        public static ObjectSpec O(ObjectKind kind, string at, int? hp = null, int? timer = null, string? host = null, string? hatchInto = null, string? of = null, int? decay = null, int? placement = null)
        {
            return new ObjectSpec { Kind = kind, At = at, Hp = hp, Timer = timer, Host = host, HatchInto = hatchInto, Of = of, Decay = decay, Placement = placement };
        }

        public static ObjectSpec Shield(string at) => O(ObjectKind.Shield, at);
        public static ObjectSpec Mine(string at) => O(ObjectKind.Mine, at);

        public sealed class ScenarioSpec
        {
            public string? Player;
            public int? PlayerHp;
            public List<UnitSpec> Units = new List<UnitSpec>();
            public List<ObjectSpec> Objects = new List<ObjectSpec>();
            public List<string>? Upgrades;
            public List<string>? Abilities;
            public int? Round;
            public int? Ap;
            public int? Movement;
            /// <summary>Waves already spawned. The default (2 of 3) means no wave spawns before round 7 and nobody wins early.</summary>
            public int? WavesSpawned;
            public Content? Content;
        }

        /// <summary>
        /// Builds a state in the player phase with exactly the given units and objects. Units take IDs in the order listed,
        /// after the player (ID 0).
        /// </summary>
        public static GameState Scenario(ScenarioSpec? spec = null)
        {
            spec ??= new ScenarioSpec();
            var c = spec.Content ?? C;
            var s = GameStates.CreateInitial(c, new Loadout(spec.Abilities ?? AllAbilities, spec.Upgrades ?? new List<string>()));
            s.Round = spec.Round ?? 2;
            s.Phase = Phase.Player;
            s.Ap = spec.Ap ?? c.Rules.Turn.Ap;
            s.Movement = spec.Movement ?? c.Rules.Turn.Movement;
            s.WavesSpawned = spec.WavesSpawned ?? 2;
            var p = s.Units[0];
            if (spec.Player != null) p.Pos = Tiles.Parse(spec.Player);
            if (spec.PlayerHp.HasValue) p.Hp = spec.PlayerHp.Value;
            foreach (var u in spec.Units) s.Units.Add(MakeUnit(c, s, u));
            foreach (var o in spec.Objects)
            {
                var obj = new BoardObject { Id = s.NextObjectId++, Kind = o.Kind, Pos = Tiles.Parse(o.At), CreatedRound = s.Round };
                switch (o.Kind)
                {
                    case ObjectKind.Mine:
                        obj.Team = Team.Player;
                        break;
                    case ObjectKind.Shield:
                        obj.Team = Team.Player;
                        obj.Hp = o.Hp ?? 4;
                        obj.MaxHp = 4;
                        obj.Placement = o.Placement ?? s.NextPlacementId++;
                        break;
                    case ObjectKind.Egg:
                        obj.Team = Team.Player;
                        obj.Hp = o.Hp ?? c.Rules.Eggs.Hp;
                        obj.MaxHp = c.Rules.Eggs.Hp;
                        obj.Timer = o.Timer ?? 1;
                        obj.Host = o.Host ?? "floor";
                        obj.HatchInto = o.HatchInto ?? "hatchling";
                        break;
                    case ObjectKind.Corpse:
                        obj.Team = Team.Enemy;
                        obj.Of = o.Of ?? "guard";
                        obj.Decay = o.Decay ?? c.Rules.CorpseDecay;
                        break;
                }
                s.Objects.Add(obj);
            }
            return s;
        }

        public static Unit MakeUnit(Content c, GameState s, UnitSpec spec)
        {
            var def = c.Units[spec.Def];
            var u = new Unit
            {
                Id = s.NextUnitId++,
                Def = spec.Def,
                Team = def.Team,
                Kind = def.Kind,
                Pos = Tiles.Parse(spec.At),
                Hp = spec.Hp ?? def.Hp,
                MaxHp = def.Hp,
                Armor = spec.Armor ?? def.Armor,
                Flying = def.Flying,
                Elite = def.Elite,
                Statuses = spec.Statuses ?? new Statuses(),
                SpawnRound = s.Round,
            };
            if (def.Purge != null) u.Cooldowns["purge"] = 0;
            if (def.Ammo.HasValue)
            {
                u.MaxAmmo = def.Ammo;
                u.Ammo = spec.Ammo ?? def.Ammo;
            }
            if (def.Charges.HasValue)
            {
                u.MaxCharges = def.Charges;
                u.Charges = spec.Charges ?? def.Charges;
                u.DroneState = spec.DroneState ?? DroneState.Deployed;
                u.ReturningPhases = 0;
            }
            if (spec.Arrival) u.ArrivalRound = s.Round;
            return u;
        }

        /// <summary>A shield segment placed directly in a state, for the tests that wall a unit in.</summary>
        public static BoardObject ShieldAt(int id, int x, int y, int placement, int round)
        {
            return new BoardObject { Id = id, Kind = ObjectKind.Shield, Pos = new Pos(x, y), Team = Team.Player, Hp = 99, MaxHp = 99, Placement = placement, CreatedRound = round };
        }

        public static SourceRef Src(string via = "test") => new SourceRef(SourceSide.Player, SourceCategory.Player, SourceKind.Unit, 0, "player", via);
        public static TimedStatus Pin() => new TimedStatus(1, Src());
        public static Statuses PoisonedFor(int remaining, string via = "spore_pod") => new Statuses { Poison = new TimedStatus(remaining, Src(via)) };
        public static Statuses ParasiteFor(int remaining, string via = "spore_pod") => new Statuses { Parasite = new TimedStatus(remaining, Src(via)) };

        /// <summary>Runs a step and fails loudly if the command was illegal.</summary>
        public static StepResult Run(GameState s, Command cmd, Content? c = null)
        {
            var r = Engine.Step(c ?? C, s, cmd);
            if (!r.Ok) throw new InvalidOperationException($"Command rejected: {r.Error}");
            return r;
        }

        public sealed class WorldRun
        {
            public World W = null!;
            public GameState S = null!;
            public List<GameEvent> Events = null!;
        }

        /// <summary>Runs a function inside a root action on a copy of the state, for testing rules directly.</summary>
        public static WorldRun InWorld(GameState s, Action<World> fn, Content? c = null)
        {
            var copy = s.Clone();
            var w = Phases.CreateWorld(c ?? C, copy);
            try
            {
                w.Root("test", null, () => fn(w));
            }
            catch (FightOver)
            {
                // The state carries the outcome.
            }
            return new WorldRun { W = w, S = copy, Events = w.Events };
        }

        public static Pos T(string name) => Tiles.Parse(name);
        public static string Name(Pos p) => Tiles.Name(p);

        public static Unit? UnitAt(GameState s, string tile)
        {
            var p = Tiles.Parse(tile);
            return s.Units.FirstOrDefault(u => u.Pos == p && !u.Docked);
        }

        public static Unit? UnitById(GameState s, int id) => s.Units.FirstOrDefault(u => u.Id == id);

        public static int DamageTo(IEnumerable<GameEvent> events, int id)
        {
            return EventsOf<DamageDealt>(events).Where(e => e.Target.Kind == EntityKind.Unit && e.Target.Id == id).Sum(e => e.Amount);
        }

        public static List<E> EventsOf<E>(IEnumerable<GameEvent> events) where E : GameEvent => events.OfType<E>().ToList();

        public static Command Sidearm(string target) => new AbilityCommand("sidearm", target);
        public static Command Ability(string ability, string target, string? target2 = null, string? mode = null) => new AbilityCommand(ability, target, target2, mode);
        public static Command Move(params string[] path) => new MoveCommand(path);
        public static readonly Command EndTurn = new EndTurnCommand();

        public static ActionStatus Action(Content c, GameState s, string id) => Actions.Statuses(c, s).First(a => a.Id == id);

        /// <summary>Canonical JSON of a state, for comparing two states whatever their field order.</summary>
        public static string CanonicalState(GameState s) => Hash.CanonicalJson(JsonIn.Parse(s.ToJson()));
    }
}
