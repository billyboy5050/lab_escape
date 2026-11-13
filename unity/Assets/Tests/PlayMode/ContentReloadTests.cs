#nullable enable
using System.Collections;
using System.IO;
using System.Linq;
using LabEscape.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace LabEscape.Client.Tests
{
    /// <summary>Hot reload on the real fight screen: a saved content file restarts the fight, and a bad one changes nothing.</summary>
    public class ContentReloadTests
    {
        private string _telemetry = "";
        private ScratchContent _scratch = null!;

        private static VisualElement Root => Object.FindAnyObjectByType<UIDocument>().rootVisualElement;

        /// <summary>Each test edits a copy of content/, which the game is pointed at for the test and taken off again afterwards.</summary>
        [UnitySetUp]
        public IEnumerator PointTheGameAtACopy()
        {
            _telemetry = Path.Combine(Path.GetTempPath(), "lab-escape-telemetry-" + System.Guid.NewGuid().ToString("N"));
            TelemetrySink.Isolate(_telemetry);
            while (FightDirector.Instance == null || FightDirector.Instance.Content == null) yield return null;
            _scratch = new ScratchContent();
            FightDirector.Instance.WatchContentIn(_scratch.Folder);
        }

        /// <summary>The director outlives a test, so it gets the repository's content back, and its start screen.</summary>
        [TearDown]
        public void PutTheGameBack()
        {
            var d = FightDirector.Instance;
            if (d != null)
            {
                d.SetDebugOpen(false);
                d.WatchContentIn(null);
                d.ShowStartScreen();
            }
            _scratch.Dispose();
            TelemetrySink.Reset();
            if (Directory.Exists(_telemetry)) Directory.Delete(_telemetry, true);
        }

        /// <summary>The game looks twice: a change is read once it has held still.</summary>
        private static void Reload(FightDirector d)
        {
            d.CheckContent();
            d.CheckContent();
        }

        private static IEnumerator WaitUntilIdle(FightDirector d)
        {
            while (!d.Idle && d.Flow!.Live.Outcome == null) yield return null;
            while (d.Flow!.Busy) yield return null;
        }

        private static void StartPreset(FightDirector d, string id)
        {
            var p = d.Content!.Presets.First(x => x.Id == id);
            d.StartFight(new Loadout(p.Abilities, p.Upgrades), new FightSettings(), id);
        }

        /// <summary>A fight with one command played, so a restart can be told from a fight left alone.</summary>
        private static IEnumerator PlayOneTurn(FightDirector d, string preset)
        {
            StartPreset(d, preset);
            yield return WaitUntilIdle(d);
            Assert.That(d.Commit(new EndTurnCommand())!.Ok, Is.True);
            yield return WaitUntilIdle(d);
            Assert.That(d.Flow!.Session.Record.Commands, Is.Not.Empty);
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator ASavedValueRestartsTheFightWithTheNewContent()
        {
            var d = FightDirector.Instance!;
            yield return PlayOneTurn(d, "tech");
            var before = d.Flow;
            var hash = d.Content!.Hash;

            _scratch.SetUnitHp("guard", 7);
            Reload(d);

            Assert.That(d.Content!.Hash, Is.Not.EqualTo(hash));
            Assert.That(d.Content.Units["guard"].Hp, Is.EqualTo(7));
            Assert.That(d.Flow, Is.Not.Null.And.Not.SameAs(before), "a new fight");
            Assert.That(d.Flow!.Session.Record.Commands, Is.Empty, "with a log of its own");
            Assert.That(d.Flow.Session.Record.ContentHash, Is.EqualTo(d.Content.Hash), "recorded against the new content");
            Assert.That(d.ContentProblems, Is.Null);
            yield return WaitUntilIdle(d);
            Assert.That(d.Board!.Units.Count, Is.EqualTo(d.Flow.Live.Units.Count(u => !u.Dead && !u.Docked)), "and the board follows it");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator InvalidContentLeavesTheFightAndTheValuesAlone()
        {
            var d = FightDirector.Instance!;
            d.SetDebugOpen(true);
            yield return PlayOneTurn(d, "tech");
            var before = d.Flow;
            var hash = d.Content!.Hash;
            var commands = before!.Session.Record.Commands.Count;

            _scratch.SetUnitHp("guard", -3);
            Reload(d);

            Assert.That(d.Content!.Hash, Is.EqualTo(hash), "the old values stay");
            Assert.That(d.Flow, Is.SameAs(before), "and the fight goes on");
            Assert.That(d.Flow!.Session.Record.Commands.Count, Is.EqualTo(commands));
            Assert.That(d.ContentProblems, Is.Not.Null.And.Not.Empty);
            yield return null;
            var note = Root.Q<Label>("debug-content");
            Assert.That(note.text, Does.Contain("old values kept").And.Contain("guard"), "the overlay says why");
            Assert.That(note.ClassListContains("error"), Is.True);

            // Put right, the next save reloads and the error goes.
            _scratch.SetUnitHp("guard", 9);
            Reload(d);
            Assert.That(d.Content!.Units["guard"].Hp, Is.EqualTo(9));
            Assert.That(d.Flow, Is.Not.SameAs(before));
            Assert.That(d.ContentProblems, Is.Null);
            yield return null;
            Assert.That(Root.Q<Label>("debug-content").ClassListContains("error"), Is.False);
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator PuttingTheOldValuesBackKeepsTheFightAndClearsTheError()
        {
            var d = FightDirector.Instance!;
            var original = _scratch.Read("units");
            yield return PlayOneTurn(d, "tech");
            var before = d.Flow;

            _scratch.SetUnitHp("guard", -3);
            Reload(d);
            Assert.That(d.ContentProblems, Is.Not.Null);
            _scratch.Write("units", original);
            Reload(d);

            Assert.That(d.ContentProblems, Is.Null, "the error is gone");
            Assert.That(d.Flow, Is.SameAs(before), "and nothing restarted, since the values are the same");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator BrokenJsonAndAMissingFileAreRefusedToo()
        {
            var d = FightDirector.Instance!;
            yield return PlayOneTurn(d, "tech");
            var before = d.Flow;
            var hash = d.Content!.Hash;
            var waves = _scratch.Read("waves");

            _scratch.Write("waves", "[ { \"round\": ");
            Reload(d);
            Assert.That(d.ContentProblems!.Single(), Does.StartWith("waves.json:"));
            _scratch.Write("waves", waves);
            _scratch.Delete("hazards");
            Reload(d);
            Assert.That(d.ContentProblems!.Single(), Does.StartWith("hazards.json:"));

            Assert.That(d.Content!.Hash, Is.EqualTo(hash));
            Assert.That(d.Flow, Is.SameAs(before));
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator AnEditedPresetsPicksAreTheRestartedFightsLoadout()
        {
            var d = FightDirector.Instance!;
            yield return PlayOneTurn(d, "tech");
            var upgrades = d.Content!.Presets.First(p => p.Id == "tech").Upgrades;
            Assert.That(upgrades.Count, Is.GreaterThan(1));

            _scratch.Edit("presets", t =>
            {
                var tech = (JArray)t.First(p => p["id"]?.Value<string>() == "tech")["upgrades"]!;
                tech.RemoveAt(tech.Count - 1);
            });
            Reload(d);

            Assert.That(d.Flow, Is.Not.Null);
            Assert.That(d.Flow!.Session.Record.Loadout.Upgrades, Is.EqualTo(upgrades.Take(upgrades.Count - 1).ToList()));
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator APresetThatIsGoneSendsThePlayerBackToThePicker()
        {
            var d = FightDirector.Instance!;
            yield return PlayOneTurn(d, "tech");

            _scratch.Edit("presets", t =>
            {
                var tech = t.First(p => p["id"]?.Value<string>() == "tech");
                tech.Remove();
            });
            Reload(d);
            yield return null;

            Assert.That(d.Flow, Is.Null, "no fight is left");
            Assert.That(d.Hud.StartVisible, Is.True);
            Assert.That(Root.Q<Label>("start-error").text, Does.Contain("tech preset is gone"));
            var names = Root.Q("presets").Query<Label>(className: "preset-name").ToList().Select(l => l.text).ToList();
            Assert.That(names, Has.None.Contains("TECH"));
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator ALoadoutThatIsNotAPresetIsKeptAcrossAReload()
        {
            var d = FightDirector.Instance!;
            d.StartFight(new Loadout(new[] { "sidearm", "lunge" }, new string[0]), new FightSettings(), null);
            yield return WaitUntilIdle(d);

            _scratch.SetUnitHp("guard", 7);
            Reload(d);

            Assert.That(d.Flow, Is.Not.Null);
            Assert.That(d.Flow!.Session.Record.Loadout.Abilities, Is.EqualTo(new[] { "sidearm", "lunge" }));
            Assert.That(d.Flow.Session.Record.ContentHash, Is.EqualTo(d.Content!.Hash));
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator ALoadoutThatNoLongerFitsSendsThePlayerBackToThePicker()
        {
            var d = FightDirector.Instance!;
            d.StartFight(new Loadout(new[] { "sidearm", "grapple_hook" }, new[] { "T4_grapple_pin" }), new FightSettings(), null);
            yield return WaitUntilIdle(d);

            // The upgrade now asks for a drone as well, which no preset's picks need.
            _scratch.Edit("upgrades", t => ((JArray)t.First(u => u["id"]?.Value<string>() == "T4_grapple_pin")["requires"]!["all"]!).Add("scout_drone"));
            Reload(d);
            yield return null;

            Assert.That(d.ContentProblems, Is.Null, "the content itself is fine");
            Assert.That(d.Flow, Is.Null);
            Assert.That(Root.Q<Label>("start-error").text, Does.Contain("no longer fits"));
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator ASavedFightPlayingBackStopsAtTheStartScreen()
        {
            var d = FightDirector.Instance!;
            d.PlayReplayFile(GameContent.RepoPath("reports", "winning-line-tech.json"));
            yield return null;
            Assert.That(d.Replaying, Is.True);

            _scratch.SetUnitHp("guard", 7);
            Reload(d);
            yield return null;

            Assert.That(d.Replaying, Is.False);
            Assert.That(d.Flow, Is.Null);
            Assert.That(d.Hud.StartVisible, Is.True);
            Assert.That(Root.Q<Label>("start-error").text, Does.Contain("recorded with other values"));
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator TheStartScreenFollowsTheNewContent()
        {
            var d = FightDirector.Instance!;
            d.ShowStartScreen();
            yield return null;

            _scratch.Edit("presets", t => t.First(p => p["id"]?.Value<string>() == "tech")["name"] = "Refit");
            Reload(d);
            yield return null;

            Assert.That(d.Flow, Is.Null);
            Assert.That(Root.Q<Label>("start-error").text, Is.EqualTo("Content reloaded."));
            var names = Root.Q("presets").Query<Label>(className: "preset-name").ToList().Select(l => l.text).ToList();
            Assert.That(names, Has.Some.Contains("REFIT"));
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator TheOverlayHeaderShowsTheNewHash()
        {
            var d = FightDirector.Instance!;
            d.SetDebugOpen(true);
            StartPreset(d, "tech");
            yield return WaitUntilIdle(d);

            _scratch.SetUnitHp("guard", 7);
            Reload(d);
            yield return null;
            yield return null;

            Assert.That(Root.Q<Label>("debug-version").text, Is.EqualTo(DebugReport.Header(d.Content!)));
            Assert.That(Root.Q<Label>("debug-content").text, Does.Contain(_scratch.Folder));
        }
    }
}
