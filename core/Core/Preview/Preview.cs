#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace LabEscape.Core
{
    public enum LineKind { Shot, Chain, Reflect, Charge, Melee, Heal }

    public sealed class HitPreview
    {
        public EntityRef Ref;
        public string Def = "";
        public Team Side;
        public Pos Pos;
        /// <summary>Damage after armor, summed over every hit in the action.</summary>
        public int Damage;
        public int Absorbed;
        public bool Killed;
        public List<StatusId> Statuses = new List<StatusId>();
        public int Healed;
        /// <summary>A player-side unit or object harmed by the player's own side.</summary>
        public bool Friendly;
    }

    public sealed class LinePreview
    {
        public Pos From;
        public Pos To;
        public LineKind Kind;
    }

    public sealed class MovePreview
    {
        public int Id;
        public Pos From;
        public Pos To;
        public List<Pos> Path = new List<Pos>();
    }

    public class EffectSummary
    {
        public List<HitPreview> Hits = new List<HitPreview>();
        public List<Pos> AreaTiles = new List<Pos>();
        public List<LinePreview> Lines = new List<LinePreview>();
        public List<MovePreview> Moves = new List<MovePreview>();
        public List<BoardObject> Placed = new List<BoardObject>();
        public List<Unit> Spawned = new List<Unit>();
        public List<Pos> MinesTriggered = new List<Pos>();
        public bool FriendlyFire;
        public bool PlayerHarmed;
    }

    public sealed class CommandPreview : EffectSummary
    {
        public bool Ok;
        public string? Error;
        public GameState? State;
        public List<GameEvent> Events = new List<GameEvent>();
        public Outcome? Outcome;
        /// <summary>Enemy and minion intents recomputed after the action, when requested.</summary>
        public IntentReport? Intents;
    }

    public sealed class Intent
    {
        public int Id;
        public string Def = "";
        public Team Team;
        /// <summary>In its arrival round the unit only moves; its destination is shown, not an attack.</summary>
        public bool Arrival;
        public Pos From;
        public Pos To;
        public List<Pos> Path = new List<Pos>();
        public List<LinePreview> Lines = new List<LinePreview>();
        public List<Pos> Tiles = new List<Pos>();
        public List<HitPreview> Hits = new List<HitPreview>();
        /// <summary>The unit dies before its turn comes (killed by someone acting earlier).</summary>
        public bool DiesFirst;
        /// <summary>Docked drones and units with nothing to do.</summary>
        public bool Idle;
        public string Label = "";
    }

    public sealed class IntentReport
    {
        public List<Intent> Intents = new List<Intent>();
        /// <summary>Damage the player takes from all minion and enemy actions this round, if the turn ended now.</summary>
        public int PlayerDamage;
        public bool PlayerDies;
        public Outcome? Outcome;
        public List<Frame> Frames = new List<Frame>();
    }

    public sealed class SimulatedActors
    {
        public GameState State = null!;
        public List<GameEvent> Events = new List<GameEvent>();
    }

    public static class Previews
    {
        private static readonly StatusId[] Harmful = { StatusId.Poison, StatusId.Parasite, StatusId.Corrode, StatusId.Pinned, StatusId.Slowed };

        private sealed class UnitInfo
        {
            public string Def = "";
            public Team Team;
            public Pos Pos;
        }

        /// <summary>Summarises a list of events into what the UI draws: hits, areas, lines, moves and friendly fire.</summary>
        public static T SummarizeEffects<T>(IReadOnlyList<GameEvent> events, GameState before, T summary) where T : EffectSummary
        {
            var hits = new OrderedMap<string, HitPreview>(StringComparer.Ordinal);
            var moves = new OrderedMap<int, MovePreview>();
            var unitInfo = new Dictionary<int, UnitInfo>();
            foreach (var u in before.Units) unitInfo[u.Id] = new UnitInfo { Def = u.Def, Team = u.Team, Pos = u.Pos };

            HitPreview Hit(EntityRef target, string def, Team side, Pos pos)
            {
                var key = Names.Of(target.Kind) + target.Id;
                if (!hits.TryGetValue(key, out var h))
                {
                    h = new HitPreview { Ref = target, Def = def, Side = side, Pos = pos };
                    hits[key] = h;
                }
                return h;
            }

            foreach (var ev in events)
            {
                switch (ev)
                {
                    case DamageDealt e:
                    {
                        var h = Hit(e.Target, e.TargetDef, e.TargetSide, e.Pos);
                        h.Damage += e.Amount;
                        h.Absorbed += e.Absorbed;
                        if (e.Killed) h.Killed = true;
                        if (e.TargetSide == Team.Player && e.Source.Side == SourceSide.Player) h.Friendly = true;
                        break;
                    }
                    case StatusApplied e:
                    {
                        if (!unitInfo.TryGetValue(e.Target, out var info)) break;
                        var h = Hit(EntityRef.Unit(e.Target), info.Def, info.Team, info.Pos);
                        if (!h.Statuses.Contains(e.Status)) h.Statuses.Add(e.Status);
                        if (info.Team == Team.Player && e.Source.Side == SourceSide.Player && Harmful.Contains(e.Status)) h.Friendly = true;
                        break;
                    }
                    case Healed e:
                    {
                        if (unitInfo.TryGetValue(e.Target, out var info)) Hit(EntityRef.Unit(e.Target), info.Def, info.Team, info.Pos).Healed += e.Amount;
                        break;
                    }
                    case AreaEffect e:
                        foreach (var t in e.Tiles)
                        {
                            if (!summary.AreaTiles.Contains(t)) summary.AreaTiles.Add(t);
                        }
                        break;
                    case ProjectileFired e:
                        if (e.Path.Count > 0) summary.Lines.Add(new LinePreview { From = e.From, To = e.Path[e.Path.Count - 1], Kind = e.Reflected == true ? LineKind.Reflect : LineKind.Shot });
                        break;
                    case ChainArc e:
                        summary.Lines.Add(new LinePreview { From = e.From, To = e.To, Kind = LineKind.Chain });
                        break;
                    case Charged e:
                        summary.Lines.Add(new LinePreview { From = e.From, To = e.To, Kind = LineKind.Charge });
                        break;
                    case UnitMoved e:
                    {
                        if (moves.TryGetValue(e.Id, out var m))
                        {
                            m.To = e.To;
                            m.Path.AddRange(e.Path);
                        }
                        else moves[e.Id] = new MovePreview { Id = e.Id, From = e.From, To = e.To, Path = new List<Pos>(e.Path) };
                        if (unitInfo.TryGetValue(e.Id, out var info)) info.Pos = e.To;
                        break;
                    }
                    case UnitSpawned e:
                        summary.Spawned.Add(e.Unit);
                        unitInfo[e.Unit.Id] = new UnitInfo { Def = e.Unit.Def, Team = e.Unit.Team, Pos = e.Unit.Pos };
                        break;
                    case ObjectPlaced e:
                        summary.Placed.Add(e.Object);
                        break;
                    case ObjectTriggered e:
                        summary.MinesTriggered.Add(e.Pos);
                        break;
                }
            }
            summary.Hits = hits.Values.ToList();
            summary.Moves = moves.Values.ToList();
            summary.FriendlyFire = summary.Hits.Any(h => h.Friendly);
            summary.PlayerHarmed = summary.Hits.Any(h => h.Ref.Kind == EntityKind.Unit && h.Ref.Id == 0 && (h.Damage > 0 || h.Statuses.Any(s => Harmful.Contains(s))));
            return summary;
        }

        public static EffectSummary SummarizeEffects(IReadOnlyList<GameEvent> events, GameState before) => SummarizeEffects(events, before, new EffectSummary());

        /// <summary>Dry-runs a player command on a cloned state with the same engine code that resolves it.</summary>
        public static CommandPreview PreviewCommand(Content c, GameState s, Command cmd, bool intents = false)
        {
            var r = Engine.Step(c, s, cmd);
            if (!r.Ok)
            {
                var failed = new CommandPreview { Ok = false, Error = r.Error };
                return SummarizeEffects(new List<GameEvent>(), s, failed);
            }
            var output = new CommandPreview { Ok = true, State = r.State, Events = r.Events, Outcome = r.State.Outcome };
            SummarizeEffects(r.Events, s, output);
            if (intents && r.State.Outcome == null && r.State.Phase == Phase.Player) output.Intents = ComputeIntents(c, r.State);
            return output;
        }

        /// <summary>
        /// Runs the minion and enemy phases on a copy of the state, without frames: the cheap form of intents, used by bots to
        /// score an action by what follows it this round.
        /// </summary>
        public static SimulatedActors SimulateActors(Content c, GameState s)
        {
            var sim = s.Clone();
            var w = Phases.CreateWorld(c, sim);
            if (s.Phase == Phase.Player && s.Outcome == null)
            {
                try
                {
                    Phases.RunMinionPhase(w);
                    Phases.RunEnemyPhase(w);
                }
                catch (FightOver)
                {
                    // The simulated state carries the outcome.
                }
            }
            return new SimulatedActors { State = sim, Events = w.Events };
        }

        /// <summary>
        /// What every minion and enemy will do if the player ends the turn now: the minion and enemy phases are run on a cloned
        /// state, so each intent already accounts for the units that act before it. The environment phase (hazards, statuses,
        /// eggs) is not run, so hazards stay hidden.
        /// </summary>
        public static IntentReport ComputeIntents(Content c, GameState s)
        {
            if (s.Phase != Phase.Player || s.Outcome != null) return new IntentReport();
            var sim = s.Clone();
            var w = Phases.CreateWorld(c, sim, frames: true);
            var actors = s.Units.Where(u => !u.Dead && u.Kind != UnitKind.Player).Select(u => u.Id).ToList();
            Outcome? outcome = null;
            try
            {
                Phases.RunMinionPhase(w);
                Phases.RunEnemyPhase(w);
            }
            catch (FightOver e)
            {
                outcome = e.Outcome;
            }
            w.CloseFrame("tail", null);
            var frames = w.Frames ?? new List<Frame>();
            var intents = new List<Intent>();
            var playerDamage = 0;
            foreach (var id in actors)
            {
                var u = s.Units.First(x => x.Id == id);
                var frameIndex = frames.FindIndex(f => f.Actor == id);
                var docked = u.Docked;
                if (frameIndex < 0)
                {
                    // An actor that reaches its turn always gets a frame, even an empty one (a turret with no ammo), so none means
                    // it was docked, cut off by the end of the fight, or removed before its turn. Read that from the simulated state
                    // rather than from the missing frame, so a unit that is still standing is never reported as dead.
                    var alive = sim.Units.Any(x => x.Id == id && !x.Dead);
                    var diesFirst = !docked && outcome == null && !alive;
                    intents.Add(new Intent
                    {
                        Id = id,
                        Def = u.Def,
                        Team = u.Team,
                        Arrival = u.ArrivalRound == s.Round,
                        From = u.Pos,
                        To = u.Pos,
                        DiesFirst = diesFirst,
                        Idle = true,
                        Label = docked ? "Docked" : outcome != null ? "Does not act (the fight ends first)" : diesFirst ? "Dies before acting" : "Holds",
                    });
                    continue;
                }
                var frame = frames[frameIndex];
                var prevState = frameIndex > 0 ? frames[frameIndex - 1].State : s;
                var sum = SummarizeEffects(frame.Events, prevState);
                var mv = sum.Moves.FirstOrDefault(m => m.Id == id);
                var hits = sum.Hits;
                foreach (var h in hits)
                {
                    if (h.Ref.Kind == EntityKind.Unit && h.Ref.Id == 0) playerDamage += h.Damage;
                }
                var arrival = u.ArrivalRound == s.Round;
                intents.Add(new Intent
                {
                    Id = id,
                    Def = u.Def,
                    Team = u.Team,
                    Arrival = arrival,
                    From = u.Pos,
                    To = mv != null ? mv.To : u.Pos,
                    Path = mv != null ? mv.Path : new List<Pos>(),
                    Lines = sum.Lines,
                    Tiles = sum.AreaTiles,
                    Hits = hits,
                    DiesFirst = false,
                    Idle = mv == null && hits.Count == 0 && sum.Lines.Count == 0,
                    Label = DescribeIntent(mv, hits, frame.Events, arrival),
                });
            }
            return new IntentReport { Intents = intents, PlayerDamage = playerDamage, PlayerDies = outcome?.Result == FightResult.Lose, Outcome = outcome, Frames = frames };
        }

        private static string DescribeIntent(MovePreview? mv, List<HitPreview> hits, List<GameEvent> events, bool arrival)
        {
            string Name(string def, int id) => id == 0 ? "you" : $"{def} {id}";
            var parts = new List<string>();
            if (mv != null) parts.Add($"moves to {Tiles.Name(mv.To)}");
            if (arrival) return parts.Count > 0 ? $"Arriving: {parts[0]}" : "Arriving: holds position";
            if (events.Any(e => e is Purged)) parts.Add("purges its poison");
            if (events.Any(e => e is DroneDocked)) parts.Add("docks");
            var dmg = hits.Where(h => h.Damage > 0 || h.Killed).ToList();
            foreach (var h in dmg.Take(3)) parts.Add($"hits {(h.Ref.Kind == EntityKind.Unit ? Name(h.Def, h.Ref.Id) : h.Def)} for {h.Damage}{(h.Killed ? " (kills)" : "")}");
            if (dmg.Count > 3) parts.Add($"and {dmg.Count - 3} more");
            foreach (var h in hits.Where(x => x.Healed > 0)) parts.Add($"heals {Name(h.Def, h.Ref.Id)} for {h.Healed}");
            if (parts.Count == 0) return "Holds";
            var text = string.Join(", ", parts);
            return char.ToUpperInvariant(text[0]) + text.Substring(1);
        }
    }
}
