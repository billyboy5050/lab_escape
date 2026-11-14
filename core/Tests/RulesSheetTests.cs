#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static LabEscape.Core.Tests.Helpers;

namespace LabEscape.Core.Tests
{
    /// <summary>
    /// The playtest rules sheet (docs/rules-sheet.html) copies numbers and text from content/. Each copy is tagged with
    /// data-c="&lt;path into the loaded content&gt;" and each map cell with data-t="&lt;map character&gt;", so a content change
    /// that makes the printed sheet wrong fails here instead of reaching a tester.
    /// </summary>
    public class RulesSheetTests
    {
        private static readonly string Sheet = File.ReadAllText(Path.Combine(RepoRoot, "docs", "rules-sheet.html"));

        private static readonly JObject Files = FreshFiles().Root;

        /// <summary>
        /// The content as the sheet's paths name it: the files' own blocks, with each list of definitions keyed by ID, so
        /// `units.guard.hp` and `hazardDefs.wall_gun.cue` read as they are written.
        /// </summary>
        private static readonly JObject Loaded = new JObject
        {
            ["rules"] = Files["rules"],
            ["units"] = ById(Files["units"]),
            ["abilities"] = ById(Files["abilities"]!["abilities"]),
            ["utilities"] = ById(Files["abilities"]!["utilities"]),
            ["upgrades"] = ById(Files["upgrades"]),
            ["hazardDefs"] = ById(Files["hazards"]),
            ["map"] = Files["map"],
            ["waves"] = Files["waves"],
            ["intercom"] = Files["intercom"],
            ["presets"] = Files["presets"],
        };

        private static JObject ById(JToken? list)
        {
            var o = new JObject();
            foreach (var item in (JArray)list!) o[item["id"]!.Value<string>()!] = item.DeepClone();
            return o;
        }

        private static string Decode(string text) =>
            text.Replace("&quot;", "\"").Replace("&#39;", "'").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&");

        /// <summary>Every tagged element on the sheet: its content path and its text.</summary>
        private static List<(string Path, string Text)> Tagged() =>
            Regex.Matches(Sheet, "<([a-z0-9]+)\\b[^>]*\\bdata-c=\"([^\"]+)\"[^>]*>(.*?)</\\1>", RegexOptions.Singleline)
                .Cast<Match>()
                .Select(m => (m.Groups[2].Value, m.Groups[3].Value))
                .ToList();

        /// <summary>The value a dotted path names in the loaded content, or null. An array answers to an index and to `length`.</summary>
        private static JToken? Lookup(string path)
        {
            JToken? at = Loaded;
            foreach (var key in path.Split('.'))
            {
                switch (at)
                {
                    case JObject o:
                        at = o[key];
                        break;
                    case JArray a when key == "length":
                        at = new JValue(a.Count);
                        break;
                    case JArray a when int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var i) && i < a.Count:
                        at = a[i];
                        break;
                    default:
                        return null;
                }
            }
            return at;
        }

        /// <summary>How the sheet writes a content value: lists as "a, b and c". Null for anything else.</summary>
        private static string? Written(JToken? value)
        {
            switch (value)
            {
                case JValue v when v.Type == JTokenType.Integer:
                    return Convert.ToInt64(v.Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
                case JValue v when v.Type == JTokenType.String:
                    return (string)v.Value!;
                case JArray a when a.Count > 0 && a.All(x => x.Type == JTokenType.String):
                    var items = a.Select(x => x.Value<string>()!).ToList();
                    return items.Count == 1 ? items[0] : $"{string.Join(", ", items.Take(items.Count - 1))} and {items[items.Count - 1]}";
                default:
                    return null;
            }
        }

        /// <summary>
        /// The sheet's text with its head, map, key and glyph examples, and every tagged value taken out. Whatever number or
        /// damage type is left was typed in by hand, so a tag dropped from a copied value shows up here.
        /// </summary>
        private static string UntaggedText()
        {
            var html = Sheet;
            html = Regex.Replace(html, "<head>.*</head>", " ", RegexOptions.Singleline);
            html = Regex.Replace(html, "<table class=\"map\".*?</table>", " ", RegexOptions.Singleline);
            html = Regex.Replace(html, "<([a-z0-9]+)\\b[^>]*\\bdata-c=\"[^\"]+\"[^>]*>.*?</\\1>", " ", RegexOptions.Singleline);
            html = Regex.Replace(html, "<kbd>.*?</kbd>", " ", RegexOptions.Singleline);
            html = Regex.Replace(html, "<span class=\"(?:g|k\\b[^\"]*)\">.*?</span>", " ", RegexOptions.Singleline);
            html = Regex.Replace(html, "<[^>]+>", " ");
            return Regex.Replace(Decode(html), "\\s+", " ");
        }

        private static IEnumerable<KeyValuePair<string, JToken?>> Entries(string block) => ((JObject)Loaded[block]!).Properties().Select(p => new KeyValuePair<string, JToken?>(p.Name, p.Value));

        [Test]
        public void QuotesEveryTaggedValueAsContentHasIt()
        {
            var tags = Tagged();
            Assert.That(tags.Count, Is.GreaterThan(50));
            foreach (var (path, text) in tags)
            {
                Assert.That(text, Does.Not.Contain("<"), $"{path} holds markup; tag the text itself");
                var value = Written(Lookup(path));
                Assert.That(value, Is.Not.Null, $"{path} is not a number, text or list of text in content");
                Assert.That(Decode(text), Is.EqualTo(value), path);
            }
        }

        [Test]
        public void TagsTheNumbersOfEveryAbilityEnemyAndLabDefence()
        {
            var paths = new HashSet<string>(Tagged().Select(t => t.Path));
            var want = new List<string> { "units.player.hp", "rules.turn.ap", "rules.turn.movement", "rules.sprint.ap", "rules.sprint.movement", "rules.caps.minions" };
            foreach (var (id, _) in Entries("abilities")) want.AddRange(new[] { $"abilities.{id}.name", $"abilities.{id}.ap" });
            foreach (var (id, u) in Entries("units"))
            {
                if (u!.Value<string>("team") == "enemy") want.AddRange(new[] { $"units.{id}.name", $"units.{id}.hp", $"units.{id}.armor", $"units.{id}.move" });
            }
            foreach (var (id, _) in Entries("hazardDefs")) want.Add($"hazardDefs.{id}.cue");
            for (var i = 0; i < ((JArray)Loaded["map"]!["hazards"]!).Count; i++) want.Add($"map.hazards.{i}.tiles");
            foreach (var (id, u) in Entries("utilities"))
            {
                if (!string.IsNullOrEmpty(u!.Value<string>("requiresAbility"))) want.Add($"utilities.{id}.ap");
            }
            Assert.That(want.Where(p => !paths.Contains(p)), Is.Empty);
        }

        [Test]
        public void LeavesNoNumberOrDamageTypeUntagged()
        {
            // Fixed by the rules, not by content: losing at 0 HP, range counting a diagonal as 1, the 8 tiles around a tile
            // (radius 1, checked below), a 3x3 area (the same) and Corrode's 1 armor per stack. Armor's complement is checked
            // below too.
            var fixedPhrases = new[] { "at 0 HP", "count as 1", "the 8 tiles around", "the 8 around", "3x3", "1 less armor", "Electric, acid, poison and Parasite damage ignore it" };
            var text = UntaggedText();
            foreach (var phrase in fixedPhrases)
            {
                Assert.That(text, Does.Contain(phrase), $"\"{phrase}\" is no longer on the sheet; drop it from this list");
                text = text.Replace(phrase, " ");
            }
            // ECMAScript semantics, so \d and \b mean ASCII digits and word boundaries only, as they did in the TypeScript test.
            var numbers = Regex.Matches(text, "\\S*\\d\\S*", RegexOptions.ECMAScript).Cast<Match>().Select(m => m.Value);
            Assert.That(numbers, Is.Empty, "numbers on the sheet without a data-c tag");
            var types = Regex.Matches(text, "\\b(?:kinetic|explosive|fire|electric|acid)\\b", RegexOptions.ECMAScript).Cast<Match>().Select(m => m.Value);
            Assert.That(types, Is.Empty, "damage types without a data-c tag");
        }

        [Test]
        public void DrawsTheMapFromContent()
        {
            var map = Loaded["map"]!;
            int width = map.Value<int>("width"), height = map.Value<int>("height");
            var rows = map["rows"]!.Values<string>().Select(r => r!).ToList();
            var cells = Regex.Matches(Sheet, "<td data-t=\"([^\"]*)\"").Cast<Match>().Select(m => Decode(m.Groups[1].Value)).ToList();
            Assert.That(cells, Has.Count.EqualTo(width * height));
            var drawn = Enumerable.Range(0, height).Select(y => string.Concat(cells.Skip(y * width).Take(width))).ToList();
            Assert.That(drawn, Is.EqualTo(rows));
            // The @ marks "Your start", but the player is placed from map.playerStart.
            var at = rows.FindIndex(r => r.Contains('@'));
            Assert.That(at, Is.GreaterThanOrEqualTo(0), "the map has no @");
            Assert.That(Tiles.Name(new Pos(rows[at].IndexOf('@'), at)), Is.EqualTo(map.Value<string>("playerStart")));
        }

        [Test]
        public void MatchesTheContentBehindItsWording()
        {
            var rules = Loaded["rules"]!;
            JToken At(string path) => Lookup(path) ?? JValue.CreateNull();
            JToken Json(string json) => JToken.Parse(json);

            // "The first is there at the start".
            Assert.That(At("waves.0.round").Value<int>(), Is.EqualTo(1));
            // "An enemy that arrived this round only moves": every wave has an arrival round.
            Assert.That(rules.Value<string>("arrivalRound"), Is.EqualTo("all"));
            // Corrode is "for the rest of the fight".
            Assert.That(At("rules.statuses.corrode.duration").Value<int>(), Is.EqualTo(0));
            // "On a Guard corpse a spitter, on a Flamer corpse a burster"; "Medic and Warden corpses cannot hold one".
            Assert.That(JToken.DeepEquals(At("rules.eggs.corpseHosts"), Json("{\"guard\":\"spitter\",\"flamer\":\"burster\",\"medic\":null,\"warden\":null}")), Is.True, "rules.eggs.corpseHosts");
            // Mine and burster blasts hit "there and on the 8 tiles around"; Spore Pod poisons "a 3x3 area".
            Assert.That(At("abilities.proximity_mine.blast.radius").Value<int>(), Is.EqualTo(1));
            Assert.That(At("units.burster.blast.radius").Value<int>(), Is.EqualTo(1));
            Assert.That(JToken.DeepEquals(At("abilities.spore_pod.area"), Json("{\"shape\":\"square\",\"radius\":1}")), Is.True, "abilities.spore_pod.area");
            // Pinned covers "its next phase" and Slowed "its next move".
            Assert.That(At("rules.statuses.pinned.duration").Value<int>(), Is.EqualTo(1));
            Assert.That(At("rules.statuses.slowed.duration").Value<int>(), Is.EqualTo(1));
            // The legend: walls and pillars block movement, sight and shots; the shutter and wall gun block movement and
            // shots, not sight; panels and vents block nothing. "Only walls and pillars block" sight, and they end the
            // Flamer's sweep.
            var terrain = (JObject)Loaded["map"]!["terrain"]!;
            string Blocks(string t) => $"move {terrain[t]!.Value<bool>("blocksMove")}, sight {terrain[t]!.Value<bool>("blocksSight")}, shots {terrain[t]!.Value<bool>("blocksProjectile")}";
            foreach (var t in new[] { "wall", "pillar" }) Assert.That(Blocks(t), Is.EqualTo("move True, sight True, shots True"), t);
            foreach (var t in new[] { "shutter", "gun" }) Assert.That(Blocks(t), Is.EqualTo("move True, sight False, shots True"), t);
            foreach (var t in new[] { "floor", "panel", "vent" }) Assert.That(Blocks(t), Is.EqualTo("move False, sight False, shots False"), t);
            Assert.That(terrain.Properties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal), Is.EqualTo(new[] { "floor", "gun", "panel", "pillar", "shutter", "vent", "wall" }));
            // "The elite" is the Warden, which the Grapple Hook cannot pull; the drone is the "flying minion".
            Assert.That(Entries("units").Where(u => u.Value!.Value<bool?>("elite") == true).Select(u => u.Key), Is.EqualTo(new[] { "warden" }));
            Assert.That(Entries("units").Where(u => u.Value!.Value<bool?>("flying") == true).Select(u => u.Key), Is.EqualTo(new[] { "drone" }));
            // "Electric, acid, poison and Parasite damage ignore it": every damage type content uses that armor does not reduce.
            var used = new HashSet<string>();
            void Walk(JToken v)
            {
                if (v is JArray a)
                {
                    foreach (var x in a) Walk(x);
                }
                else if (v is JObject o)
                {
                    foreach (var p in o.Properties())
                    {
                        if (p.Name == "damageType" && p.Value.Type == JTokenType.String) used.Add(p.Value.Value<string>()!);
                        else Walk(p.Value);
                    }
                }
            }
            Walk(Files);
            var armored = rules["armorAppliesTo"]!.Values<string>().ToList();
            Assert.That(used.Where(t => !armored.Contains(t)).OrderBy(t => t, StringComparer.Ordinal), Is.EqualTo(new[] { "acid", "electric", "parasite", "poison" }));
            // Pick Up takes "an adjacent mine".
            Assert.That(At("utilities.pick_up_mine.range").Value<int>(), Is.EqualTo(1));
            // The status table gives one duration each for poison and Parasite, whichever ability applies them.
            Assert.That(JToken.DeepEquals(At("abilities.spore_pod.status"), new JObject { ["id"] = "poison", ["duration"] = At("rules.statuses.poison.duration") }), Is.True, "abilities.spore_pod.status");
            Assert.That(JToken.DeepEquals(At("abilities.parasite.status"), new JObject { ["id"] = "parasite", ["duration"] = At("rules.statuses.parasite.duration") }), Is.True, "abilities.parasite.status");
        }
    }
}
