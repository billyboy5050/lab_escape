#nullable enable
using System.Collections;
using System.IO;
using System.Linq;
using LabEscape.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace LabEscape.Client.Tests
{
    /// <summary>The screens a player moves through around a fight: the title, the loadout screen, the briefing and the report.</summary>
    public class ScreenFlowTests
    {
        private string _telemetry = "";

        [SetUp]
        public void SendTelemetryAside()
        {
            _telemetry = Path.Combine(Path.GetTempPath(), "lab-escape-telemetry-" + System.Guid.NewGuid().ToString("N"));
            TelemetrySink.Isolate(_telemetry);
        }

        /// <summary>The director outlives a test, so the debug overlay is closed and the start screen put back.</summary>
        [TearDown]
        public void PutTheGameBack()
        {
            var d = FightDirector.Instance;
            if (d != null)
            {
                d.SetDebugOpen(false);
                d.ShowStartScreen();
            }
            TelemetrySink.Reset();
            if (Directory.Exists(_telemetry)) Directory.Delete(_telemetry, true);
        }

        private static VisualElement Root => Object.FindAnyObjectByType<UIDocument>().rootVisualElement;

        private static IEnumerator WaitForDirector()
        {
            while (FightDirector.Instance == null || FightDirector.Instance.Content == null) yield return null;
        }

        private static void Press(string name)
        {
            var b = Root.Q<Button>(name);
            Assert.That(b.enabledSelf, Is.True, $"{name} can be pressed");
            using (var e = NavigationSubmitEvent.GetPooled())
            {
                e.target = b;
                b.SendEvent(e);
            }
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator TheTitleIsTheFirstScreenAndItsTerminalPrintsItself()
        {
            yield return WaitForDirector();
            var d = FightDirector.Instance!;
            d.ShowTitle();
            yield return null;
            Assert.That(d.CurrentScreen, Is.EqualTo(GameScreen.Title));
            Assert.That(d.Hud.TitleVisible, Is.True);
            Assert.That(d.Hud.StartVisible, Is.False, "and only the title");
            Assert.That(d.Flow, Is.Null);
            Assert.That(Root.Q<Label>("title-room").text, Is.EqualTo(d.Content!.DefaultRoom.Name.ToUpperInvariant()));
            Assert.That(Root.Q<Label>("title-foot").text, Does.Contain(d.Content.Hash));

            // It prints a little at a time, not all at once.
            while (d.Hud.TitleTerminal().All(l => l.Length == 0)) yield return null;
            Assert.That(d.Hud.TitleTypingDone, Is.False);
            Assert.That(d.Hud.TitleTerminal().Sum(l => l.Length), Is.LessThan(ScreenText.TitleLines(d.Content).Sum(l => l.Length)));

            d.Hud.FinishTitleTyping();
            yield return null;
            Assert.That(d.Hud.TitleTerminal(), Is.EqualTo(ScreenText.TitleLines(d.Content)));
            var labels = Root.Q("title-terminal").Query<Label>().ToList();
            Assert.That(labels.Select(l => l.text.TrimEnd('_')), Is.EqualTo(ScreenText.TitleLines(d.Content)));
            Assert.That(labels.Skip(ScreenText.TitleStatusLines).All(l => l.ClassListContains("alert")), Is.True, "the message is the alert");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator TheTitleFitsAndBeginGoesOnToTheLoadoutScreen()
        {
            yield return WaitForDirector();
            var d = FightDirector.Instance!;
            d.ShowTitle();
            d.Hud.FinishTitleTyping();
            yield return null;
            yield return null;
            var card = Root.Q(className: "title-card").worldBound;
            Assert.That(card.width, Is.GreaterThan(100f));
            foreach (var name in new[] { "title-terminal", "title-begin", "title-blurb", "title-foot" })
            {
                var box = Root.Q(name).worldBound;
                Assert.That(box.width, Is.GreaterThan(0f), $"{name} is drawn");
                Assert.That(box.xMin, Is.GreaterThanOrEqualTo(card.xMin - 0.5f), name);
                Assert.That(box.xMax, Is.LessThanOrEqualTo(card.xMax + 0.5f), name);
                Assert.That(box.yMax, Is.LessThanOrEqualTo(card.yMax + 0.5f), name);
            }
            Press("title-begin");
            yield return null;
            Assert.That(d.CurrentScreen, Is.EqualTo(GameScreen.Loadout));
            Assert.That(d.Hud.StartVisible, Is.True);
            Assert.That(d.Hud.TitleVisible, Is.False);
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator StartingAFightPutsTheScreensAway()
        {
            yield return WaitForDirector();
            var d = FightDirector.Instance!;
            d.ShowTitle();
            var p = d.Content!.Presets.First();
            d.StartFight(new Loadout(p.Abilities, p.Upgrades), new FightSettings(), p.Id);
            yield return null;
            Assert.That(d.CurrentScreen, Is.EqualTo(GameScreen.Fight));
            Assert.That(d.Hud.TitleVisible, Is.False);
            Assert.That(d.Hud.StartVisible, Is.False);
        }

        private static void PressButton(Button b)
        {
            Assert.That(b.enabledSelf, Is.True, "the button can be pressed");
            using (var e = NavigationSubmitEvent.GetPooled())
            {
                e.target = b;
                b.SendEvent(e);
            }
        }

        private static System.Collections.Generic.List<Button> Rows(string container) => Root.Q(container).Query<Button>(className: "choice").ToList();

        private static System.Collections.Generic.List<Button> PresetButtons() => Root.Q("presets").Query<Button>(className: "preset-btn").ToList();

        private static IEnumerator OpenLoadout()
        {
            yield return WaitForDirector();
            var d = FightDirector.Instance!;
            d.ShowStartScreen();
            yield return null;
            yield return null;
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator TheLoadoutScreenHasARowForEveryChoiceAndFitsItsCard()
        {
            yield return OpenLoadout();
            var d = FightDirector.Instance!;
            var c = d.Content!;
            Assert.That(d.CurrentScreen, Is.EqualTo(GameScreen.Loadout));
            Assert.That(Rows("abilities").Count, Is.EqualTo(c.AbilityOrder.Count));
            Assert.That(Rows("upgrades").Count, Is.EqualTo(c.UpgradeOrder.Count));
            Assert.That(PresetButtons().Count, Is.EqualTo(c.Presets.Count + 1), "a button for each preset, and Clear");
            var card = Root.Q("start-card").worldBound;
            Assert.That(card.width, Is.GreaterThan(100f));
            foreach (var row in Rows("abilities").Concat(Rows("upgrades")).Concat(PresetButtons()))
            {
                var box = row.worldBound;
                Assert.That(box.width, Is.GreaterThan(0f), "drawn");
                Assert.That(box.xMin, Is.GreaterThanOrEqualTo(card.xMin - 0.5f));
                Assert.That(box.xMax, Is.LessThanOrEqualTo(card.xMax + 0.5f));
            }
            foreach (var name in new[] { "start-dock", "start-fight", "back-title", "dock-status", "loadout-body" })
            {
                var box = Root.Q(name).worldBound;
                Assert.That(box.width, Is.GreaterThan(0f), $"{name} is drawn");
                Assert.That(box.xMin, Is.GreaterThanOrEqualTo(card.xMin - 0.5f), name);
                Assert.That(box.xMax, Is.LessThanOrEqualTo(card.xMax + 0.5f), name);
                Assert.That(box.yMax, Is.LessThanOrEqualTo(card.yMax + 0.5f), name);
            }
            Assert.That(Root.Q("loadout-body").worldBound.height, Is.GreaterThan(150f), "the picks have room");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator APresetFillsThePicksAndMarksItselfAndNothingStarts()
        {
            yield return OpenLoadout();
            var d = FightDirector.Instance!;
            var c = d.Content!;
            PressButton(PresetButtons()[1]);
            yield return null;
            var preset = c.Presets[1];
            Assert.That(d.Picker!.PresetId, Is.EqualTo(preset.Id));
            Assert.That(d.Flow, Is.Null, "choosing a preset does not start a fight");
            Assert.That(PresetButtons().Select(b => b.ClassListContains("on")), Is.EqualTo(Enumerable.Range(0, c.Presets.Count + 1).Select(i => i == 1)));
            var on = Rows("abilities").Select((b, i) => (b, id: c.AbilityOrder[i])).Where(x => x.b.ClassListContains("on")).Select(x => x.id).ToList();
            Assert.That(on, Is.EquivalentTo(preset.Abilities));
            var upgradesOn = Rows("upgrades").Select((b, i) => (b, id: c.UpgradeOrder[i])).Where(x => x.b.ClassListContains("on")).Select(x => x.id).ToList();
            Assert.That(upgradesOn, Is.EquivalentTo(preset.Upgrades));
            Assert.That(Root.Q<Label>("ability-count").text, Is.EqualTo($"{preset.Abilities.Count}/{c.Rules.Loadout.Abilities}"));
            Assert.That(Root.Q<Label>("dock-status").text, Is.EqualTo("Ready."));
            Assert.That(Root.Q<Button>("start-fight").enabledSelf, Is.True);
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator PickingByHandUnmarksThePresetAndAFullKitTakesTheRestOff()
        {
            yield return OpenLoadout();
            var d = FightDirector.Instance!;
            var c = d.Content!;
            var hybrid = c.Presets.First(p => p.Id == "hybrid");
            d.SelectPreset(hybrid);
            yield return null;
            Assert.That(hybrid.Abilities.Count, Is.EqualTo(c.Rules.Loadout.Abilities), "the hybrid preset fills the kit");
            var rows = Rows("abilities");
            var offRows = rows.Where((b, i) => !hybrid.Abilities.Contains(c.AbilityOrder[i])).ToList();
            Assert.That(offRows, Is.Not.Empty);
            Assert.That(offRows.All(b => !b.enabledSelf), Is.True, "no room for more");
            Assert.That(rows.Where((b, i) => hybrid.Abilities.Contains(c.AbilityOrder[i])).All(b => b.enabledSelf), Is.True, "those picked can come off");

            var first = rows.Where((b, i) => hybrid.Abilities.Contains(c.AbilityOrder[i])).First();
            PressButton(first);
            yield return null;
            Assert.That(d.Picker!.PresetId, Is.Null);
            Assert.That(PresetButtons().All(b => !b.ClassListContains("on")), Is.True);
            Assert.That(offRows.All(b => b.enabledSelf), Is.True, "there is room again");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator AnUpgradeThatNeedsAnAbilityYouLackIsOffAndSaysWhatItNeeds()
        {
            yield return OpenLoadout();
            var d = FightDirector.Instance!;
            var c = d.Content!;
            PressButton(PresetButtons().Last());
            yield return null;
            Assert.That(d.Picker!.Abilities, Is.Empty, "Clear takes everything off");
            Assert.That(Root.Q<Label>("dock-status").text, Is.EqualTo("Pick at least one ability."));
            Assert.That(Root.Q<Button>("start-fight").enabledSelf, Is.False);
            var upgrades = Rows("upgrades");
            Assert.That(upgrades.All(b => !b.enabledSelf), Is.True, "nothing meets its requirements");
            var needs = Root.Q("upgrades").Query<Label>(className: "choice-needs").ToList();
            Assert.That(needs.All(l => l.text.StartsWith("Needs ")), Is.True);
            var id = c.UpgradeOrder[0];
            Assert.That(needs[0].text, Is.EqualTo("Needs " + Engine.DescribeRequires(c, c.Upgrades[id].Requires)));
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator StartFightStartsTheFightWithThePicksAndTheDeploymentSettings()
        {
            yield return OpenLoadout();
            var d = FightDirector.Instance!;
            var c = d.Content!;
            var tech = c.Presets.First(p => p.Id == "tech");
            d.SelectPreset(tech);
            Assert.That(Root.Q<Button>("hold-wave").ClassListContains("on"), Is.False);
            PressButton(Root.Q<Button>("hold-wave"));
            yield return null;
            Assert.That(Root.Q<Button>("hold-wave").ClassListContains("on"), Is.True);
            PressButton(Root.Q<Button>("start-fight"));
            yield return null;
            Assert.That(d.CurrentScreen, Is.EqualTo(GameScreen.Fight));
            var record = d.Flow!.Session.Record;
            Assert.That(record.Loadout.Abilities, Is.EquivalentTo(tech.Abilities));
            Assert.That(record.Loadout.Upgrades, Is.EquivalentTo(tech.Upgrades));
            Assert.That(record.Settings.MaxWaves, Is.EqualTo(2));
            Assert.That(record.Meta?.Preset, Is.EqualTo("tech"));
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator ThePicksAreRememberedForNextTime()
        {
            yield return OpenLoadout();
            var d = FightDirector.Instance!;
            var c = d.Content!;
            d.SelectPreset(c.Presets.First(p => p.Id == "alien"));
            var saved = TelemetrySink.Prefs.Get(LoadoutPrefs.LoadoutKey);
            Assert.That(saved, Is.Not.Null);
            Assert.That(TelemetrySink.Prefs.Get(LoadoutPrefs.PresetKey), Is.EqualTo("alien"));
            var (loadout, preset) = LoadoutPrefs.Load(TelemetrySink.Prefs, c);
            Assert.That(preset, Is.EqualTo("alien"));
            Assert.That(loadout.Abilities, Is.EquivalentTo(c.Presets.First(p => p.Id == "alien").Abilities));
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator BackGoesToTheTitle()
        {
            yield return OpenLoadout();
            var d = FightDirector.Instance!;
            PressButton(Root.Q<Button>("back-title"));
            yield return null;
            Assert.That(d.CurrentScreen, Is.EqualTo(GameScreen.Title));
            Assert.That(d.Hud.TitleVisible, Is.True);
            Assert.That(d.Hud.StartVisible, Is.False);
        }
    }
}