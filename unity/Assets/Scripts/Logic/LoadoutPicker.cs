#nullable enable
using System.Collections.Generic;
using System.Linq;
using LabEscape.Core;

namespace LabEscape.Client
{
    /// <summary>
    /// The loadout screen's choices: the abilities and upgrades picked, the preset they came from, and whether wave 3 is held
    /// back. It follows the web client's `LoadoutScreen.tsx` and has no Unity code, so tests drive it directly. What may be picked
    /// and what is a problem come from the engine (`Engine.LoadoutProblems`, `ContentLoader.UpgradeAllowed`); this decides nothing.
    /// </summary>
    public sealed class LoadoutPicker
    {
        private readonly Content _c;

        /// <summary>Abilities picked, in the content's order.</summary>
        public List<string> Abilities { get; }

        public List<string> Upgrades { get; }

        /// <summary>The preset these picks are, or null once any pick is made by hand.</summary>
        public string? PresetId { get; private set; }

        /// <summary>Wave 3 held back: the fight has two waves in play and clearing the second wins (the first playtest's setup).</summary>
        public bool HoldWave3 { get; set; }

        public LoadoutPicker(Content c, Loadout loadout, string? presetId)
        {
            _c = c;
            Abilities = c.AbilityOrder.Where(loadout.Abilities.Contains).ToList();
            Upgrades = c.UpgradeOrder.Where(loadout.Upgrades.Contains).ToList();
            PresetId = presetId != null && c.Presets.Any(p => p.Id == presetId) ? presetId : null;
        }

        public int MaxAbilities => _c.Rules.Loadout.Abilities;

        public int MaxUpgrades => _c.Rules.Loadout.Upgrades;

        public bool AbilityOn(string id) => Abilities.Contains(id);

        public bool UpgradeOn(string id) => Upgrades.Contains(id);

        /// <summary>An ability can be added while there is room, and taken off at any time.</summary>
        public bool CanToggleAbility(string id) => AbilityOn(id) || Abilities.Count < MaxAbilities;

        /// <summary>The abilities picked meet what an upgrade requires.</summary>
        public bool UpgradeAllowed(string id) => ContentLoader.UpgradeAllowed(_c.Upgrades[id].Requires, Abilities);

        public bool CanToggleUpgrade(string id) => UpgradeOn(id) || (UpgradeAllowed(id) && Upgrades.Count < MaxUpgrades);

        public string NeedsOf(string id) => Engine.DescribeRequires(_c, _c.Upgrades[id].Requires);

        public void ToggleAbility(string id)
        {
            if (!_c.Abilities.ContainsKey(id) || !CanToggleAbility(id)) return;
            if (AbilityOn(id)) Abilities.Remove(id);
            else Abilities.Add(id);
            Abilities.Sort((a, b) => _c.AbilityOrder.IndexOf(a).CompareTo(_c.AbilityOrder.IndexOf(b)));
            // An upgrade whose requirements are no longer met goes with the ability it needed.
            Upgrades.RemoveAll(u => !UpgradeAllowed(u));
            PresetId = null;
        }

        public void ToggleUpgrade(string id)
        {
            if (!_c.Upgrades.ContainsKey(id) || !CanToggleUpgrade(id)) return;
            if (UpgradeOn(id)) Upgrades.Remove(id);
            else Upgrades.Add(id);
            Upgrades.Sort((a, b) => _c.UpgradeOrder.IndexOf(a).CompareTo(_c.UpgradeOrder.IndexOf(b)));
            PresetId = null;
        }

        public void SelectPreset(PresetDef p)
        {
            Abilities.Clear();
            Abilities.AddRange(_c.AbilityOrder.Where(p.Abilities.Contains));
            Upgrades.Clear();
            Upgrades.AddRange(_c.UpgradeOrder.Where(p.Upgrades.Contains));
            PresetId = p.Id;
        }

        public void Clear()
        {
            Abilities.Clear();
            Upgrades.Clear();
            PresetId = null;
        }

        public Loadout ToLoadout() => new Loadout(Abilities, Upgrades);

        /// <summary>The settings for the fight: held back, wave 3 stays out of play.</summary>
        public FightSettings ToSettings()
        {
            var settings = new FightSettings();
            if (HoldWave3) settings.MaxWaves = 2;
            return settings;
        }

        public List<string> Problems() => Engine.LoadoutProblems(_c, ToLoadout());

        public bool CanStart => Abilities.Count > 0 && Problems().Count == 0;

        /// <summary>The dock's line: what is wrong, or that the picks are ready.</summary>
        public string Status()
        {
            var problems = Problems();
            if (problems.Count > 0) return string.Join("; ", problems);
            return Abilities.Count == 0 ? "Pick at least one ability." : "Ready.";
        }

        /// <summary>"Move, Reload ..." : what the player always has, and what the abilities picked add.</summary>
        public string UtilitiesLine()
        {
            var shown = _c.Utilities.Select(p => p.Value).Where(u => u.RequiresAbility == null || Abilities.Contains(u.RequiresAbility));
            return "Always available: " + string.Join(", ", shown.Select(u => $"{u.Name} ({u.Ap} AP)")) + ".";
        }
    }
}
