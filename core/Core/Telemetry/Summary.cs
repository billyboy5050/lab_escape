#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace LabEscape.Core
{
    /// <summary>How a fight finished. Error is only ever on a simulator row, for a fight that threw.</summary>
    public enum FightOutcome { Win, Lose, Unfinished, Error }

    /// <summary>Damage split by who caused it: the player's own abilities, its minions, enemies, hazards.</summary>
    public sealed class DamageTally
    {
        public int Player;
        public int Minion;
        public int Enemy;
        public int Hazard;

        public int this[SourceCategory category]
        {
            get
            {
                switch (category)
                {
                    case SourceCategory.Player: return Player;
                    case SourceCategory.Minion: return Minion;
                    case SourceCategory.Enemy: return Enemy;
                    case SourceCategory.Hazard: return Hazard;
                    default: throw new ArgumentOutOfRangeException(nameof(category));
                }
            }
            set
            {
                switch (category)
                {
                    case SourceCategory.Player: Player = value; break;
                    case SourceCategory.Minion: Minion = value; break;
                    case SourceCategory.Enemy: Enemy = value; break;
                    case SourceCategory.Hazard: Hazard = value; break;
                    default: throw new ArgumentOutOfRangeException(nameof(category));
                }
            }
        }

        public int Total => Player + Minion + Enemy + Hazard;

        public JObject ToToken() => new JObject { ["player"] = Player, ["minion"] = Minion, ["enemy"] = Enemy, ["hazard"] = Hazard };
    }

    public sealed class CauseOfDeath
    {
        public SourceCategory Category;
        public string Source = "";
        public string Via = "";
    }

    public sealed class HazardFire
    {
        public int Round;
        public string Hazard = "";
        public List<string> Victims = new List<string>();
    }

    public sealed class FightSummary
    {
        public Loadout Loadout = null!;
        public FightOutcome Outcome = FightOutcome.Unfinished;
        public string? Cause;
        public int Rounds;
        public int WavesSpawned;
        /// <summary>Damage the player took, by who caused it.</summary>
        public DamageTally PlayerDamageTaken = new DamageTally();
        /// <summary>Damage taken by every player-side unit and object (player, minions, shields, eggs).</summary>
        public DamageTally SideDamageTaken = new DamageTally();
        /// <summary>Damage dealt to enemies, by source.</summary>
        public DamageTally EnemyDamageTaken = new DamageTally();
        /// <summary>Share of the player's damage taken that came from the player's own side (0 to 1).</summary>
        public double FriendlyFireShare;
        public int FriendlyFireToPlayer;
        public int FriendlyFireToSide;
        public CauseOfDeath? CauseOfDeath;
        public OrderedMap<string, int> AbilitiesUsed = new OrderedMap<string, int>(StringComparer.Ordinal);
        public OrderedMap<string, int> Kills = new OrderedMap<string, int>(StringComparer.Ordinal);
        public int MinionsLost;
        public int PeakMinions;
        public List<HazardFire> HazardFires = new List<HazardFire>();
        public int HazardCancels;
        public int HazardPrimes;
        public List<string> IntercomLines = new List<string>();
        /// <summary>Round the Warden first damaged the player.</summary>
        public int? WardenReachedPlayerRound;
        public int? WardenDiedRound;
        public int ChainSuppressions;

        /// <summary>The summary with the same fields, in the same order, as the TypeScript object.</summary>
        public JObject ToToken()
        {
            return new JObject
            {
                ["loadout"] = new JObject { ["abilities"] = new JArray(Loadout.Abilities), ["upgrades"] = new JArray(Loadout.Upgrades) },
                ["outcome"] = Names.Of(Outcome),
                ["cause"] = Cause == null ? JValue.CreateNull() : new JValue(Cause),
                ["rounds"] = Rounds,
                ["wavesSpawned"] = WavesSpawned,
                ["playerDamageTaken"] = PlayerDamageTaken.ToToken(),
                ["sideDamageTaken"] = SideDamageTaken.ToToken(),
                ["enemyDamageTaken"] = EnemyDamageTaken.ToToken(),
                ["friendlyFireShare"] = FriendlyFireShare,
                ["friendlyFireToPlayer"] = FriendlyFireToPlayer,
                ["friendlyFireToSide"] = FriendlyFireToSide,
                ["causeOfDeath"] = CauseOfDeath == null ? JValue.CreateNull() : new JObject { ["category"] = Names.Of(CauseOfDeath.Category), ["source"] = CauseOfDeath.Source, ["via"] = CauseOfDeath.Via },
                ["abilitiesUsed"] = Summaries.Counts(AbilitiesUsed),
                ["kills"] = Summaries.Counts(Kills),
                ["minionsLost"] = MinionsLost,
                ["peakMinions"] = PeakMinions,
                ["hazardFires"] = new JArray(HazardFires.Select(h => new JObject { ["round"] = h.Round, ["hazard"] = h.Hazard, ["victims"] = new JArray(h.Victims) })),
                ["hazardCancels"] = HazardCancels,
                ["hazardPrimes"] = HazardPrimes,
                ["intercomLines"] = new JArray(IntercomLines),
                ["wardenReachedPlayerRound"] = WardenReachedPlayerRound,
                ["wardenDiedRound"] = WardenDiedRound,
                ["chainSuppressions"] = ChainSuppressions,
            };
        }
    }

    /// <summary>Builds a fight summary from the event stream, fed incrementally so the simulator need not keep every event.</summary>
    public sealed class SummaryBuilder
    {
        private int _round;
        private readonly FightSummary _s;
        private readonly Dictionary<int, string> _unitNames = new Dictionary<int, string> { [0] = "player" };

        public SummaryBuilder(Loadout loadout)
        {
            _s = new FightSummary { Loadout = loadout };
        }

        private static void Bump(OrderedMap<string, int> counts, string key)
        {
            counts[key] = (counts.TryGetValue(key, out var n) ? n : 0) + 1;
        }

        public void Add(IEnumerable<GameEvent> events)
        {
            var s = _s;
            foreach (var e in events)
            {
                switch (e)
                {
                    case RoundStarted x:
                        _round = x.Round;
                        s.Rounds = x.Round;
                        break;
                    case WaveSpawned x:
                        s.WavesSpawned = Math.Max(s.WavesSpawned, x.Wave);
                        break;
                    case UnitSpawned x:
                        _unitNames[x.Unit.Id] = x.Unit.Def;
                        break;
                    case AbilityUsed x:
                        Bump(s.AbilitiesUsed, x.Ability);
                        break;
                    case Sprinted _:
                        Bump(s.AbilitiesUsed, "sprint");
                        break;
                    case Reloaded _:
                        Bump(s.AbilitiesUsed, "reload");
                        break;
                    case DroneRedeployed _:
                        Bump(s.AbilitiesUsed, "redeploy");
                        break;
                    case ObjectRemoved x:
                        // Picking up a mine is the only action that removes an object for this reason.
                        if (x.Reason == RemovalReason.Pickup) Bump(s.AbilitiesUsed, "pick_up_mine");
                        break;
                    case DamageDealt x:
                        AddDamage(x);
                        break;
                    case UnitDied x:
                        if (x.Team == Team.Enemy)
                        {
                            Bump(s.Kills, x.Def);
                            if (x.Def == "warden") s.WardenDiedRound = _round;
                        }
                        else if (x.Kind == UnitKind.Minion)
                        {
                            s.MinionsLost++;
                        }
                        if (x.Id == 0 && x.Killer != null) s.CauseOfDeath = new CauseOfDeath { Category = x.Killer.Category, Source = Summaries.SourceName(x.Killer), Via = x.Killer.Via };
                        break;
                    case HazardFired x:
                        s.HazardFires.Add(new HazardFire { Round = _round, Hazard = x.Hazard, Victims = x.Victims.Select(VictimName).ToList() });
                        break;
                    case HazardCancelled _:
                        s.HazardCancels++;
                        break;
                    case HazardPrimed _:
                        s.HazardPrimes++;
                        break;
                    case IntercomLine x:
                        s.IntercomLines.Add(x.Line);
                        break;
                    case ChainSuppressed _:
                        s.ChainSuppressions++;
                        break;
                    case FightEnded x:
                        s.Outcome = Summaries.OutcomeOf(x.Result);
                        s.Cause = x.Cause;
                        s.Rounds = x.Round;
                        break;
                }
            }
        }

        private void AddDamage(DamageDealt e)
        {
            var s = _s;
            // HP actually lost, so a killing blow's overkill does not inflate the totals.
            var lost = e.HpLost;
            if (e.TargetSide != Team.Player)
            {
                s.EnemyDamageTaken[e.Source.Category] += lost;
                return;
            }
            s.SideDamageTaken[e.Source.Category] += lost;
            if (e.Source.Side == SourceSide.Player) s.FriendlyFireToSide += lost;
            if (e.Target.Kind != EntityKind.Unit || e.Target.Id != 0) return;
            s.PlayerDamageTaken[e.Source.Category] += lost;
            if (e.Source.Side == SourceSide.Player) s.FriendlyFireToPlayer += lost;
            if (e.Source.Def == "warden" && lost > 0 && !s.WardenReachedPlayerRound.HasValue) s.WardenReachedPlayerRound = _round;
        }

        private string VictimName(EntityRef v)
        {
            if (v.Kind != EntityKind.Unit) return $"object {v.Id}";
            return _unitNames.TryGetValue(v.Id, out var name) ? name : $"unit {v.Id}";
        }

        public FightSummary Finish(GameState state)
        {
            var s = _s;
            s.PeakMinions = state.PeakMinions;
            s.Rounds = state.Round;
            if (state.Outcome != null)
            {
                s.Outcome = Summaries.OutcomeOf(state.Outcome.Result);
                s.Cause = state.Outcome.Cause;
            }
            var taken = s.PlayerDamageTaken.Total;
            s.FriendlyFireShare = taken > 0 ? (double)s.FriendlyFireToPlayer / taken : 0;
            return s;
        }
    }

    public static class Summaries
    {
        public static FightSummary Summarize(IEnumerable<GameEvent> events, GameState state)
        {
            var b = new SummaryBuilder(state.Loadout);
            b.Add(events);
            return b.Finish(state);
        }

        public static string SourceName(SourceRef src)
        {
            if (src.Kind == SourceKind.Hazard) return src.TextId ?? JsText.Number(src.NumberId);
            return src.Def;
        }

        internal static FightOutcome OutcomeOf(FightResult result) => result == FightResult.Win ? FightOutcome.Win : FightOutcome.Lose;

        /// <summary>A map of counts as a JSON object, in insertion order.</summary>
        public static JObject Counts(OrderedMap<string, int> counts)
        {
            var o = new JObject();
            foreach (var pair in counts) o[pair.Key] = pair.Value;
            return o;
        }
    }
}
