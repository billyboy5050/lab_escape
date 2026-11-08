#nullable enable
using System;
using System.Collections.Generic;

namespace LabEscape.Core
{
    public enum Team { Player, Enemy }
    public enum UnitKind { Player, Minion, Enemy }
    public enum DamageType { Kinetic, Explosive, Fire, Electric, Acid, Poison, Parasite }
    public enum StatusId { Poison, Parasite, Corrode, Pinned, Slowed }
    public enum Phase { RoundStart, Player, Minion, Enemy, Environment, Ended }
    public enum DroneState { Deployed, Returning, Docked }
    public enum ObjectKind { Mine, Egg, Shield, Corpse }
    public enum HazardState { Ready, Primed, Cooldown }
    public enum HazardShape { Lane, AdjacentToTiles }
    public enum HazardStage { Prime, Fire }
    public enum HazardResult { Prime, Hold, Fire, Cancel }
    public enum MoveMode { Walk, Fly, Pull, Grapple, Lunge, Charge }
    public enum EntityKind { Unit, Object }
    public enum SourceSide { Player, Enemy, Hazard }
    public enum SourceCategory { Player, Minion, Enemy, Hazard }
    public enum SourceKind { Unit, Object, Hazard }
    public enum FightResult { Win, Lose }
    public enum RemovalReason { Pickup, Consumed, Decayed, Hatched, Detonated, Replaced }
    public enum AreaKind { Blast, Spore, Gas, Panel, Sweep, Burst }
    public enum SuppressReason { Repeat, Depth }
    public enum ArrivalRound { All, First, None }

    /// <summary>The JSON name of an enum value: its C# name with a lowercase first letter ("roundStart", "adjacentToTiles").</summary>
    public static class Names
    {
        public static string Of<T>(T value) where T : struct, Enum => Table<T>.Name[value];

        public static T Parse<T>(string name) where T : struct, Enum
        {
            if (Table<T>.Value.TryGetValue(name, out var value)) return value;
            throw new FormatException($"\"{name}\" is not a {typeof(T).Name}");
        }

        public static bool IsName<T>(string name) where T : struct, Enum => Table<T>.Value.ContainsKey(name);

        private static class Table<T> where T : struct, Enum
        {
            public static readonly Dictionary<T, string> Name = new Dictionary<T, string>();
            public static readonly Dictionary<string, T> Value = new Dictionary<string, T>(StringComparer.Ordinal);

            static Table()
            {
                foreach (T item in Enum.GetValues(typeof(T)))
                {
                    var text = item.ToString();
                    var name = char.ToLowerInvariant(text[0]) + text.Substring(1);
                    Name[item] = name;
                    Value[name] = item;
                }
            }
        }
    }
}
