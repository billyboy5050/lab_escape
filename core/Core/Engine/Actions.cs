#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace LabEscape.Core
{
    public enum ActionKind { Ability, Utility }

    public sealed class ActionStatus
    {
        /// <summary>Ability id, or a utility id: sprint, reload, pick_up_mine, redeploy.</summary>
        public string Id = "";
        public string Name = "";
        public int Ap;
        public ActionKind Kind;
        public bool Usable;
        /// <summary>Why it cannot be used right now: not enough AP, no valid target, or a cap reached.</summary>
        public string? Reason;
        public List<TargetOption> Options = new List<TargetOption>();
        /// <summary>Redeploy only: the docked drone it would place.</summary>
        public int? Drone;
    }

    public sealed class MoveOption
    {
        public Pos Tile;
        public List<Pos> Path = new List<Pos>();
        /// <summary>Mines the default path steps on (the path avoids them when it can).</summary>
        public int Mines;
    }

    public static class Actions
    {
        private const int NoCost = 9999;

        /// <summary>Every action the player could take this phase, with its valid targets or the reason it is disabled.</summary>
        public static List<ActionStatus> Statuses(Content c, GameState s)
        {
            var w = Phases.CreateWorld(c, s);
            var p = w.Player;
            var output = new List<ActionStatus>();
            var phaseOk = s.Phase == Phase.Player && s.Outcome == null && p != null;
            foreach (var id in s.Loadout.Abilities)
            {
                var def = c.Abilities[id];
                var impl = Abilities.All[id];
                var st = new ActionStatus { Id = id, Name = def.Name, Ap = def.Ap, Kind = ActionKind.Ability };
                if (!phaseOk) st.Reason = "Not your turn";
                else
                {
                    var blocked = impl.Blocked?.Invoke(w);
                    st.Options = blocked != null ? new List<TargetOption>() : impl.Options(w, p!);
                    if (s.Ap < def.Ap) st.Reason = $"Not enough AP (needs {def.Ap})";
                    else if (blocked != null) st.Reason = blocked;
                    else if (st.Options.Count == 0) st.Reason = "No valid target";
                    else st.Usable = true;
                }
                output.Add(st);
            }
            // Sprint. The AP cost is rules.sprint, the value the engine charges; validation keeps the utility entry equal to it.
            {
                var ap = c.Rules.Sprint.Ap;
                var st = new ActionStatus { Id = "sprint", Name = c.Utilities["sprint"].Name, Ap = ap, Kind = ActionKind.Utility };
                if (!phaseOk) st.Reason = "Not your turn";
                else if (s.Ap < ap) st.Reason = $"Not enough AP (needs {ap})";
                else st.Usable = true;
                output.Add(st);
            }
            foreach (var id in new[] { "reload", "pick_up_mine", "redeploy" })
            {
                var u = c.Utilities[id];
                if (!string.IsNullOrEmpty(u.RequiresAbility) && !s.Loadout.Abilities.Contains(u.RequiresAbility!)) continue;
                var st = new ActionStatus { Id = id, Name = u.Name, Ap = u.Ap, Kind = ActionKind.Utility };
                if (phaseOk)
                {
                    if (id == "reload")
                    {
                        st.Options = w.PlayerSide()
                            .Where(t => t.MaxAmmo.HasValue && (t.Ammo ?? 0) < t.MaxAmmo.Value && Grid.Cheb(t.Pos, p!.Pos) <= u.Range!.Value)
                            .Select(t => new TargetOption(t.Pos))
                            .ToList();
                    }
                    else if (id == "pick_up_mine")
                    {
                        st.Options = s.Objects
                            .Where(o => o.Kind == ObjectKind.Mine && Grid.Cheb(o.Pos, p!.Pos) <= u.Range!.Value)
                            .Select(o => new TargetOption(o.Pos))
                            .ToList();
                    }
                    else
                    {
                        var docked = s.Units.FirstOrDefault(x => x.Docked && !x.Dead);
                        if (docked != null)
                        {
                            st.Drone = docked.Id;
                            st.Options = w.B.Neighbors8(p!.Pos).Where(t => w.IsFree(t)).Select(t => new TargetOption(t)).ToList();
                        }
                    }
                }
                if (!phaseOk) st.Reason = "Not your turn";
                else if (s.Ap < u.Ap) st.Reason = $"Not enough AP (needs {u.Ap})";
                else if (st.Options.Count == 0) st.Reason = id == "redeploy" && !st.Drone.HasValue ? "No drone is docked" : "No valid target";
                else st.Usable = true;
                output.Add(st);
            }
            return output;
        }

        /// <summary>
        /// Tiles the player can reach with the movement left, each with a default path: fewest mines stepped on, then fewest
        /// steps. Ground movement is 4-direction and cannot pass through units, shields, eggs or terrain.
        /// </summary>
        public static List<MoveOption> MoveOptions(Content c, GameState s)
        {
            var empty = new List<MoveOption>();
            if (s.Phase != Phase.Player || s.Outcome != null) return empty;
            var w = Phases.CreateWorld(c, s);
            var p = w.Player;
            if (p == null || s.Movement <= 0) return empty;
            var b = w.B;
            var n = b.Width * b.Height;
            var maxK = s.Movement;
            // best[k][i] = fewest mines to stand on tile i after exactly k steps; parent pointers per layer.
            var best = new int[maxK + 1][];
            var parent = new int[maxK + 1][];
            for (var k = 0; k <= maxK; k++)
            {
                best[k] = new int[n];
                parent[k] = new int[n];
                for (var i = 0; i < n; i++)
                {
                    best[k][i] = NoCost;
                    parent[k][i] = -1;
                }
            }
            best[0][b.Idx(p.Pos)] = 0;
            for (var k = 0; k < maxK; k++)
            {
                for (var i = 0; i < n; i++)
                {
                    var m = best[k][i];
                    if (m == NoCost) continue;
                    var from = b.PosOf(i);
                    foreach (var d in Grid.Dirs4)
                    {
                        var q = Grid.Add(from, d);
                        if (!b.InBounds(q) || !w.IsFree(q, p.Id)) continue;
                        var qi = b.Idx(q);
                        var cost = m + (w.MineAt(q) != null ? 1 : 0);
                        if (cost < best[k + 1][qi])
                        {
                            best[k + 1][qi] = cost;
                            parent[k + 1][qi] = i;
                        }
                    }
                }
            }
            var output = new List<MoveOption>();
            for (var i = 0; i < n; i++)
            {
                if (i == b.Idx(p.Pos)) continue;
                var bestK = -1;
                for (var k = 1; k <= maxK; k++)
                {
                    if (best[k][i] == NoCost) continue;
                    if (bestK < 0 || best[k][i] < best[bestK][i]) bestK = k;
                }
                if (bestK < 0) continue;
                var path = new List<Pos>();
                var cur = i;
                for (var k = bestK; k > 0; k--)
                {
                    path.Add(b.PosOf(cur));
                    cur = parent[k][cur];
                }
                path.Reverse();
                output.Add(new MoveOption { Tile = b.PosOf(i), Path = path, Mines = best[bestK][i] });
            }
            return output;
        }

        /// <summary>Converts an action and target option into a command.</summary>
        public static Command CommandFor(ActionStatus st, TargetOption opt)
        {
            switch (st.Id)
            {
                case "sprint":
                    return new SprintCommand();
                case "reload":
                    return new ReloadCommand(Tiles.Name(opt.Target));
                case "pick_up_mine":
                    return new PickUpMineCommand(Tiles.Name(opt.Target));
                case "redeploy":
                    return new RedeployCommand(st.Drone!.Value, Tiles.Name(opt.Target));
                default:
                    return new AbilityCommand(st.Id, Tiles.Name(opt.Target), opt.Target2.HasValue ? Tiles.Name(opt.Target2.Value) : null, string.IsNullOrEmpty(opt.Mode) ? null : opt.Mode);
            }
        }

        public static Command MoveCommandFor(IEnumerable<Pos> path) => new MoveCommand(path.Select(Tiles.Name));

        /// <summary>Every legal command in this state (used by bots).</summary>
        public static List<Command> LegalCommands(Content c, GameState s)
        {
            var output = new List<Command>();
            if (s.Phase != Phase.Player || s.Outcome != null) return output;
            foreach (var m in MoveOptions(c, s)) output.Add(MoveCommandFor(m.Path));
            foreach (var st in Statuses(c, s))
            {
                if (!st.Usable) continue;
                if (st.Id == "sprint") output.Add(new SprintCommand());
                else
                {
                    foreach (var opt in st.Options) output.Add(CommandFor(st, opt));
                }
            }
            output.Add(new EndTurnCommand());
            return output;
        }

        public static Unit? PlayerUnit(GameState s) => s.Units.FirstOrDefault(u => u.Id == 0);
    }
}
