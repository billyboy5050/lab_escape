#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static LabEscape.Core.Tests.Helpers;

namespace LabEscape.Core.Tests
{
    public class ContentTests
    {
        private static ContentFiles Edited(Action<ContentFiles> fn)
        {
            var f = FreshFiles();
            fn(f);
            return f;
        }

        private static List<string> Problems(Action<ContentFiles> fn) => ContentLoader.Validate(Edited(fn));

        private static JObject Item(JToken? list, string id) => (JObject)((JArray)list!).First(x => x["id"]!.Value<string>() == id);
        private static JObject AbilityOf(ContentFiles f, string id) => Item(f["abilities"]!["abilities"], id);
        private static JObject UpgradeOf(ContentFiles f, string id) => Item(f["upgrades"], id);
        private static JObject UnitOf(ContentFiles f, string id) => Item(f["units"], id);
        private static JObject UtilityOf(ContentFiles f, string id) => Item(f["abilities"]!["utilities"], id);
        private static JObject HazardOf(ContentFiles f, string id) => Item(f["hazards"], id);
        private static JObject MapHazardOf(ContentFiles f, string id) => Item(f["map"]!["hazards"], id);
        private static JObject Rules(ContentFiles f) => (JObject)f["rules"]!;

        private static void Drop(JObject o, string path)
        {
            var keys = path.Split('.');
            JToken node = o;
            for (var i = 0; i < keys.Length - 1; i++) node = node[keys[i]]!;
            ((JObject)node).Remove(keys[keys.Length - 1]);
        }

        private static IEnumerable<object[]> Pairs(OrderedMap<string, string[]> table)
        {
            foreach (var pair in table)
            {
                foreach (var path in pair.Value) yield return new object[] { pair.Key, path };
            }
        }

        [Test]
        public void AcceptsTheShippedContent() => Assert.That(ContentLoader.Validate(FreshFiles()), Is.Empty);

        [TestCase("spore_pod")]
        [TestCase("parasite")]
        public void RejectsAnEditThatRemovesTheStatusOfAnAbility(string id)
        {
            var f = Edited(x => AbilityOf(x, id).Remove("status"));
            Assert.That(ContentLoader.Validate(f), Is.EqualTo(new[] { $"abilities.{id}.status is missing" }));
            Assert.Throws<ContentError>(() => ContentLoader.Build(f));
        }

        [TestCase("A1_spore_burst")]
        [TestCase("H1_spore_mines")]
        [TestCase("H2_plague_drone")]
        public void RejectsAnEditThatRemovesTheStatusOfAnUpgrade(string id)
        {
            Assert.That(Problems(x => UpgradeOf(x, id).Remove("status")), Is.EqualTo(new[] { $"upgrades.{id}.status is missing" }));
        }

        [Test]
        public void RejectsAnUnknownStatusId()
        {
            var problems = Problems(x => AbilityOf(x, "spore_pod")["status"] = JToken.FromObject(new { id = "burning", duration = 3 }));
            Assert.That(problems, Is.EqualTo(new[] { "abilities.spore_pod.status has unknown status \"burning\"" }));
        }

        private static IEnumerable<JToken> BadDurations() => new JToken[] { new JValue(0), new JValue(-1), new JValue(1.5), new JValue(double.NaN), new JValue("3") };

        [TestCaseSource(nameof(BadDurations))]
        public void RejectsABadStatusDuration(JToken duration)
        {
            var problems = Problems(x => AbilityOf(x, "parasite")["status"] = new JObject { ["id"] = "parasite", ["duration"] = duration });
            Assert.That(problems, Has.Count.EqualTo(1));
            Assert.That(problems[0], Does.Contain("abilities.parasite.status.duration must be an integer >= 1"));
        }

        [Test]
        public void ChecksTheShapeOfAnOptionalUpgradeStatusThatIsPresentButDoesNotRequireOne()
        {
            Assert.That(Problems(x => UpgradeOf(x, "T4_grapple_pin")["status"] = JToken.FromObject(new { id = "pinned", duration = 0 })), Has.Count.EqualTo(1));
            Assert.That(Problems(x => UpgradeOf(x, "T4_grapple_pin").Remove("status")), Is.Empty);
        }

        [Test]
        public void DoesNotThrowOnANonObjectStatus()
        {
            Assert.That(Problems(x => AbilityOf(x, "spore_pod")["status"] = JValue.CreateNull()), Is.Not.Empty);
        }

        private static IEnumerable<object[]> AbilityPairs() => Pairs(RequiredFields.Ability);
        private static IEnumerable<object[]> UnitPairs() => Pairs(RequiredFields.Unit);
        private static IEnumerable<object[]> UpgradePairs() => Pairs(RequiredFields.Upgrade);
        private static IEnumerable<object[]> UtilityPairs() => Pairs(RequiredFields.Utility);
        private static IEnumerable<object[]> BlockPairs() => Pairs(RequiredFields.Block);

        [TestCaseSource(nameof(AbilityPairs))]
        public void RejectsAnAbilityWithoutARequiredField(string id, string path)
        {
            var f = Edited(x => Drop(AbilityOf(x, id), path));
            Assert.That(ContentLoader.Validate(f), Does.Contain($"abilities.{id}.{path} is missing"));
            Assert.Throws<ContentError>(() => ContentLoader.Build(f));
        }

        [TestCaseSource(nameof(UnitPairs))]
        public void RejectsAUnitWithoutARequiredField(string id, string path)
        {
            Assert.That(Problems(x => Drop(UnitOf(x, id), path)), Does.Contain($"units.{id}.{path} is missing"));
        }

        [TestCaseSource(nameof(UpgradePairs))]
        public void RejectsAnUpgradeWithoutARequiredField(string id, string path)
        {
            Assert.That(Problems(x => Drop(UpgradeOf(x, id), path)), Does.Contain($"upgrades.{id}.{path} is missing"));
        }

        [TestCaseSource(nameof(UtilityPairs))]
        public void RejectsAUtilityWithoutARequiredField(string id, string path)
        {
            Assert.That(Problems(x => Drop(UtilityOf(x, id), path)), Does.Contain($"utilities.{id}.{path} is missing"));
        }

        [TestCase("panel_grid")]
        [TestCase("gas_vent")]
        public void RejectsTheAreaHazardWithoutARadius(string id)
        {
            Assert.That(Problems(x => HazardOf(x, id).Remove("radius")), Does.Contain($"hazards.{id}.radius is missing"));
        }

        [Test]
        public void RejectsAHazardWithAMissingOrUnknownShape()
        {
            foreach (var shape in new[] { null, "cone", "toString" })
            {
                var problems = Problems(x =>
                {
                    if (shape == null) HazardOf(x, "wall_gun").Remove("shape");
                    else HazardOf(x, "wall_gun")["shape"] = shape;
                });
                Assert.That(problems, Has.Count.EqualTo(1));
                Assert.That(problems[0], Does.Contain("hazards.wall_gun.shape must be one of"));
            }
            Assert.That(RequiredFields.HazardShape.Keys, Is.EqualTo(new[] { "lane", "adjacentToTiles" }));
        }

        [TestCaseSource(nameof(BlockPairs))]
        public void RejectsAnyUnitWithABlockButNotTheFieldsThatBlockNeeds(string block, string path)
        {
            var id = RequiredFields.Unit.First(u => u.Value.Contains(block)).Key;
            Assert.That(Problems(x => Drop(UnitOf(x, id), path)), Does.Contain($"units.{id}.{path} is missing"));
            // A different unit that gains the block is held to the same fields.
            var problems = Problems(x => UnitOf(x, "guard")[block] = UnitOf(x, id)[block]!.DeepClone());
            Assert.That(problems, Does.Contain($"units.guard.{path} is missing"));
        }

        [Test]
        public void RejectsAProximityMineWithNoBlastANullBlastOrABadBlastRadius()
        {
            Assert.That(Problems(x => AbilityOf(x, "proximity_mine").Remove("blast")), Is.EqualTo(new[] { "abilities.proximity_mine.blast is missing" }));
            Assert.That(Problems(x => AbilityOf(x, "proximity_mine")["blast"] = JValue.CreateNull()), Does.Contain("abilities.proximity_mine.blast is missing"));
            var bad = Problems(x => AbilityOf(x, "proximity_mine")["blast"]!["radius"] = -1);
            Assert.That(bad, Has.Count.EqualTo(1));
            Assert.That(bad[0], Does.Contain("abilities.proximity_mine.blast.radius must be an integer >= 0"));
        }

        [TestCase("sprint")]
        [TestCase("reload")]
        [TestCase("pick_up_mine")]
        [TestCase("redeploy")]
        public void RejectsContentWithoutAUtility(string id)
        {
            var problems = Problems(x =>
            {
                var list = (JArray)x["abilities"]!["utilities"]!;
                list.Remove(list.First(u => u["id"]!.Value<string>() == id));
            });
            Assert.That(problems, Does.Contain($"abilities.json is missing utility \"{id}\""));
        }

        [Test]
        public void ChecksTheNumbersTheAiDoesArithmeticOn()
        {
            var problems = Problems(x =>
            {
                UnitOf(x, "flamer")["sweep"]!["length"] = double.NaN;
                UnitOf(x, "medic")["heal"]!["amount"] = 0;
                UnitOf(x, "burster")["blast"]!["radius"] = 1.5;
            });
            Assert.That(problems, Has.Count.EqualTo(3));
        }

        [Test]
        public void RejectsContentWhereTheUtilityEntryAndRulesSprintDisagree()
        {
            Assert.That(Problems(x => Rules(x)["sprint"]!["ap"] = 2), Is.EqualTo(new[] { "utilities.sprint.ap (1) must equal rules.sprint.ap (2)" }));
            Assert.That(Problems(x => Rules(x)["sprint"]!["movement"] = 3), Is.EqualTo(new[] { "utilities.sprint.movement (2) must equal rules.sprint.movement (3)" }));
        }

        [Test]
        public void OffersChargesAndShowsTheSameCostWhenBothAreTunedTogether()
        {
            var c = ContentWith(("rules.sprint.ap", 2), ("abilities.utilities.sprint.ap", 2));
            ActionStatus Sprint(int ap) => Action(c, Scenario(new ScenarioSpec { Ap = ap, Content = c }), "sprint");
            Assert.That(Sprint(1).Ap, Is.EqualTo(2));
            Assert.That(Sprint(1).Usable, Is.False);
            Assert.That(Sprint(2).Ap, Is.EqualTo(2));
            Assert.That(Sprint(2).Usable, Is.True);
            var s = Scenario(new ScenarioSpec { Ap = 2, Content = c });
            Assert.That(Run(s, new SprintCommand(), c).State.Ap, Is.EqualTo(0));
        }

        [Test]
        public void RejectsAnArmorAppliesToThatIsRemovedNullOrNotAList()
        {
            var expected = new[] { "rules.armorAppliesTo must be a list of damage types" };
            Assert.That(Problems(x => Rules(x).Remove("armorAppliesTo")), Is.EqualTo(expected));
            Assert.That(Problems(x => Rules(x)["armorAppliesTo"] = JValue.CreateNull()), Is.EqualTo(expected));
            Assert.That(Problems(x => Rules(x)["armorAppliesTo"] = "kinetic"), Is.EqualTo(expected));
        }

        [Test]
        public void StillChecksEachArmorEntryAndAcceptsAnEmptyList()
        {
            Assert.That(Problems(x => Rules(x)["armorAppliesTo"] = new JArray("kinetic", "fire")), Is.Empty);
            Assert.That(Problems(x => Rules(x)["armorAppliesTo"] = new JArray()), Is.Empty);
            Assert.That(Problems(x => Rules(x)["armorAppliesTo"] = new JArray("kinetic", "plasma")), Is.EqualTo(new[] { "rules.armorAppliesTo[1] has unknown damage type \"plasma\"" }));
        }

        private static IEnumerable<JToken> BadDirections() => new JToken[] { new JValue("Q"), new JValue("w"), new JValue("north"), new JValue(5) };

        [TestCaseSource(nameof(BadDirections))]
        public void RejectsABadLaneDirection(JToken dir)
        {
            var f = Edited(x => MapHazardOf(x, "gun")["direction"] = dir);
            Assert.That(ContentLoader.Validate(f), Is.EqualTo(new[] { $"map hazard gun has unknown direction {Hash.Stringify(dir)} (use N, E, S or W)" }));
            Assert.Throws<ContentError>(() => ContentLoader.Build(f));
        }

        [Test]
        public void StillRequiresADirectionOnALane()
        {
            Assert.That(Problems(x => MapHazardOf(x, "gun").Remove("direction")), Is.EqualTo(new[] { "map hazard gun is a lane and needs a direction" }));
        }

        [TestCase("N")]
        [TestCase("E")]
        [TestCase("S")]
        [TestCase("W")]
        public void AcceptsACompassDirection(string dir) => Assert.That(Problems(x => MapHazardOf(x, "gun")["direction"] = dir), Is.Empty);

        [TestCase("gun")]
        [TestCase("panel")]
        [TestCase("vent_b5")]
        public void RejectsAHazardWithNoTiles(string id)
        {
            var f = Edited(x => MapHazardOf(x, id)["tiles"] = new JArray());
            Assert.That(ContentLoader.Validate(f), Is.EqualTo(new[] { $"map hazard {id} needs at least one tile" }));
            Assert.Throws<ContentError>(() => ContentLoader.Build(f));
        }

        [Test]
        public void RejectsAHazardWhoseTilesAreMissingAltogether()
        {
            Assert.That(Problems(x => MapHazardOf(x, "gun").Remove("tiles")), Is.EqualTo(new[] { "map hazard gun needs at least one tile" }));
        }

        [Test]
        public void StillChecksEachTileThatIsThere()
        {
            Assert.That(Problems(x => MapHazardOf(x, "panel")["tiles"] = new JArray("D5", "Z9")), Is.EqualTo(new[] { "map hazard panel tile \"Z9\" is off the map" }));
        }

        private static IEnumerable<object[]> BadCollections()
        {
            yield return new object[] { "map.hazards", (Action<ContentFiles>)(x => ((JObject)x["map"]!).Remove("hazards")), "map.hazards must be a list" };
            yield return new object[] { "hazards", (Action<ContentFiles>)(x => x.Root.Remove("hazards")), "hazards must be a list" };
            yield return new object[] { "presets", (Action<ContentFiles>)(x => x.Root["presets"] = JValue.CreateNull()), "presets must be a list" };
            yield return new object[] { "intercom.lines", (Action<ContentFiles>)(x => ((JObject)x["intercom"]!).Remove("lines")), "intercom.lines must be a list" };
            yield return new object[] { "units", (Action<ContentFiles>)(x => x.Root["units"] = new JObject()), "units must be a list" };
            yield return new object[] { "upgrades", (Action<ContentFiles>)(x => x.Root["upgrades"] = "none"), "upgrades must be a list" };
            yield return new object[] { "abilities.abilities", (Action<ContentFiles>)(x => ((JObject)x["abilities"]!).Remove("abilities")), "abilities.abilities must be a list" };
            yield return new object[] { "abilities.utilities", (Action<ContentFiles>)(x => ((JObject)x["abilities"]!).Remove("utilities")), "abilities.utilities must be a list" };
        }

        [TestCaseSource(nameof(BadCollections))]
        public void RejectsAMissingOrNonListCollection(string name, Action<ContentFiles> edit, string problem)
        {
            var f = Edited(edit);
            Assert.That(ContentLoader.Validate(f), Does.Contain(problem), name);
            Assert.Throws<ContentError>(() => ContentLoader.Build(f));
        }

        [Test]
        public void RejectsMapHazardsSetToNullAsWellAsRemoved()
        {
            Assert.That(Problems(x => x["map"]!["hazards"] = JValue.CreateNull()), Is.EqualTo(new[] { "map.hazards must be a list" }));
        }

        [Test]
        public void RejectsAnEggCorpseHostTableThatIsMissingOrNotAnObject()
        {
            var expected = new[] { "rules.eggs.corpseHosts must be an object" };
            Assert.That(Problems(x => ((JObject)Rules(x)["eggs"]!).Remove("corpseHosts")), Is.EqualTo(expected));
            Assert.That(Problems(x => Rules(x)["eggs"]!["corpseHosts"] = new JArray("guard")), Is.EqualTo(expected));
        }

        [Test]
        public void RejectsARemovedUiBlock()
        {
            var f = Edited(x => Rules(x).Remove("ui"));
            Assert.That(ContentLoader.Validate(f), Is.EqualTo(new[]
            {
                "rules.ui.showHazardCooldowns must be true or false",
                "rules.ui.secondsPerActor must be a number of seconds, 0 or more (got undefined)",
            }));
            Assert.Throws<ContentError>(() => ContentLoader.Build(f));
        }

        private static IEnumerable<JToken> BadBooleans() => new JToken[] { new JValue("yes"), new JValue(1), JValue.CreateNull() };

        [TestCaseSource(nameof(BadBooleans))]
        public void RejectsABadShowHazardCooldowns(JToken v)
        {
            Assert.That(Problems(x => Rules(x)["ui"]!["showHazardCooldowns"] = v), Is.EqualTo(new[] { "rules.ui.showHazardCooldowns must be true or false" }));
        }

        private static IEnumerable<JToken> BadSeconds() => new JToken[] { new JValue(-1), new JValue("0.4"), JValue.CreateNull(), new JValue(double.NaN), new JValue(double.PositiveInfinity) };

        [TestCaseSource(nameof(BadSeconds))]
        public void RejectsABadSecondsPerActor(JToken v)
        {
            var problems = Problems(x => Rules(x)["ui"]!["secondsPerActor"] = v);
            Assert.That(problems, Has.Count.EqualTo(1));
            Assert.That(problems[0], Does.StartWith("rules.ui.secondsPerActor must be a number of seconds, 0 or more"));
        }

        [TestCase(0)]
        [TestCase(0.25)]
        [TestCase(2)]
        public void AcceptsAGoodSecondsPerActor(double v) => Assert.That(Problems(x => Rules(x)["ui"]!["secondsPerActor"] = v), Is.Empty);

        [Test]
        public void RejectsAnEarlyWaveWhenEmptyThatIsNotTrueOrFalse()
        {
            Assert.That(Problems(x => Rules(x)["earlyWaveWhenEmpty"] = "yes"), Is.EqualTo(new[] { "rules.earlyWaveWhenEmpty must be true or false" }));
        }

        [Test]
        public void RejectsAnUpgradeWithNoRequiresBlock()
        {
            Assert.That(Problems(x => UpgradeOf(x, "T1_piercing_rounds").Remove("requires")), Is.EqualTo(new[] { "upgrades.T1_piercing_rounds.requires must be an object" }));
        }

        [Test]
        public void RejectsRequirementListsThatAreNotListsAndStillNamesAnUnknownAbility()
        {
            Assert.That(Problems(x => UpgradeOf(x, "T1_piercing_rounds")["requires"]!["any"] = "sidearm"), Is.EqualTo(new[] { "upgrades.T1_piercing_rounds.requires.any must be a list" }));
            Assert.That(Problems(x => UpgradeOf(x, "T1_piercing_rounds")["requires"]!["any"] = new JArray("sidearm", "railgun")), Is.EqualTo(new[] { "upgrades.T1_piercing_rounds requires unknown ability \"railgun\"" }));
        }

        private static IEnumerable<object[]> ShapesTheTypeScriptValidatorCrashesOn()
        {
            yield return new object[] { (Action<ContentFiles>)(x => ((JObject)x["waves"]![0]!).Remove("units")), "waves[0].units must be a list" };
            yield return new object[] { (Action<ContentFiles>)(x => ((JObject)x["presets"]![0]!).Remove("abilities")), "presets.tech.abilities must be a list" };
            yield return new object[] { (Action<ContentFiles>)(x => ((JObject)x["presets"]![0]!).Remove("upgrades")), "presets.tech.upgrades must be a list" };
            yield return new object[] { (Action<ContentFiles>)(x => ((JObject)x["intercom"]!["lines"]![0]!).Remove("trigger")), "intercom line \"round1\" needs a trigger" };
        }

        // Not in the TypeScript tests: these inputs throw a TypeError from validateContent there. Reporting them is invariant 3.
        [TestCaseSource(nameof(ShapesTheTypeScriptValidatorCrashesOn))]
        public void ReportsAMissingListOrBlockThatRulesReadWithoutAFallback(Action<ContentFiles> edit, string problem)
        {
            var f = Edited(edit);
            Assert.That(ContentLoader.Validate(f), Does.Contain(problem));
            Assert.Throws<ContentError>(() => ContentLoader.Build(f));
        }

        [Test]
        public void AValueThatValidationDoesNotCheckIsStillRejectedAsContentError()
        {
            var f = Edited(x => UpgradeOf(x, "T4_grapple_pin")["damageType"] = "plasma");
            Assert.That(ContentLoader.Validate(f), Is.Empty);
            Assert.Throws<ContentError>(() => ContentLoader.Build(f));
        }

        [Test]
        public void AnOverrideChangesTheHashAndLeavesTheSourceFilesAlone()
        {
            var tuned = ContentWith(("units.warden.hp", 16));
            Assert.That(tuned.Hash, Is.Not.EqualTo(C.Hash));
            Assert.That(tuned.Units["warden"].Hp, Is.EqualTo(16));
            Assert.That(C.Units["warden"].Hp, Is.EqualTo(20));
        }
    }
}
