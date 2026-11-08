#nullable enable
using System.Collections.Generic;
using System.Linq;
using LabEscape.Core;

namespace LabEscape.Client
{
    public enum LogKind { Round, Phase, Ev, FriendlyFire, Hazard, Intercom, End }

    public sealed class LogLine
    {
        public readonly int Id;
        public readonly string Text;
        public readonly LogKind Kind;

        public LogLine(int id, string text, LogKind kind)
        {
            Id = id;
            Text = text;
            Kind = kind;
        }
    }

    /// <summary>Names and descriptions shown to the player.</summary>
    public static class Words
    {
        public static string UnitName(Content c, string def, int? id = null)
        {
            if (def == "player") return "You";
            var n = c.Units.TryGetValue(def, out var d) ? d.Name : def;
            return id == null ? n : $"{n} {id}";
        }

        public static string HazardName(Content c, GameState? s, string id)
        {
            var hz = s?.Hazards.FirstOrDefault(h => h.Id == id);
            var key = hz != null ? hz.Def : id;
            var def = c.HazardDefs.TryGetValue(key, out var d) ? d.Name : id;
            if (hz != null && hz.Def == "gas_vent") return $"{def} {string.Join("/", hz.Tiles.Select(Tiles.Name))}";
            return def;
        }

        public static string TargetName(Content c, EntityKind kind, int id, string def)
        {
            if (kind == EntityKind.Unit) return UnitName(c, def, id);
            return def == "shield" ? "Shield segment" : def == "egg" ? "Egg" : def;
        }

        /// <summary>The hover summary of a unit: HP, armor, ammo, statuses, and whether it arrived this round.</summary>
        public static string DescribeUnit(Unit u, int round)
        {
            var parts = new List<string> { $"{u.Hp}/{u.MaxHp} HP" };
            var corrode = u.Statuses.Corrode;
            var armor = System.Math.Max(0, u.Armor - (corrode?.Stacks ?? 0));
            if (u.Armor != 0 || corrode != null) parts.Add($"armor {armor}{(corrode != null ? $" ({u.Armor} − {corrode.Stacks} Corrode)" : "")}");
            if (u.Ammo.HasValue) parts.Add($"{u.Ammo}/{u.MaxAmmo} ammo");
            if (u.Charges.HasValue) parts.Add($"{u.Charges}/{u.MaxCharges} charges, {(u.DroneState.HasValue ? Names.Of(u.DroneState.Value) : "")}");
            if (u.Statuses.Poison != null) parts.Add($"poisoned {u.Statuses.Poison.Remaining}");
            if (u.Statuses.Parasite != null) parts.Add($"Parasite {u.Statuses.Parasite.Remaining}");
            if (u.Statuses.Pinned != null) parts.Add("Pinned");
            if (u.Statuses.Slowed != null) parts.Add("Slowed");
            // ArrivalRound stays on a wave unit for good; only the round it arrived in is "just arrived".
            if (u.ArrivalRound == round) parts.Add("just arrived");
            return string.Join(" · ", parts);
        }

        public static string DescribeObject(Content c, BoardObject o, bool placing = false)
        {
            var eggs = c.Rules.Eggs;
            switch (o.Kind)
            {
                case ObjectKind.Egg:
                {
                    var n = o.Host == "floor" ? eggs.FloorHatchlings : 1;
                    var hatch = o.HatchInto != null && c.Units.TryGetValue(o.HatchInto, out var hd) ? hd.Name : o.HatchInto ?? "hatchling";
                    var what = $"{(n == 1 ? "a" : n.ToString())} {hatch.ToLowerInvariant()}{(n == 1 ? "" : "s")}";
                    var host = o.Host != null && o.Host != "floor" ? $" on a {(c.Units.TryGetValue(o.Host, out var ho) ? ho.Name : o.Host)} corpse" : "";
                    var when = $"{o.Timer} round{(o.Timer == 1 ? "" : "s")}";
                    return placing ? $"Places an egg{host}: hatches {what} in {when} ({o.Hp} HP)." : $"Egg{host}: hatches {what} in {when}; {o.Hp}/{o.MaxHp} HP.";
                }
                case ObjectKind.Corpse:
                {
                    var of = o.Of ?? "";
                    string? variant = null;
                    if (eggs.CorpseHosts.TryGetValue(of, out var v)) variant = v;
                    var host = variant != null ? $"A Brood Egg here hatches a {(c.Units.TryGetValue(variant, out var vd) ? vd.Name : variant).ToLowerInvariant()}." : "Cannot host an egg.";
                    return $"{(c.Units.TryGetValue(of, out var od) ? od.Name : of)} corpse: {o.Decay} round{(o.Decay == 1 ? "" : "s")} left. {host}";
                }
                case ObjectKind.Mine:
                {
                    var b = c.Abilities.TryGetValue("proximity_mine", out var pm) ? pm.Blast : null;
                    return placing ? "Places a mine." : $"Your mine: any ground unit entering it sets it off ({b?.Damage} {(b != null ? Names.Of(b.DamageType) : "")} to its tile and neighbours).";
                }
                default:
                    return placing ? $"Places a shield segment ({o.Hp} HP)." : $"Shield segment: {o.Hp}/{o.MaxHp} HP. Blocks movement and projectiles, not sight.";
            }
        }
    }

    /// <summary>Turns engine events into combat log lines. Keeps a name registry so dead units stay nameable.</summary>
    public sealed class EventFormatter
    {
        private readonly Content _c;
        private readonly Dictionary<int, string> _names = new Dictionary<int, string>();
        private int _nextId = 1;

        public EventFormatter(Content c, GameState? initial = null)
        {
            _c = c;
            if (initial != null)
            {
                foreach (var u in initial.Units) _names[u.Id] = Words.UnitName(c, u.Def, u.Id);
            }
            _names[0] = "You";
        }

        private string Who(int id) => _names.TryGetValue(id, out var n) ? n : $"Unit {id}";

        private string Src(SourceRef s)
        {
            if (s.Kind == SourceKind.Hazard) return _c.HazardDefs.TryGetValue(s.Def, out var d) ? d.Name : s.TextId ?? s.Def;
            if (s.Kind == SourceKind.Object) return s.Def == "mine" ? "A mine" : s.Def;
            if (s.Via == "A1_spore_burst") return $"Spores from {Who(s.NumberId)}";
            if (s.Via == "A2_acid_brood") return $"Acid from {Who(s.NumberId)}";
            if (s.Via == "poison") return $"Poison ({(s.Def == "player" ? "yours" : Who(s.NumberId))})";
            if (s.Via == "parasite" || s.Via == "A3_parasite_jump") return "Parasite";
            if (s.Def == "player") return "You";
            return Who(s.NumberId);
        }

        public LogLine? Format(GameEvent e, GameState? s)
        {
            var t = Line(e, s);
            return t == null ? null : new LogLine(_nextId++, t.Value.Text, t.Value.Kind);
        }

        private static string StatusWord(StatusId id)
        {
            switch (id)
            {
                case StatusId.Poison: return "poisoned";
                case StatusId.Parasite: return "infected with a Parasite";
                case StatusId.Corrode: return "corroded";
                case StatusId.Pinned: return "Pinned";
                default: return "Slowed";
            }
        }

        private static string ObjectWord(ObjectKind k) => k == ObjectKind.Shield ? "Shield segment" : k == ObjectKind.Egg ? "Egg" : Names.Of(k);

        private (string Text, LogKind Kind)? Line(GameEvent ev, GameState? s)
        {
            var c = _c;
            switch (ev)
            {
                case RoundStarted e:
                    return ($"Round {e.Round}", LogKind.Round);
                case PhaseStarted e:
                    if (e.Phase == Phase.RoundStart) return null;
                    if (e.Phase == Phase.Player) return ("Your turn", LogKind.Phase);
                    var name = Names.Of(e.Phase);
                    return ($"{char.ToUpperInvariant(name[0])}{name.Substring(1)} phase", LogKind.Phase);
                case UnitSpawned e:
                {
                    var n = Words.UnitName(c, e.Unit.Def, e.Unit.Id);
                    _names[e.Unit.Id] = n;
                    var verb = e.Cause.StartsWith("wave", System.StringComparison.Ordinal) ? "arrives at" : e.Cause == "egg" ? "hatches at" : e.Cause == "parasite" ? "bursts out at" : "appears at";
                    return ($"{n} {verb} {Tiles.Name(e.Unit.Pos)}", LogKind.Ev);
                }
                case UnitMoved e:
                {
                    var verb = e.Mode == MoveMode.Pull ? "is pulled" : e.Mode == MoveMode.Charge ? "charges" : e.Mode == MoveMode.Lunge ? "lunges" : "moves";
                    return ($"{Who(e.Id)} {verb} {Tiles.Name(e.From)} → {Tiles.Name(e.To)}", LogKind.Ev);
                }
                case AbilityUsed e:
                {
                    var n = c.Abilities.TryGetValue(e.Ability, out var a) ? a.Name : e.Ability;
                    var mode = e.Mode != null ? $" (pull {e.Mode})" : "";
                    var first = e.Target.HasValue ? $" on {Tiles.Name(e.Target.Value)}" : "";
                    var second = e.Target2.HasValue ? $" and {Tiles.Name(e.Target2.Value)}" : "";
                    return ($"You use {n}{mode}{first}{second}", LogKind.Ev);
                }
                case ProjectileFired e:
                    if (e.Reflected == true) return ("A shield segment reflects the shot", LogKind.Ev);
                    if (e.Hit == null && e.HitPos.HasValue) return ($"{Src(e.Source)}'s shot stops at {Tiles.Name(e.HitPos.Value)}", LogKind.Ev);
                    return null;
                case DamageDealt e:
                {
                    var target = e.Target.Kind == EntityKind.Unit ? Who(e.Target.Id) : $"{(e.TargetDef == "shield" ? "Shield segment" : e.TargetDef == "egg" ? "Egg" : e.TargetDef)} at {Tiles.Name(e.Pos)}";
                    var ff = e.TargetSide == Team.Player && e.Source.Side == SourceSide.Player;
                    var armor = e.Absorbed != 0 ? $" ({e.Absorbed} absorbed by armor)" : "";
                    var text = $"{Src(e.Source)} → {target}: {e.Amount} {Names.Of(e.DamageType)}{armor}{(e.Killed ? ", destroyed" : "")}{(ff ? "  [friendly fire]" : "")}";
                    return (text, ff ? LogKind.FriendlyFire : e.Source.Kind == SourceKind.Hazard ? LogKind.Hazard : LogKind.Ev);
                }
                case Healed e:
                    return ($"{Src(e.Source)} heals {Who(e.Target)} for {e.Amount}", LogKind.Ev);
                case UnitDied e:
                    return ($"{Who(e.Id)} dies at {Tiles.Name(e.Pos)}", e.Team == Team.Player ? LogKind.FriendlyFire : LogKind.Ev);
                case ObjectPlaced e:
                {
                    var o = e.Object;
                    if (o.Kind == ObjectKind.Corpse) return null;
                    var what = o.Kind == ObjectKind.Shield ? "Shield segment" : o.Kind == ObjectKind.Egg ? $"Egg ({o.HatchInto}, {o.Timer} round{(o.Timer == 1 ? "" : "s")})" : "Mine";
                    return ($"{what} placed at {Tiles.Name(o.Pos)}", LogKind.Ev);
                }
                case ObjectDestroyed e:
                    return ($"{ObjectWord(e.Kind)} at {Tiles.Name(e.Pos)} destroyed", LogKind.Ev);
                case ObjectTriggered e:
                    return ($"Mine at {Tiles.Name(e.Pos)} goes off{(e.By.HasValue ? $" under {Who(e.By.Value.Id)}" : "")}", LogKind.Ev);
                case ObjectRemoved e:
                    if (e.Reason == RemovalReason.Pickup) return ($"You pick up the mine at {Tiles.Name(e.Pos)}", LogKind.Ev);
                    if (e.Reason == RemovalReason.Consumed) return ($"The corpse at {Tiles.Name(e.Pos)} becomes an egg", LogKind.Ev);
                    return null;
                case StatusApplied e:
                {
                    var v = e.Status == StatusId.Corrode ? $" (+{e.Value})" : e.Status == StatusId.Poison || e.Status == StatusId.Parasite ? $" ({e.Value} rounds)" : "";
                    var targetTeam = s?.Units.FirstOrDefault(u => u.Id == e.Target)?.Team ?? Team.Enemy;
                    var ff = e.Source.Side == SourceSide.Player && targetTeam == Team.Player;
                    if (e.Spread) return ($"Poison spreads to {Who(e.Target)}{v}{(ff ? "  [friendly fire]" : "")}", ff ? LogKind.FriendlyFire : LogKind.Ev);
                    return ($"{Who(e.Target)} is {StatusWord(e.Status)}{v}{(ff && e.Status != StatusId.Pinned ? "  [friendly fire]" : "")}", ff ? LogKind.FriendlyFire : LogKind.Ev);
                }
                case StatusExpired e:
                    if (e.Status != StatusId.Poison && e.Status != StatusId.Parasite) return null;
                    return ($"{(e.Status == StatusId.Poison ? "Poison" : "Parasite")} wears off {Who(e.Target)}", LogKind.Ev);
                case StatusRemoved e:
                    return ($"{Who(e.Target)} purges its {Names.Of(e.Status)}", LogKind.Ev);
                case EggHatched e:
                    return ($"Egg at {Tiles.Name(e.Pos)} hatches{(e.Capped != 0 ? $" ({e.Capped} held back by the minion cap)" : "")}", LogKind.Ev);
                case DroneDocked e:
                    return ($"{Who(e.Id)} docks", LogKind.Ev);
                case DroneRedeployed e:
                    return ($"{Who(e.Id)} redeploys at {Tiles.Name(e.Pos)}", LogKind.Ev);
                case Reloaded e:
                    return ($"{Who(e.Id)} reloaded", LogKind.Ev);
                case Sprinted e:
                    return ($"You sprint (+{e.Movement} movement)", LogKind.Ev);
                case Charged e:
                    return ($"{Who(e.Id)} charges {Tiles.Name(e.From)} → {Tiles.Name(e.To)}", LogKind.Ev);
                case HazardPrimed e:
                {
                    var hz = s?.Hazards.FirstOrDefault(h => h.Id == e.Hazard);
                    var cue = hz != null && c.HazardDefs.TryGetValue(hz.Def, out var d) ? d.Cue : null;
                    return (cue ?? $"{Words.HazardName(c, s, e.Hazard)} stirs", LogKind.Hazard);
                }
                case HazardFired e:
                    return ($"{Words.HazardName(c, s, e.Hazard)} fires", LogKind.Hazard);
                case HazardCancelled e:
                    return ($"The cue at the {Words.HazardName(c, s, e.Hazard).ToLowerInvariant()} fades", LogKind.Hazard);
                case IntercomLine e:
                    return ($"INTERCOM: \"{e.Text}\"", LogKind.Intercom);
                case FightEnded e:
                    return ($"{(e.Result == FightResult.Win ? "Victory" : "Defeat")}: {e.Cause}", LogKind.End);
                default:
                    return null;
            }
        }
    }
}
