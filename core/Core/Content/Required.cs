#nullable enable
using System.Collections.Generic;

namespace LabEscape.Core
{
    /// <summary>
    /// Fields the rules code reads without a fallback, by definition id, as dotted paths. A content edit that drops one is
    /// rejected on load, so a hot reload keeps the old values instead of crashing the first time the rule runs.
    /// </summary>
    public static class RequiredFields
    {
        public static readonly OrderedMap<string, string[]> Ability = new OrderedMap<string, string[]>
        {
            { "sidearm", new[] { "targeting.range" } },
            { "grapple_hook", new[] { "targeting.range" } },
            { "proximity_mine", new[] { "targeting.range", "blast" } },
            { "auto_turret", new[] { "targeting.range", "summon" } },
            { "scout_drone", new[] { "summon" } },
            { "barrier_shield", new[] { "targeting.range", "segmentHp" } },
            { "acid_spit", new[] { "targeting.range" } },
            { "lunge", new[] { "targeting.range", "damage", "damageType" } },
            { "spore_pod", new[] { "targeting.range", "area", "status" } },
            { "brood_egg", new[] { "targeting.range" } },
            { "parasite", new[] { "targeting.range", "status" } },
        };

        /// <summary>Each unit's AI is chosen by which blocks it has, so a missing block would also change its role silently.</summary>
        public static readonly OrderedMap<string, string[]> Unit = new OrderedMap<string, string[]>
        {
            { "guard", new[] { "attack" } },
            { "medic", new[] { "heal" } },
            { "flamer", new[] { "sweep" } },
            { "warden", new[] { "melee", "charge" } },
            { "turret", new[] { "attack", "ammo" } },
            { "drone", new[] { "zap", "charges" } },
            { "hatchling", new[] { "melee" } },
            { "spitter", new[] { "attack" } },
            { "burster", new[] { "blast" } },
        };

        /// <summary>
        /// Fields a unit needs because of a behaviour block it has, whatever its ID: the AI is picked by which block a unit
        /// carries, so any unit with the block runs that AI and reads these numbers.
        /// </summary>
        public static readonly OrderedMap<string, string[]> Block = new OrderedMap<string, string[]>
        {
            { "heal", new[] { "ai.safeDistance", "ai.followDistance" } },
            { "sweep", new[] { "ai.scorePerPlayerSideHit", "ai.scorePlayerBonus", "ai.scorePerAllyHit" } },
            { "zap", new[] { "returnPhases" } },
        };

        /// <summary>The hazard shapes the engine knows, and the fields each shape reads without a fallback.</summary>
        public static readonly OrderedMap<string, string[]> HazardShape = new OrderedMap<string, string[]>
        {
            { "lane", new string[0] },
            { "adjacentToTiles", new[] { "radius" } },
        };

        public static readonly OrderedMap<string, string[]> Upgrade = new OrderedMap<string, string[]>
        {
            { "A1_spore_burst", new[] { "status", "area.radius" } },
            { "A3_parasite_jump", new[] { "range" } },
            { "H1_spore_mines", new[] { "status" } },
            { "H2_plague_drone", new[] { "status" } },
        };

        /// <summary>Utilities with a range read it without a fallback.</summary>
        public static readonly OrderedMap<string, string[]> Utility = new OrderedMap<string, string[]>
        {
            { "reload", new[] { "range" } },
            { "pick_up_mine", new[] { "range" } },
        };

        public static readonly IReadOnlyList<string> Utilities = new[] { "sprint", "reload", "pick_up_mine", "redeploy" };
    }
}
