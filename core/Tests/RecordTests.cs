#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LabEscape.Sim;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static LabEscape.Core.Tests.Helpers;

namespace LabEscape.Core.Tests
{
    /// <summary>Ported from tests/record.test.ts.</summary>
    public class RecordTests
    {
        private static JObject Good()
        {
            var meta = new RecordMeta { Created = "2026-10-04T00:00:00.000Z", Label = "client", Preset = "tech", TurnTimesMs = new List<double> { 4000, 6500 } };
            var rec = Records.NewRecord(C, LoadoutOf("sidearm", "proximity_mine"), new FightSettings { MaxWaves = 2 }, meta);
            rec.Commands.Add(Move("B7"));
            rec.Commands.Add(EndTurn);
            rec.Commands.Add(new DebugForceWave());
            return rec.ToToken();
        }

        /// <summary>A record with one piece of the file replaced, the way a hand-edited or truncated file would look.</summary>
        private static JObject Broken(Action<JObject> edit)
        {
            var r = Good();
            edit(r);
            return r;
        }

        private static JObject WithCommand(string json) => Broken(r => r["commands"] = new JArray(JToken.Parse(json)));

        private static string Message(JToken? raw) => Assert.Throws<ReplayMismatch>(() => Records.CheckCompatible(C, raw))!.Message;

        [Test]
        public void AcceptsARecordTheAppWroteWithOrWithoutItsMetadata()
        {
            Assert.That(Records.RecordProblems(Good()), Is.Empty);
            Assert.DoesNotThrow(() => Records.CheckCompatible(C, JsonIn.Parse(Good().ToString())));
            Assert.That(Records.RecordProblems(Broken(r => r.Remove("meta"))), Is.Empty);
            Assert.That(Records.RecordProblems(Broken(r => r["meta"] = JObject.Parse("{bot:'greedy',seed:7}"))), Is.Empty);
            foreach (var view in new[] { "top", "iso", "both" }) Assert.That(Records.RecordProblems(Broken(r => r["meta"] = new JObject { ["view"] = view })), Is.Empty);
        }

        private static IEnumerable<TestCaseData> NotReplays()
        {
            yield return new TestCaseData(JValue.CreateNull()).SetName("rejects null");
            yield return new TestCaseData(new JValue("replay")).SetName("rejects text");
            yield return new TestCaseData(new JValue(7)).SetName("rejects a number");
            yield return new TestCaseData(new JArray()).SetName("rejects a list");
            yield return new TestCaseData(null).SetName("rejects a missing value");
        }

        [TestCaseSource(nameof(NotReplays))]
        public void RejectsWhatIsNotAReplayFileAtAll(JToken? raw)
        {
            Assert.That(Message(raw), Does.Match("(?i)not a replay file"));
        }

        private static TestCaseData Case(string name, Action<JObject> edit, string field) => new TestCaseData(edit, field).SetName("rejects a file where " + name);

        private static IEnumerable<TestCaseData> BrokenFiles()
        {
            yield return Case("loadout is missing", r => r.Remove("loadout"), "loadout");
            yield return Case("loadout is null", r => r["loadout"] = JValue.CreateNull(), "loadout");
            yield return Case("loadout has no abilities", r => r["loadout"] = JObject.Parse("{upgrades:[]}"), "loadout.abilities");
            yield return Case("loadout upgrades are not a list", r => r["loadout"] = JObject.Parse("{abilities:[],upgrades:'x'}"), "loadout.upgrades");
            yield return Case("an ability is not a name", r => r["loadout"] = JObject.Parse("{abilities:['sidearm',3],upgrades:[]}"), "loadout.abilities[1]");
            yield return Case("settings is missing", r => r.Remove("settings"), "settings");
            yield return Case("settings is a list", r => r["settings"] = new JArray(), "settings");
            yield return Case("maxWaves is not a number", r => r["settings"] = JObject.Parse("{maxWaves:'2'}"), "settings.maxWaves");
            yield return Case("maxWaves is zero", r => r["settings"] = JObject.Parse("{maxWaves:0}"), "settings.maxWaves");
            yield return Case("commands is missing", r => r.Remove("commands"), "commands");
            yield return Case("commands is not a list", r => r["commands"] = JObject.Parse("{length:3}"), "commands");
            yield return Case("a command is null", r => r["commands"] = JArray.Parse("[{type:'endTurn'},null]"), "commands[1]");
            yield return Case("a command has no type", r => r["commands"] = JArray.Parse("[{path:['B7']}]"), "commands[0]");
            yield return Case("a command has an unknown type", r => r["commands"] = JArray.Parse("[{type:'teleport'}]"), "commands[0]");
            yield return Case("a debug command has an unknown op", r => r["commands"] = JArray.Parse("[{type:'debug',op:'godMode'}]"), "commands[0]");
            yield return Case("meta is not an object", r => r["meta"] = "x", "meta");
            yield return Case("turn times are not a list", r => r["meta"] = JObject.Parse("{turnTimesMs:5}"), "meta.turnTimesMs");
            yield return Case("a turn time is not a number", r => r["meta"] = JObject.Parse("{turnTimesMs:[1,'slow']}"), "meta.turnTimesMs[1]");
            yield return Case("the board view is not one of the views", r => r["meta"] = JObject.Parse("{view:'sideways'}"), "meta.view");
        }

        [TestCaseSource(nameof(BrokenFiles))]
        public void NamesTheFieldInsteadOfCrashingLater(Action<JObject> edit, string field)
        {
            var raw = Broken(edit);
            Assert.That(string.Join("\n", Records.RecordProblems(raw)), Does.Contain(field));
            Assert.That(Message(raw), Does.Contain(field));
        }

        [Test]
        public void ChecksTheFormatEngineVersionAndContentHashFirstSoAnOldFileGetsTheUsefulMessage()
        {
            Assert.That(Message(Broken(r => { r["engineVersion"] = "0.0.1"; r.Remove("loadout"); })), Does.Match("recorded on engine 0\\.0\\.1; this is "));
            Assert.That(Message(Broken(r => { r["contentHash"] = "zzzz"; r.Remove("commands"); })), Does.Contain("recorded with content zzzz"));
            Assert.That(Message(Broken(r => { r["format"] = "other@2"; r.Remove("settings"); })), Does.Contain("Unknown replay format \"other@2\""));
            Assert.That(Good()["engineVersion"]!.Value<string>(), Is.EqualTo(GameStates.EngineVersion));
        }

        [Test]
        public void ListsAFewProblemsAndSaysThereAreMoreRatherThanPrintingOnePerBadCommand()
        {
            var text = Message(Broken(r => r["commands"] = new JArray(Enumerable.Range(0, 50).Select(_ => JObject.Parse("{type:'nope'}")))));
            Assert.That(text.Split('\n').Length, Is.LessThan(12));
            Assert.That(text, Does.Contain("further problems not shown"));
        }

        [TestCase("{type:'debug',op:'setAp',ap:'oops'}", "commands[0].ap must be a number (got \"oops\")")]
        [TestCase("{type:'debug',op:'setAp'}", "commands[0].ap must be a number")]
        [TestCase("{type:'debug',op:'setAp',ap:null}", "commands[0].ap must be a number")]
        [TestCase("{type:'debug',op:'setAp',ap:3,movement:'x'}", "commands[0].movement must be a number")]
        [TestCase("{type:'debug',op:'setHp',unit:'1',value:5}", "commands[0].unit must be a whole number")]
        [TestCase("{type:'debug',op:'setHp',unit:1.5,value:5}", "commands[0].unit must be a whole number")]
        [TestCase("{type:'debug',op:'setHp',unit:1}", "commands[0].value must be a number")]
        [TestCase("{type:'debug',op:'spawn',target:'D5'}", "commands[0].def must be text")]
        [TestCase("{type:'move',path:'B7'}", "commands[0].path must be a list of tile names")]
        [TestCase("{type:'move',path:['B7',3]}", "commands[0].path must be a list of tile names")]
        [TestCase("{type:'move'}", "commands[0].path must be a list of tile names")]
        [TestCase("{type:'ability',target:'B5'}", "commands[0].ability must be text")]
        [TestCase("{type:'ability',ability:'sidearm',target:5}", "commands[0].target must be text")]
        [TestCase("{type:'ability',ability:'barrier_shield',target:'E5',target2:{}}", "commands[0].target2 must be text")]
        [TestCase("{type:'ability',ability:'grapple_hook',target:'B5',mode:'sideways'}", "commands[0].mode must be \"self\" or \"unit\"")]
        [TestCase("{type:'reload'}", "commands[0].target must be text")]
        [TestCase("{type:'pickUpMine',target:7}", "commands[0].target must be text")]
        [TestCase("{type:'redeploy',drone:1.5,target:'C7'}", "commands[0].drone must be a whole number")]
        public void RejectsACommandPayloadTheEngineWouldNotRefuse(string json, string problem)
        {
            Assert.That(Records.RecordProblems(WithCommand(json)), Has.Some.Contains(problem));
            Assert.That(Message(WithCommand(json)), Does.Contain("malformed"));
        }

        [TestCase("{type:'sprint'}")]
        [TestCase("{type:'endTurn'}")]
        [TestCase("{type:'move',path:['B7','B6']}")]
        [TestCase("{type:'ability',ability:'sidearm',target:'B5'}")]
        [TestCase("{type:'ability',ability:'barrier_shield',target:'E5',target2:'D6'}")]
        [TestCase("{type:'ability',ability:'sidearm',target:'B5',target2:null}")]
        [TestCase("{type:'ability',ability:'grapple_hook',target:'B5',mode:'self'}")]
        [TestCase("{type:'ability',ability:'grapple_hook',target:'B5',mode:'unit'}")]
        [TestCase("{type:'reload',target:'C6'}")]
        [TestCase("{type:'pickUpMine',target:'B7'}")]
        [TestCase("{type:'redeploy',drone:4,target:'C7'}")]
        [TestCase("{type:'debug',op:'setHp',unit:0,value:3}")]
        [TestCase("{type:'debug',op:'setAp',ap:5}")]
        [TestCase("{type:'debug',op:'setAp',ap:5,movement:4}")]
        [TestCase("{type:'debug',op:'spawn',def:'guard',target:'D5'}")]
        [TestCase("{type:'debug',op:'forceWave'}")]
        public void AcceptsAValidCommand(string json)
        {
            Assert.That(Records.RecordProblems(WithCommand(json)), Is.Empty);
        }

        [Test]
        public void AcceptsEveryReplayFileTheRepositoryShips()
        {
            var files = Directory.GetFiles(Path.Combine(RepoRoot, "reports"), "winning-line-*.json");
            Assert.That(files, Is.Not.Empty);
            foreach (var f in files) Assert.That(Records.RecordProblems(JsonIn.Parse(File.ReadAllText(f))), Is.Empty, f);
        }

        [Test]
        public void ReadsBackAWinningLineAndReplaysItToAWin()
        {
            foreach (var f in Directory.GetFiles(Path.Combine(RepoRoot, "reports"), "winning-line-*.json"))
            {
                var token = JsonIn.Parse(File.ReadAllText(f));
                var rec = Records.CheckCompatible(C, token);
                Assert.That(Records.Replay(C, rec).State.Outcome?.Result, Is.EqualTo(FightResult.Win), f);
                Assert.That(rec.ToJson(), Is.EqualTo(File.ReadAllText(f)), "a record read from a file writes back exactly");
            }
        }

        [Test]
        public void AcceptsTheRecordsOfRealBotFightsWhichUseEveryCommandThePlayerCanIssue()
        {
            foreach (var p in C.Presets)
            {
                foreach (var seed in new[] { 1, 2 })
                {
                    var record = Runner.RunFight(C, new Loadout(p.Abilities, p.Upgrades), Batch.MakeBot(Batch.GreedyKind, seed)).Record;
                    Assert.That(record.Commands.Count, Is.GreaterThan(5));
                    Assert.That(Records.RecordProblems(JsonIn.Parse(record.ToJson())), Is.Empty, $"{p.Id} seed {seed}");
                }
            }
        }

        [Test]
        public void AVeryLargeMaxWavesIsAcceptedAndHoldsNothingBack()
        {
            var rec = Records.CheckCompatible(C, Broken(r => { r["settings"] = JObject.Parse("{maxWaves:1e300}"); r["commands"] = new JArray(); }));
            Assert.That(rec.Settings.MaxWaves, Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void ARecordWithNoMaxWavesWritesAnEmptySettingsObject()
        {
            var rec = Records.NewRecord(C, LoadoutOf("sidearm"));
            Assert.That(rec.ToToken()["settings"]!.ToString(Newtonsoft.Json.Formatting.None), Is.EqualTo("{}"));
        }
    }
}
