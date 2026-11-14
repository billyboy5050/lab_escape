#nullable enable
using System.Collections.Generic;
using System.Linq;
using LabEscape.Core;

namespace LabEscape.Client
{
    /// <summary>
    /// The words on the briefing screen, as the retired web client wrote them. It says what the rules sheet says and
    /// nothing the game hides: not when the waves come, and not what the defences cover. Every number is read from the content.
    /// </summary>
    public static class BriefingText
    {
        /// <summary>How fast the intercom prints, and the wait before it starts, as the web client's briefing.</summary>
        public const float CharsPerSecond = 45f;
        public const float Delay = 0.3f;

        public const string KitHeading = "YOUR KIT";

        /// <summary>The line the intercom opens the fight with, in quotes, or nothing if the content has none.</summary>
        public static string Opening(Content c)
        {
            var line = c.Intercom.Lines.FirstOrDefault(l => l.Trigger.Type == TriggerTypes.RoundStart && l.Trigger.Round == 1);
            return line == null || line.Text.Length == 0 ? "" : $"\"{line.Text}\"";
        }

        /// <summary>The room's size and how many waves are in play: `wavesInPlay` is fewer when one is held back.</summary>
        public static string Grid(RoomDef room, int wavesInPlay) => $"{room.Width}x{room.Height} grid · {wavesInPlay} wave{(wavesInPlay == 1 ? "" : "s")}";

        public static int WavesInPlay(RoomDef room, FightSettings settings) => System.Math.Min(settings.MaxWaves ?? room.Waves.Count, room.Waves.Count);

        public static IReadOnlyList<string> Objective(Content c, RoomDef room, int wavesInPlay) => new[]
        {
            $"You are alone, with {c.Units["player"].Hp} HP and no way to heal. Security arrives in {wavesInPlay} wave{(wavesInPlay == 1 ? "" : "s")}: the first is already here, and you are not told when the others come.",
            "Win when the last wave has arrived and every enemy is dead.",
            $"Lose at 0 HP, or if round {room.RoundCap} ends with the fight undecided.",
        };

        public static IReadOnlyList<string> KnowTheRoom => new[]
        {
            "The lab's own defences fire on whoever is in reach. They always give a cue first; nothing shows which tiles are at risk.",
            "Every enemy and minion shows what it will do, and every action of yours is previewed before you commit.",
        };

        /// <summary>The lab's own defences as the room shows them: each device and where it stands, which the board draws and the player can see.</summary>
        public static string Devices(Content c, RoomDef room)
        {
            if (room.Hazards.Count == 0) return "";
            var names = room.Hazards.GroupBy(h => h.Def).Select(g =>
            {
                var name = c.HazardDefs.TryGetValue(g.Key, out var def) ? def.Name : g.Key;
                return $"{name} ({string.Join(", ", g.SelectMany(h => h.Tiles))})";
            });
            return "Lab devices: " + string.Join("; ", names) + ".";
        }

        /// <summary>The player's start tile.</summary>
        public static string Start(RoomDef room) => room.PlayerStart;

        /// <summary>The kit the player takes in: each ability's name, then each upgrade's, with the kit each belongs to.</summary>
        public static IReadOnlyList<(string Name, string Kit)> Kit(Content c, Loadout loadout) =>
            loadout.Abilities.Select(id => (c.Abilities.TryGetValue(id, out var a) ? a.Name : id, c.Abilities.TryGetValue(id, out var b) ? b.Kit : ""))
                .Concat(loadout.Upgrades.Select(id => (c.Upgrades.TryGetValue(id, out var u) ? u.Name : id, c.Upgrades.TryGetValue(id, out var v) ? v.Kit : ""))).ToList();
    }
}
