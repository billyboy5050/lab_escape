#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace LabEscape.Core
{
    public sealed class StepResult
    {
        public bool Ok;
        public string? Error;
        public GameState State = null!;
        public List<GameEvent> Events = new List<GameEvent>();
        /// <summary>One frame per root action, each with a state snapshot. Only filled when frames were requested.</summary>
        public List<Frame> Frames = new List<Frame>();
        /// <summary>The command spent AP or set something off, so free movement before it can no longer be undone.</summary>
        public bool Commits;
    }

    public sealed class NewGameResult
    {
        public GameState State = null!;
        public List<GameEvent> Events = new List<GameEvent>();
        public List<Frame> Frames = new List<Frame>();
    }

    public static class Engine
    {
        /// <summary>
        /// The engine's single entry point: a pure function from a state and a command to a new state and the events it produced.
        /// The input state is never modified. Illegal commands return Ok = false.
        /// </summary>
        public static StepResult Step(Content c, GameState s0, Command cmd, bool frames = false)
        {
            var s = s0.Clone();
            var w = Phases.CreateWorld(c, s, frames);
            try
            {
                ApplyCommand(w, cmd);
            }
            catch (IllegalCommand e)
            {
                return new StepResult { Ok = false, Error = e.Message, State = s0, Commits = false };
            }
            catch (FightOver)
            {
                // The state carries the outcome.
            }
            w.CloseFrame("tail", null);
            var commits = !(cmd is MoveCommand) || w.Triggered;
            return new StepResult { Ok = true, State = s, Events = w.Events, Frames = w.Frames ?? new List<Frame>(), Commits = commits };
        }

        /// <summary>Creates a fight for a loadout and runs the start of round 1 (wave 1 spawns, the briefing line plays).</summary>
        public static NewGameResult NewGame(Content c, Loadout loadout, FightSettings? settings = null, bool frames = false)
        {
            var problems = LoadoutProblems(c, loadout);
            if (problems.Count > 0) throw new ArgumentException("Invalid loadout: " + string.Join("; ", problems));
            var s = GameStates.CreateInitial(c, loadout, settings);
            var w = Phases.CreateWorld(c, s, frames);
            try
            {
                Phases.StartRound(w, 1);
            }
            catch (FightOver)
            {
                // The state carries the outcome.
            }
            w.CloseFrame("tail", null);
            return new NewGameResult { State = s, Events = w.Events, Frames = w.Frames ?? new List<Frame>() };
        }

        /// <summary>Loadout rules: up to N of the pool's abilities, up to M upgrades whose required abilities are present, no duplicates.</summary>
        public static List<string> LoadoutProblems(Content c, Loadout l)
        {
            var p = new List<string>();
            var max = c.Rules.Loadout;
            if (l.Abilities.Distinct(StringComparer.Ordinal).Count() != l.Abilities.Count) p.Add("Duplicate ability");
            if (l.Upgrades.Distinct(StringComparer.Ordinal).Count() != l.Upgrades.Count) p.Add("Duplicate upgrade");
            if (l.Abilities.Count > max.Abilities) p.Add($"More than {max.Abilities} abilities");
            if (l.Upgrades.Count > max.Upgrades) p.Add($"More than {max.Upgrades} upgrades");
            foreach (var a in l.Abilities)
            {
                if (!c.Abilities.ContainsKey(a)) p.Add($"Unknown ability \"{a}\"");
            }
            foreach (var u in l.Upgrades)
            {
                if (!c.Upgrades.TryGetValue(u, out var def)) p.Add($"Unknown upgrade \"{u}\"");
                else if (!ContentLoader.UpgradeAllowed(def.Requires, l.Abilities)) p.Add($"{def.Name} needs {DescribeRequires(c, def.Requires)}");
            }
            return p;
        }

        public static string DescribeRequires(Content c, RequiresDef r)
        {
            string Name(string id) => c.Abilities.TryGetValue(id, out var a) ? a.Name : id;
            var parts = new List<string>();
            if (r.All != null && r.All.Count > 0) parts.Add(string.Join(" and ", r.All.Select(Name)));
            if (r.Any != null && r.Any.Count > 0) parts.Add(string.Join(" or ", r.Any.Select(Name)));
            return string.Join(", and ", parts);
        }

        private static Pos Tile(string? name, string what)
        {
            if (!Tiles.IsName(name)) throw new IllegalCommand($"{what} is not a tile");
            return Tiles.Parse(name!);
        }

        private static void ApplyCommand(World w, Command cmd)
        {
            var s = w.S;
            if (s.Outcome != null) throw new IllegalCommand("The fight is over");
            if (cmd is DebugCommand debug)
            {
                ApplyDebug(w, debug);
                return;
            }
            if (s.Phase != Phase.Player) throw new IllegalCommand($"Not the player phase ({Names.Of(s.Phase)})");
            var p = w.Player;
            if (p == null) throw new IllegalCommand("The player is not on the board");
            switch (cmd)
            {
                case MoveCommand move:
                    ApplyMove(w, p, move);
                    return;
                case SprintCommand _:
                {
                    var sp = w.C.Rules.Sprint;
                    if (s.Ap < sp.Ap) throw new IllegalCommand("Not enough AP");
                    s.Ap -= sp.Ap;
                    s.Movement += sp.Movement;
                    s.Committed = true;
                    w.Root("sprint", 0, () => w.Emit(new Sprinted { Movement = sp.Movement }));
                    return;
                }
                case AbilityCommand ability:
                    ApplyAbility(w, p, ability);
                    return;
                case ReloadCommand reload:
                {
                    var u = Utility(w, "reload");
                    var t = Tile(reload.Target, "Target");
                    var turret = w.UnitAt(t);
                    if (turret == null || turret.Team != Team.Player || !turret.MaxAmmo.HasValue) throw new IllegalCommand("No turret there");
                    if (Grid.Cheb(turret.Pos, p.Pos) > u.Range!.Value) throw new IllegalCommand("Turret out of range");
                    if ((turret.Ammo ?? 0) >= turret.MaxAmmo.Value) throw new IllegalCommand("Turret is already full");
                    s.Ap -= u.Ap;
                    s.Committed = true;
                    w.Root("reload", 0, () =>
                    {
                        turret.Ammo = turret.MaxAmmo;
                        w.Emit(new Reloaded { Id = turret.Id, Ammo = turret.Ammo.Value });
                    });
                    return;
                }
                case PickUpMineCommand pickUp:
                {
                    var u = Utility(w, "pick_up_mine");
                    var t = Tile(pickUp.Target, "Target");
                    var mine = w.MineAt(t);
                    if (mine == null) throw new IllegalCommand("No mine there");
                    if (Grid.Cheb(t, p.Pos) > u.Range!.Value) throw new IllegalCommand("Mine is not adjacent");
                    s.Ap -= u.Ap;
                    s.Committed = true;
                    w.Root("pick up mine", 0, () => w.RemoveObject(mine, RemovalReason.Pickup));
                    return;
                }
                case RedeployCommand redeploy:
                {
                    var u = Utility(w, "redeploy");
                    var drone = w.Unit(redeploy.Drone);
                    if (drone == null || !drone.Docked) throw new IllegalCommand("That drone is not docked");
                    var t = Tile(redeploy.Target, "Target");
                    if (Grid.Cheb(t, p.Pos) != 1 || !w.IsFree(t)) throw new IllegalCommand("Redeploy needs a free tile adjacent to you");
                    s.Ap -= u.Ap;
                    s.Committed = true;
                    w.Root("redeploy", 0, () =>
                    {
                        drone.Pos = t;
                        drone.DroneState = DroneState.Deployed;
                        drone.ReturningPhases = 0;
                        drone.Charges = drone.MaxCharges = w.DroneCharges(w.C.Units[drone.Def].Charges ?? 1);
                        w.Emit(new DroneRedeployed { Id = drone.Id, Pos = t });
                    });
                    return;
                }
                case EndTurnCommand _:
                    s.Committed = true;
                    Phases.EndTurn(w);
                    return;
                default:
                    throw new ArgumentException($"Unknown command {cmd.Type}");
            }
        }

        private static void ApplyMove(World w, Unit p, MoveCommand cmd)
        {
            var s = w.S;
            if (cmd.Path == null || cmd.Path.Count == 0) throw new IllegalCommand("Move needs a path");
            var path = cmd.Path.Select(t => Tile(t, "Path step")).ToList();
            if (path.Count > s.Movement) throw new IllegalCommand("Not enough movement");
            var prev = p.Pos;
            foreach (var t in path)
            {
                if (Grid.Manhattan(prev, t) != 1) throw new IllegalCommand("Each step must be to an orthogonally adjacent tile");
                if (!w.IsFree(t, p.Id)) throw new IllegalCommand($"The path is blocked at {Tiles.Name(t)}");
                prev = t;
            }
            s.Movement -= path.Count;
            w.Root("move", 0, () => w.MoveAlong(p, path, MoveMode.Walk));
            if (w.Triggered) s.Committed = true;
        }

        private static void ApplyAbility(World w, Unit p, AbilityCommand cmd)
        {
            var s = w.S;
            if (!w.C.Abilities.TryGetValue(cmd.Ability, out var def) || !Abilities.All.TryGetValue(cmd.Ability, out var impl)) throw new IllegalCommand($"Unknown ability \"{cmd.Ability}\"");
            if (!s.Loadout.Abilities.Contains(cmd.Ability)) throw new IllegalCommand($"{def.Name} is not in the loadout");
            if (s.Ap < def.Ap) throw new IllegalCommand("Not enough AP");
            var want = new TargetOption(
                Tile(cmd.Target, "Target"),
                string.IsNullOrEmpty(cmd.Target2) ? (Pos?)null : Tile(cmd.Target2, "Second target"),
                cmd.Mode);
            var opt = Abilities.MatchOption(impl.Options(w, p), want);
            if (opt == null) throw new IllegalCommand(impl.Blocked?.Invoke(w) ?? $"Not a valid target for {def.Name}");
            s.Ap -= def.Ap;
            s.Committed = true;
            w.Root(def.Name, 0, () =>
            {
                w.Emit(new AbilityUsed { Ability = cmd.Ability, Actor = 0, Target = opt.Target, Target2 = opt.Target2, Mode = opt.Mode, Ap = def.Ap });
                impl.Resolve(w, p, opt);
            });
        }

        private static UtilityDef Utility(World w, string id)
        {
            if (!w.C.Utilities.TryGetValue(id, out var u)) throw new IllegalCommand($"Unknown action {id}");
            if (!string.IsNullOrEmpty(u.RequiresAbility) && !w.S.Loadout.Abilities.Contains(u.RequiresAbility!))
            {
                var needed = w.C.Abilities.TryGetValue(u.RequiresAbility!, out var ability) ? ability.Name : "undefined";
                throw new IllegalCommand($"{u.Name} needs {needed}");
            }
            if (w.S.Ap < u.Ap) throw new IllegalCommand("Not enough AP");
            return u;
        }

        private static void ApplyDebug(World w, DebugCommand cmd)
        {
            var s = w.S;
            switch (cmd)
            {
                case DebugSetHp setHp:
                {
                    var u = w.Unit(setHp.Unit);
                    if (u == null) throw new IllegalCommand("No such unit");
                    w.Root("debug: set HP", null, () =>
                    {
                        u.Hp = (int)Math.Max(1, Math.Min(u.MaxHp, Math.Floor(setHp.Value)));
                        w.Emit(new DebugEvent { Op = "setHp", Detail = $"{u.Def} {u.Id} HP set to {u.Hp}" });
                    });
                    return;
                }
                case DebugSetAp setAp:
                    w.Root("debug: set AP", null, () =>
                    {
                        s.Ap = (int)Math.Max(0, Math.Floor(setAp.Ap));
                        if (setAp.Movement.HasValue) s.Movement = (int)Math.Max(0, Math.Floor(setAp.Movement.Value));
                        w.Emit(new DebugEvent { Op = "setAp", Detail = $"AP {s.Ap}, movement {s.Movement}" });
                    });
                    return;
                case DebugSpawn spawn:
                {
                    if (!w.C.Units.ContainsKey(spawn.Def) || spawn.Def == "player") throw new IllegalCommand($"Cannot spawn \"{spawn.Def}\"");
                    var want = Tile(spawn.Target, "Target");
                    w.Root("debug: spawn", null, () =>
                    {
                        var at = w.FindSpawnTile(want);
                        if (!at.HasValue) return;
                        var u = w.SpawnUnit(spawn.Def, at.Value, "debug");
                        w.Emit(new DebugEvent { Op = "spawn", Detail = $"Spawned {u.Def} {u.Id} at {Tiles.Name(at.Value)}" });
                    });
                    return;
                }
                case DebugForceWave _:
                {
                    if (s.WavesSpawned >= w.C.Waves.Count) throw new IllegalCommand("No waves left");
                    w.Root("debug: force wave", null, () =>
                    {
                        var idx = s.WavesSpawned;
                        if (s.Settings.MaxWaves.HasValue && s.Settings.MaxWaves.Value <= idx) s.Settings.MaxWaves = idx + 1;
                        Waves.Spawn(w, idx);
                        w.Emit(new DebugEvent { Op = "forceWave", Detail = $"Forced wave {idx + 1}" });
                    });
                    return;
                }
            }
        }
    }
}
