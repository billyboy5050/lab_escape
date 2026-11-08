#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using LabEscape.Core;

namespace LabEscape.Sim
{
    public interface IBot
    {
        string Name { get; }
        long? Seed { get; }
        /// <summary>The next command, or null to stop playing.</summary>
        Command? Decide(Content c, GameState s);
    }

    /// <summary>Random legal bot: finds crashes, infinite loops and exploits. Draws from a seeded generator.</summary>
    public sealed class RandomBot : IBot
    {
        private readonly Rng _rng;

        public RandomBot(long seed)
        {
            Seed = seed;
            _rng = new Rng(seed);
        }

        public string Name => "random";
        public long? Seed { get; }

        public Command Decide(Content c, GameState s) => _rng.Pick(Actions.LegalCommands(c, s));
    }

    /// <summary>Replays a fixed command script (used for scripted regression fights and golden replays).</summary>
    public sealed class ScriptedBot : IBot
    {
        private readonly IReadOnlyList<Command> _script;
        private int _i;

        public ScriptedBot(IReadOnlyList<Command> script, string name = "scripted")
        {
            _script = script;
            Name = name;
        }

        public string Name { get; }
        public long? Seed => null;

        public Command? Decide(Content c, GameState s) => _i < _script.Count ? _script[_i++] : null;
    }

    /// <summary>
    /// Greedy bot: each action maximises its immediate preview score. A candidate is scored by applying it, then playing out
    /// this round's minion and enemy phases (hazards stay hidden), and valuing the result. One action in ten is a random legal
    /// action from the seeded generator, so a batch is a spread of fights.
    /// </summary>
    public sealed class GreedyBot : IBot
    {
        private readonly Rng _rng;
        private readonly int _randomPerTen;
        private readonly bool _heedCues;

        /// <param name="seed">Seed for the bot's random actions.</param>
        /// <param name="randomPerTen">Random legal actions per ten decisions (the spec's one in ten).</param>
        /// <param name="heedCues">Treat a hazard showing its cue as a threat to the tiles it covers, as a player who has learned the room would.</param>
        public GreedyBot(long seed, int randomPerTen = 1, bool heedCues = true)
        {
            Seed = seed;
            _rng = new Rng(seed);
            _randomPerTen = randomPerTen;
            _heedCues = heedCues;
            Name = heedCues ? "greedy" : "greedy-naive";
        }

        public string Name { get; }
        public long? Seed { get; }

        public Command Decide(Content c, GameState s)
        {
            if (_randomPerTen > 0 && _rng.Chance(_randomPerTen, 10)) return _rng.Pick(Actions.LegalCommands(c, s));
            return Greedy.Choose(c, s, _heedCues).Command;
        }
    }

    public static class Greedy
    {
        private const int Impossible = int.MinValue;

        public static (Command Command, int Score) Choose(Content c, GameState s, bool heedCues = true)
        {
            Command best = new EndTurnCommand();
            var bestScore = ScoreAfterRound(c, s, heedCues);
            foreach (var cmd in CandidateCommands(c, s))
            {
                var score = ScoreCommand(c, s, cmd, heedCues);
                if (score > bestScore)
                {
                    best = cmd;
                    bestScore = score;
                }
            }
            return (best, bestScore);
        }

        private static int ScoreCommand(Content c, GameState s, Command cmd, bool heedCues)
        {
            var r = Engine.Step(c, s, cmd);
            if (!r.Ok) return Impossible;
            if (cmd is SprintCommand && r.State.Outcome == null)
            {
                // Sprint only pays off through the move it allows: score it as its best follow-up move.
                var best = Impossible;
                foreach (var m in Actions.MoveOptions(c, r.State))
                {
                    var r2 = Engine.Step(c, r.State, Actions.MoveCommandFor(m.Path));
                    if (r2.Ok) best = Math.Max(best, ScoreAfterRound(c, r2.State, heedCues));
                }
                return best;
            }
            return ScoreAfterRound(c, r.State, heedCues);
        }

        /// <summary>Candidate commands, pruned to keep the bot fast: duplicate grapple landings and far shield pairs are dropped.</summary>
        public static List<Command> CandidateCommands(Content c, GameState s)
        {
            var output = new List<Command>();
            var p = GameStates.PlayerOf(s);
            if (p == null || s.Phase != Phase.Player || s.Outcome != null) return output;
            foreach (var m in Actions.MoveOptions(c, s)) output.Add(Actions.MoveCommandFor(m.Path));
            var enemies = s.Units.Where(u => u.Team == Team.Enemy && !u.Dead).ToList();
            foreach (var st in Actions.Statuses(c, s))
            {
                if (!st.Usable) continue;
                if (st.Id == "sprint")
                {
                    if (s.Movement == 0 || Actions.MoveOptions(c, s).Count == 0) output.Add(new SprintCommand());
                    continue;
                }
                IEnumerable<TargetOption> opts = st.Options;
                if (st.Id == "barrier_shield") opts = opts.Where(o => Grid.Cheb(o.Target, p.Pos) <= 2 && o.Target2.HasValue && Grid.Cheb(o.Target2.Value, p.Pos) <= 2);
                if (st.Id == "spore_pod") opts = opts.Where(o => enemies.Any(e => Grid.Cheb(e.Pos, o.Target) <= 1));
                if (st.Id == "grapple_hook") opts = OnePerLanding(c, s, p, st.Options);
                foreach (var o in opts) output.Add(Actions.CommandFor(st, o));
            }
            return output;
        }

        /// <summary>Many self-pull targets land on the same tile: keeps one option per landing tile.</summary>
        private static List<TargetOption> OnePerLanding(Content c, GameState s, Unit p, IEnumerable<TargetOption> options)
        {
            var w = Phases.CreateWorld(c, s);
            var seen = new HashSet<Pos>();
            var kept = new List<TargetOption>();
            foreach (var o in options)
            {
                if (o.Mode != "self")
                {
                    kept.Add(o);
                    continue;
                }
                var walk = Abilities.GrappleSelfPath(w, p, o.Target);
                if (seen.Add(walk[walk.Count - 1])) kept.Add(o);
            }
            return kept;
        }

        /// <summary>Plays out this round's minion and enemy phases from the state and values what is left.</summary>
        public static int ScoreAfterRound(Content c, GameState s, bool heedCues = true)
        {
            if (s.Outcome != null) return Terminal(s);
            var after = Previews.SimulateActors(c, s).State;
            var v = ScoreState(c, after);
            if (heedCues && after.Outcome == null)
            {
                var threat = CueThreat(c, after);
                var p = GameStates.PlayerOf(after);
                if (p != null && threat >= p.Hp) v -= 500_000;
                else v -= threat * 100;
            }
            return v;
        }

        /// <summary>Damage the player would take from hazards showing their cue, if they fired on the current positions.</summary>
        public static int CueThreat(Content c, GameState s)
        {
            var primed = s.Hazards.Where(h => h.State == HazardState.Primed).ToList();
            if (primed.Count == 0) return 0;
            var w = Phases.CreateWorld(c, s);
            var dmg = 0;
            foreach (var h in primed)
            {
                var cls = HazardAi.Classify(HazardAi.Targets(w, h));
                if (cls.Player && !cls.Elite) dmg += HazardAi.DamageToPlayer(w, h);
            }
            return dmg;
        }

        private static int Terminal(GameState s) => s.Outcome!.Result == FightResult.Win ? 1_000_000 - s.Round : -1_000_000 + s.Round;

        /// <summary>Integer valuation of a state: the player's health first, then enemy health and future damage, then assets.</summary>
        public static int ScoreState(Content c, GameState s)
        {
            if (s.Outcome != null) return Terminal(s);
            var p = GameStates.PlayerOf(s);
            if (p == null) return -1_000_000;
            var rs = c.Rules.Statuses;
            var board = c.RoomFor(s.Settings.Room).Board;
            var v = p.Hp * 100;
            if (p.Statuses.Poison != null) v -= p.Statuses.Poison.Remaining * rs.Poison.Damage * 90;
            if (p.Statuses.Parasite != null) v -= p.Statuses.Parasite.Remaining * rs.Parasite.Damage * 90;
            foreach (var u in s.Units)
            {
                if (u.Dead) continue;
                if (u.Team == Team.Enemy)
                {
                    v -= 24 * u.Hp + 80;
                    if (u.Def == "medic") v -= 60;
                    if (u.Statuses.Poison != null) v += 12 * Math.Min(u.Hp, u.Statuses.Poison.Remaining * rs.Poison.Damage);
                    if (u.Statuses.Parasite != null) v += 14 * Math.Min(u.Hp, u.Statuses.Parasite.Remaining * rs.Parasite.Damage) + 20;
                    var armor = u.Armor - (u.Statuses.Corrode?.Stacks ?? 0);
                    v -= 30 * Math.Max(0, armor);
                    // Offensive potential: enemies the player can see within range 5.
                    if (Grid.Cheb(u.Pos, p.Pos) <= 5 && board.HasLos(u.Pos, p.Pos)) v += 6;
                }
                else if (u.Kind == UnitKind.Minion)
                {
                    v += 12 * u.Hp + 30 + 6 * (u.Ammo ?? 0) + 10 * (u.Charges ?? 0);
                    if (u.Statuses.Poison != null) v -= 8 * u.Statuses.Poison.Remaining;
                }
            }
            foreach (var o in s.Objects)
            {
                if (o.Kind == ObjectKind.Egg) v += 40 * (o.Host == "floor" ? c.Rules.Eggs.FloorHatchlings : 1) + 4 * (o.Hp ?? 0);
                else if (o.Kind == ObjectKind.Mine) v += 10;
                else if (o.Kind == ObjectKind.Shield) v += 3 * (o.Hp ?? 0);
            }
            return v;
        }
    }
}
