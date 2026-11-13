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
    }
}
