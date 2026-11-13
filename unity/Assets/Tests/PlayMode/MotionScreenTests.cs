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
    /// <summary>The reduce-motion setting on the real screens: its buttons, and what it takes off the screen.</summary>
    public class MotionScreenTests
    {
        private string _telemetry = "";

        [SetUp]
        public void SendTelemetryAside()
        {
            _telemetry = Path.Combine(Path.GetTempPath(), "lab-escape-telemetry-" + System.Guid.NewGuid().ToString("N"));
            TelemetrySink.Isolate(_telemetry);
            Motion.Reduced = false;
        }

        [TearDown]
        public void PutTheGameBack()
        {
            var d = FightDirector.Instance;
            if (d != null)
            {
                d.SetReducedMotion(false);
                d.SetDebugOpen(false);
                d.ShowStartScreen();
            }
            Motion.Reduced = false;
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
        public IEnumerator TheTitleAndTheFightsControlsBothSetItAndRememberIt()
        {
            yield return WaitForDirector();
            var d = FightDirector.Instance!;
            d.ShowTitle();
            yield return null;
            Assert.That(Motion.Reduced, Is.False);
            Assert.That(Root.Q<Button>("title-motion").ClassListContains("on"), Is.False);

            Press("title-motion");
            yield return null;
            Assert.That(Motion.Reduced, Is.True);
            Assert.That(TelemetrySink.Prefs.Get(Motion.PrefKey), Is.EqualTo("1"), "remembered");
            Assert.That(Root.Q<Button>("title-motion").ClassListContains("on"), Is.True);
            Assert.That(Root.Q<Button>("motion").ClassListContains("on"), Is.True, "the fight's button shows it too");

            var p = d.Content!.Presets.First();
            d.StartFight(new Loadout(p.Abilities, p.Upgrades), new FightSettings(), p.Id);
            yield return null;
            Press("motion");
            yield return null;
            Assert.That(Motion.Reduced, Is.False);
            Assert.That(TelemetrySink.Prefs.Get(Motion.PrefKey), Is.EqualTo("0"));
            Assert.That(Root.Q<Button>("motion").ClassListContains("on"), Is.False);
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator TheTitleHasSoundAndMotionSettingsThatFitItsCard()
        {
            yield return WaitForDirector();
            var d = FightDirector.Instance!;
            d.ShowTitle();
            yield return null;
            yield return null;
            var card = Root.Q(className: "title-card").worldBound;
            foreach (var name in new[] { "title-sound", "title-motion" })
            {
                var box = Root.Q(name).worldBound;
                Assert.That(box.width, Is.GreaterThan(0f), name);
                Assert.That(box.xMin, Is.GreaterThanOrEqualTo(card.xMin - 0.5f), name);
                Assert.That(box.xMax, Is.LessThanOrEqualTo(card.xMax + 0.5f), name);
                Assert.That(box.yMax, Is.LessThanOrEqualTo(card.yMax + 0.5f), name);
            }
            var was = d.Muted;
            Press("title-sound");
            yield return null;
            Assert.That(d.Muted, Is.Not.EqualTo(was));
            d.ToggleSound();
            Assert.That(d.Muted, Is.EqualTo(was));
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator TheCameraIsNotKnockedAboutWhenMotionIsReduced()
        {
            yield return WaitForDirector();
            var d = FightDirector.Instance!;
            var p = d.Content!.Presets.First();
            d.StartFight(new Loadout(p.Abilities, p.Upgrades), new FightSettings(), p.Id);
            yield return null;
            var cam = d.IsoCamera;
            cam.Still();
            cam.Tick(0.016f);
            var rest = cam.Cam.transform.position;
            var size = cam.Cam.orthographicSize;

            // Not reduced: a blow shakes, kicks and punches the view.
            cam.Shake(1f);
            cam.Kick(Vector3.right, 0.5f);
            cam.Punch(0.1f);
            for (var i = 0; i < 4; i++) cam.Tick(0.03f);
            Assert.That((cam.Cam.transform.position - rest).magnitude + Mathf.Abs(cam.Cam.orthographicSize - size), Is.GreaterThan(0.001f), "a blow moves the camera");

            cam.Still();
            for (var i = 0; i < 80; i++) cam.Tick(0.03f);
            d.SetReducedMotion(true);
            cam.Tick(0.016f);
            rest = cam.Cam.transform.position;
            size = cam.Cam.orthographicSize;
            cam.Shake(1f);
            cam.Kick(Vector3.right, 0.5f);
            cam.Punch(0.1f);
            for (var i = 0; i < 4; i++) cam.Tick(0.03f);
            Assert.That((cam.Cam.transform.position - rest).magnitude, Is.LessThan(0.0001f));
            Assert.That(cam.Cam.orthographicSize, Is.EqualTo(size).Within(0.0001f));
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator TheFightOpensOnTheWholeRoomWithNoPullBack()
        {
            yield return WaitForDirector();
            var d = FightDirector.Instance!;
            var p = d.Content!.Presets.First();
            d.StartFight(new Loadout(p.Abilities, p.Upgrades), new FightSettings(), p.Id);
            Assert.That(d.IsoCamera.Entering, Is.True, "the entrance pulls back from the player");
            d.SetReducedMotion(true);
            d.StartFight(new Loadout(p.Abilities, p.Upgrades), new FightSettings(), p.Id);
            Assert.That(d.IsoCamera.Entering, Is.False, "with reduced motion it starts on the room");
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator TheEdgesDoNotFlashAndTheLightsDoNotFlickerWhenMotionIsReduced()
        {
            yield return WaitForDirector();
            var d = FightDirector.Instance!;
            d.ShowStartScreen();
            yield return null;
            var alarm = Root.Q("flash-alarm");
            var hurt = Root.Q("flash-hurt");
            d.Hud.Flash(true, true);
            Assert.That(alarm.ClassListContains("on") && hurt.ClassListContains("on"), Is.True, "the edges flash");
            yield return new WaitForSeconds(0.2f);
            Assert.That(alarm.ClassListContains("on") || hurt.ClassListContains("on"), Is.False, "and stop");

            d.SetReducedMotion(true);
            d.Hud.Flash(true, true);
            Assert.That(alarm.ClassListContains("on") || hurt.ClassListContains("on"), Is.False, "they do not flash at all");

            var ambience = new Ambience();
            ambience.Strike();
            Assert.That(Lighting.Ambient.r, Is.EqualTo(1f).Within(0.001f), "the lights come on steady, with no false starts");
            ambience.SoundAlarm();
            ambience.Tick(0.4f);
            Assert.That(Lighting.Ambient.g, Is.EqualTo(1f).Within(0.001f), "and the alarm does not pulse them");

            d.SetReducedMotion(false);
            ambience.Strike();
            Assert.That(Lighting.Ambient.r, Is.LessThan(0.5f), "the first false start of the tube is dark");
            ambience.Steady();
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator TurningItOnPartWayThroughAFlickerOrAJoltSettlesThemAtOnce()
        {
            yield return WaitForDirector();
            var d = FightDirector.Instance!;
            var p = d.Content!.Presets.First();
            d.StartFight(new Loadout(p.Abilities, p.Upgrades), new FightSettings(), p.Id);
            yield return null;
            var ambience = d.Board!.Ambience;

            // The lights are in the middle of the tube's false starts, and an alarm is pulsing.
            ambience.Strike();
            ambience.Tick(0.1f);
            ambience.SoundAlarm();
            Assert.That(Lighting.Ambient.r, Is.LessThan(0.5f), "dark in the first false start");
            d.Hud.JoltHp();

            d.SetReducedMotion(true);
            ambience.Tick(0.016f);
            Assert.That(Lighting.Ambient.r, Is.EqualTo(1f).Within(0.001f), "the lights are steady at the next frame");
            Assert.That(Lighting.Ambient.g, Is.EqualTo(1f).Within(0.001f), "with no alarm pulse");
            ambience.Tick(0.4f);
            Assert.That(Lighting.Ambient.g, Is.EqualTo(1f).Within(0.001f), "and they stay so");

            d.Hud.Tick(0.016f, d.Flow!.Shown);
            var t = Root.Q("hp-cells").style.translate.value;
            Assert.That(t.x.value, Is.EqualTo(0f).Within(0.001f), "the jolt is over");
            Assert.That(t.y.value, Is.EqualTo(0f).Within(0.001f));
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator TheTerminalsPrintAtOnceWhenMotionIsReduced()
        {
            yield return WaitForDirector();
            var d = FightDirector.Instance!;
            d.ShowTitle();
            yield return null;
            Assert.That(d.Hud.TitleTypingDone, Is.False, "it prints a little at a time");

            d.SetReducedMotion(true);
            d.ShowTitle();
            yield return null;
            Assert.That(d.Hud.TitleTypingDone, Is.True);
            Assert.That(d.Hud.TitleTerminal(), Is.EqualTo(ScreenText.TitleLines(d.Content!)));

            d.ShowStartScreen();
            d.SelectPreset(d.Content!.Presets.First());
            d.ShowBriefing();
            yield return null;
            Assert.That(d.Hud.Transmission, Is.EqualTo(BriefingText.Opening(d.Content)));
        }

        [UnityTest]
        [Timeout(60000)]
        public IEnumerator TheHpDoesNotJoltWhenMotionIsReduced()
        {
            yield return WaitForDirector();
            var d = FightDirector.Instance!;
            var p = d.Content!.Presets.First();
            d.StartFight(new Loadout(p.Abilities, p.Upgrades), new FightSettings(), p.Id);
            yield return null;
            var cells = Root.Q("hp-cells");
            d.SetReducedMotion(true);
            d.Hud.JoltHp();
            d.Hud.Tick(0.016f, d.Flow!.Shown);
            var t = cells.style.translate.value;
            Assert.That(t.x.value, Is.EqualTo(0f).Within(0.001f));
            Assert.That(t.y.value, Is.EqualTo(0f).Within(0.001f));
        }
    }
}
