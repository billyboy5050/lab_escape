#nullable enable
using System.Linq;
using LabEscape.Core;
using NUnit.Framework;

namespace LabEscape.Client.Tests
{
    /// <summary>The loadout screen's choices, and the loadout it remembers between sessions.</summary>
    public class LoadoutPickerTests
    {
        private static readonly Content C = GameContent.Load();

        private static PresetDef Preset(string id) => C.Presets.First(p => p.Id == id);

        private static LoadoutPicker From(string preset)
        {
            var p = Preset(preset);
            return new LoadoutPicker(C, new Loadout(p.Abilities, p.Upgrades), preset);
        }

        private static MemoryPrefs Prefs() => new MemoryPrefs();

        [Test]
        public void APresetFillsThePicksAndNamesItself()
        {
            var picker = new LoadoutPicker(C, new Loadout(new string[0], new string[0]), null);
            picker.SelectPreset(Preset("tech"));
            Assert.That(picker.PresetId, Is.EqualTo("tech"));
            Assert.That(picker.Abilities, Is.EquivalentTo(Preset("tech").Abilities));
            Assert.That(picker.Upgrades, Is.EquivalentTo(Preset("tech").Upgrades));
            Assert.That(picker.CanStart, Is.True);
            Assert.That(picker.Status(), Is.EqualTo("Ready."));
        }

        [Test]
        public void PicksAreKeptInTheContentsOrderWhateverOrderTheyWereMadeIn()
        {
            var picker = new LoadoutPicker(C, new Loadout(new string[0], new string[0]), null);
            foreach (var id in Preset("tech").Abilities.AsEnumerable().Reverse()) picker.ToggleAbility(id);
            Assert.That(picker.Abilities, Is.EqualTo(C.AbilityOrder.Where(Preset("tech").Abilities.Contains)));
        }

        [Test]
        public void AnyPickByHandMakesItNoPreset()
        {
            var picker = From("tech");
            picker.ToggleAbility(picker.Abilities.Last());
            Assert.That(picker.PresetId, Is.Null);
            picker = From("tech");
            picker.ToggleUpgrade(picker.Upgrades.Last());
            Assert.That(picker.PresetId, Is.Null);
            picker = From("tech");
            picker.Clear();
            Assert.That(picker.PresetId, Is.Null);
            Assert.That(picker.Abilities, Is.Empty);
            Assert.That(picker.Upgrades, Is.Empty);
        }

        [Test]
        public void NoMoreAbilitiesThanTheRulesAllowAreTaken()
        {
            var picker = From("hybrid");
            Assert.That(picker.Abilities.Count, Is.EqualTo(picker.MaxAbilities));
            var spare = C.AbilityOrder.First(a => !picker.AbilityOn(a));
            Assert.That(picker.CanToggleAbility(spare), Is.False);
            picker.ToggleAbility(spare);
            Assert.That(picker.AbilityOn(spare), Is.False);
            Assert.That(picker.Abilities.Count, Is.EqualTo(picker.MaxAbilities));
            Assert.That(picker.CanToggleAbility(picker.Abilities[0]), Is.True, "one on can always come off");
        }

        [Test]
        public void NoMoreUpgradesThanTheRulesAllowAreTaken()
        {
            var picker = From("tech");
            Assert.That(picker.Upgrades.Count, Is.EqualTo(picker.MaxUpgrades));
            var spare = C.UpgradeOrder.First(u => !picker.UpgradeOn(u) && picker.UpgradeAllowed(u));
            Assert.That(picker.CanToggleUpgrade(spare), Is.False);
            picker.ToggleUpgrade(spare);
            Assert.That(picker.UpgradeOn(spare), Is.False);
        }

        [Test]
        public void AnUpgradeNeedsWhatItRequiresAndTakingTheAbilityOffTakesItToo()
        {
            var picker = new LoadoutPicker(C, new Loadout(new string[0], new string[0]), null);
            var upgrade = C.Upgrades.Select(p => p.Value).First(u => u.Requires.All != null && u.Requires.All.Count == 1 && (u.Requires.Any == null || u.Requires.Any.Count == 0));
            var needed = upgrade.Requires.All![0];
            Assert.That(picker.UpgradeAllowed(upgrade.Id), Is.False);
            Assert.That(picker.CanToggleUpgrade(upgrade.Id), Is.False);
            Assert.That(picker.NeedsOf(upgrade.Id), Is.EqualTo(C.Abilities[needed].Name));
            picker.ToggleUpgrade(upgrade.Id);
            Assert.That(picker.UpgradeOn(upgrade.Id), Is.False, "it cannot be taken without the ability");
            picker.ToggleAbility(needed);
            picker.ToggleUpgrade(upgrade.Id);
            Assert.That(picker.UpgradeOn(upgrade.Id), Is.True);
            picker.ToggleAbility(needed);
            Assert.That(picker.UpgradeOn(upgrade.Id), Is.False, "it went with the ability it needed");
        }

        [Test]
        public void NothingPickedCannotStartAndSaysWhy()
        {
            var picker = From("tech");
            picker.Clear();
            Assert.That(picker.CanStart, Is.False);
            Assert.That(picker.Status(), Is.EqualTo("Pick at least one ability."));
        }

        [Test]
        public void HoldingWave3BackIsInTheFightsSettingsAndOnlyThen()
        {
            var picker = From("tech");
            Assert.That(picker.ToSettings().MaxWaves, Is.Null);
            picker.HoldWave3 = true;
            Assert.That(picker.ToSettings().MaxWaves, Is.EqualTo(2));
        }

        [Test]
        public void TheUtilitiesLineNamesWhatTheKitAdds()
        {
            var picker = new LoadoutPicker(C, new Loadout(new string[0], new string[0]), null);
            var always = C.Utilities.Select(p => p.Value).Where(u => u.RequiresAbility == null).Select(u => u.Name);
            foreach (var name in always) Assert.That(picker.UtilitiesLine(), Does.Contain(name));
            var gated = C.Utilities.Select(p => p.Value).First(u => u.RequiresAbility != null);
            Assert.That(picker.UtilitiesLine(), Does.Not.Contain(gated.Name));
            picker.ToggleAbility(gated.RequiresAbility!);
            Assert.That(picker.UtilitiesLine(), Does.Contain(gated.Name));
        }

        [Test]
        public void ThePicksAreRememberedAndComeBack()
        {
            var prefs = Prefs();
            var picker = From("tech");
            picker.ToggleUpgrade(picker.Upgrades.Last());
            LoadoutPrefs.Save(prefs, picker.ToLoadout(), picker.PresetId);
            var (loadout, preset) = LoadoutPrefs.Load(prefs, C);
            Assert.That(preset, Is.Null);
            Assert.That(loadout.Abilities, Is.EqualTo(picker.Abilities));
            Assert.That(loadout.Upgrades, Is.EqualTo(picker.Upgrades));
        }

        [Test]
        public void ARememberedPresetFollowsItsEditedPicks()
        {
            var prefs = Prefs();
            LoadoutPrefs.Save(prefs, new Loadout(new[] { "sidearm" }, new string[0]), "tech");
            var (loadout, preset) = LoadoutPrefs.Load(prefs, C);
            Assert.That(preset, Is.EqualTo("tech"));
            Assert.That(loadout.Abilities, Is.EqualTo(Preset("tech").Abilities), "the preset's own picks, not the stale ones");
        }

        [Test]
        public void NothingRememberedOpensOnTheDefaultPreset()
        {
            var (loadout, preset) = LoadoutPrefs.Load(Prefs(), C);
            Assert.That(preset, Is.EqualTo(LoadoutPrefs.DefaultPresetId));
            Assert.That(loadout.Abilities, Is.EqualTo(Preset(LoadoutPrefs.DefaultPresetId).Abilities));
        }

        [TestCase("not json")]
        [TestCase("")]
        [TestCase("[]")]
        [TestCase("null")]
        [TestCase("{\"abilities\":\"sidearm\",\"upgrades\":[]}")]
        [TestCase("{\"abilities\":[1,2],\"upgrades\":[]}")]
        [TestCase("{\"abilities\":[null],\"upgrades\":[]}")]
        [TestCase("{\"abilities\":[],\"upgrades\":[]}")]
        [TestCase("{\"abilities\":[\"nope\"],\"upgrades\":[]}")]
        [TestCase("{\"abilities\":[\"sidearm\",\"sidearm\"],\"upgrades\":[]}")]
        [TestCase("{\"abilities\":[\"sidearm\"],\"upgrades\":[\"T4_grapple_pin\"]}")]
        [TestCase("{\"abilities\":{\"a\":1},\"upgrades\":[]}")]
        public void ASavedLoadoutThatIsNotOneIsDroppedForTheDefault(string saved)
        {
            var prefs = Prefs();
            prefs.Set(LoadoutPrefs.LoadoutKey, saved);
            prefs.Set(LoadoutPrefs.PresetKey, "no such preset");
            var (loadout, preset) = LoadoutPrefs.Load(prefs, C);
            Assert.That(preset, Is.EqualTo(LoadoutPrefs.DefaultPresetId));
            Assert.That(Engine.LoadoutProblems(C, loadout), Is.Empty);
        }

        [Test]
        public void AHugeSavedLoadoutIsRefusedNotBuiltInto()
        {
            var prefs = Prefs();
            prefs.Set(LoadoutPrefs.LoadoutKey, "{\"abilities\":[" + string.Join(",", Enumerable.Repeat("\"sidearm\"", 5000)) + "],\"upgrades\":[]}");
            var (_, preset) = LoadoutPrefs.Load(prefs, C);
            Assert.That(preset, Is.EqualTo(LoadoutPrefs.DefaultPresetId));
        }

        [Test]
        public void ContentWithNoPresetsOpensWithNothingPickedInsteadOfFailing()
        {
            var files = ContentLoader.FilesFromDirectory(GameContent.RepoPath("content"));
            files.Root["presets"] = new Newtonsoft.Json.Linq.JArray();
            var none = ContentLoader.Build(files);
            Assert.That(none.Presets, Is.Empty, "the content is valid with no presets");
            var (loadout, preset) = LoadoutPrefs.Load(Prefs(), none);
            Assert.That(preset, Is.Null);
            Assert.That(loadout.Abilities, Is.Empty);
            var picker = new LoadoutPicker(none, loadout, preset);
            Assert.That(picker.CanStart, Is.False);
            Assert.That(picker.Status(), Is.EqualTo("Pick at least one ability."));
            picker.ToggleAbility(none.AbilityOrder[0]);
            Assert.That(picker.CanStart, Is.True, "abilities can still be picked by hand");
        }
    }
}
