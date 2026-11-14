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
    public class RoomsTests
    {
        private static readonly JArray Rooms = (JArray)JsonIn.Parse(File.ReadAllText(Path.Combine(RepoRoot, "golden", "rooms.json")));
        private static readonly Content R = WithRooms(Rooms);
        private static readonly Loadout Hybrid = PresetLoadout("hybrid");

        private static string[] RoomIds() => Rooms.Select(r => r["id"]!.Value<string>()!).ToArray();

        private static Content WithRooms(JToken rooms) => ContentLoader.Build(FilesWith(rooms));

        private static ContentFiles FilesWith(JToken rooms)
        {
            var files = FreshFiles();
            files.Root["rooms"] = rooms.DeepClone();
            return files;
        }

        private static JObject RoomFile(string id) => (JObject)Rooms.First(r => r["id"]!.Value<string>() == id).DeepClone();

        /// <summary>A one-row room: the player and a Guard either side of a wall, so nothing can happen until the round cap.</summary>
        private static JObject SealedRoom(int roundCap) => JObject.Parse("{\"id\":\"sealed\",\"name\":\"Sealed\",\"width\":3,\"height\":1,\"rows\":[\"@#.\"],\"playerStart\":\"A1\",\"hazards\":[],\"waves\":[{\"round\":1,\"units\":[{\"def\":\"guard\",\"tile\":\"C1\"}]}],\"roundCap\":" + roundCap + "}");

        private static JObject ArmoryAsRoom(ContentFiles files)
        {
            var copy = (JObject)files["map"]!.DeepClone();
            copy.Remove("legend");
            copy.Remove("terrain");
            copy["id"] = "armory_copy";
            copy["waves"] = files["waves"]!.DeepClone();
            copy["roundCap"] = files["rules"]!["roundCap"]!.DeepClone();
            return copy;
        }

        private static IEnumerable<string> Json(IEnumerable<GameEvent> events) => events.Select(e => e.ToJson());

        // ------------------------------------------------------------------ the Armory as a room

        [Test]
        public void TheArmoryIsTheDefaultRoomBuiltFromItsOwnFiles()
        {
            Assert.That(C.RoomFor(null), Is.SameAs(C.DefaultRoom));
            Assert.That(C.RoomFor(C.DefaultRoom.Id), Is.SameAs(C.DefaultRoom));
            Assert.That(C.DefaultRoom.RoundCap, Is.EqualTo(C.Files["rules"]!["roundCap"]!.Value<int>()));
            Assert.That(C.DefaultRoom.Waves.Count, Is.EqualTo(((JArray)C.Files["waves"]!).Count));
            Assert.That(C.DefaultRoom.PlayerStart, Is.EqualTo(C.Files["map"]!["playerStart"]!.Value<string>()));
            Assert.That(C.Rooms.Keys, Is.EqualTo(new[] { C.DefaultRoom.Id }));
        }

        [Test]
        public void TheArmoryFollowsOverridesOfTheFilesItIsBuiltFrom()
        {
            Assert.That(ContentWith(("rules.roundCap", 9)).DefaultRoom.RoundCap, Is.EqualTo(9));
            Assert.That(ContentWith(("map.playerStart", "A8")).DefaultRoom.PlayerStart, Is.EqualTo("A8"));
        }

        [Test]
        public void TheContentHashIgnoresAnAbsentRoomsFileAndCoversAPresentOne()
        {
            var golden = JsonIn.Parse(File.ReadAllText(Path.Combine(RepoRoot, "golden", "hybrid.json")));
            Assert.That(C.Hash, Is.EqualTo(golden["record"]!["contentHash"]!.Value<string>()));
            Assert.That(R.Hash, Is.Not.EqualTo(C.Hash));
            Assert.That(WithRooms(new JArray(Rooms.Skip(1))).Hash, Is.Not.EqualTo(R.Hash));
        }

        [Test]
        public void TheContentHashOfRoomsMatchesTheOneTheGoldenReplayRecorded()
        {
            var golden = JsonIn.Parse(File.ReadAllText(Path.Combine(RepoRoot, "golden", "room-hall.json")));
            Assert.That(R.Hash, Is.EqualTo(golden["record"]!["contentHash"]!.Value<string>()));
        }

        [Test]
        public void TheArmoryPlaysExactlyAsTheSameRoomWrittenInTheNewFormat()
        {
            var files = FreshFiles();
            var copy = WithRooms(new JArray(ArmoryAsRoom(files)));
            var settings = new FightSettings { Room = "armory_copy" };
            foreach (var seed in new long[] { 1, 2 })
            {
                var a = Runner.RunFight(C, Hybrid, new GreedyBot(seed));
                var b = Runner.RunFight(copy, Hybrid, new GreedyBot(seed), settings);
                Assert.That(Json(Records.Replay(copy, b.Record).Events), Is.EqualTo(Json(Records.Replay(C, a.Record).Events)));
            }
            for (long seed = 1; seed <= 20; seed++)
            {
                var a = Runner.RunFight(C, Hybrid, new RandomBot(seed));
                var b = Runner.RunFight(copy, Hybrid, new RandomBot(seed), settings);
                Assert.That(Json(Records.Replay(copy, b.Record).Events), Is.EqualTo(Json(Records.Replay(C, a.Record).Events)));
            }
        }

        [Test]
        public void AFightThatNamesNoRoomIsInTheArmoryEvenWithOtherRoomsLoaded()
        {
            var s = Engine.NewGame(R, Hybrid).State;
            Assert.That(Name(s.Units[0].Pos), Is.EqualTo(C.DefaultRoom.PlayerStart));
            Assert.That(s.Hazards.Select(h => h.Id), Is.EqualTo(C.DefaultRoom.Hazards.Select(h => h.Id)));
        }

        // ------------------------------------------------------------------ rooms of other sizes

        [Test]
        public void RoomsLoadWithTheirOwnSizeAndAddToThePalette()
        {
            Assert.That(RoomIds().Select(id => $"{R.Rooms[id].Width}x{R.Rooms[id].Height}"), Is.EqualTo(new[] { "6x5", "12x12", "14x8" }));
            var hallways = R.Rooms["hallways"];
            Assert.That(hallways.Legend["_"], Is.EqualTo("void"));
            Assert.That(hallways.Legend["#"], Is.EqualTo("wall"));
            Assert.That(hallways.Terrain["wall"].BlocksMove, Is.True);
            // A room's additions do not leak into the Armory's palette.
            Assert.That(R.DefaultRoom.Legend.ContainsKey("_"), Is.False);
        }

        [TestCase("nowhere")]
        [TestCase("constructor")]
        public void AnUnknownRoomIsRefusedRatherThanFallingBackToTheArmory(string id)
        {
            var e = Assert.Throws<ArgumentException>(() => Engine.NewGame(R, Hybrid, new FightSettings { Room = id }));
            Assert.That(e!.Message, Is.EqualTo($"Unknown room \"{id}\""));
        }

        [Test]
        public void TheFightStartsFromTheRoom()
        {
            var s = Engine.NewGame(R, Hybrid, new FightSettings { Room = "hall" }).State;
            Assert.That(Name(s.Units[0].Pos), Is.EqualTo("F12"));
            Assert.That(s.Hazards.Select(h => h.Id), Is.EqualTo(new[] { "gun", "panel", "vent_e5", "vent_h5", "vent_c10", "vent_j10" }));
            Assert.That(s.Units.Where(u => u.Team == Team.Enemy).Select(u => u.Def), Is.EqualTo(new[] { "guard", "guard", "guard", "medic" }));
            Assert.That(Name(s.Units.First(u => u.Def == "medic").Pos), Is.EqualTo("F2"));
        }

        [Test]
        public void RoomsUseTheirOwnBounds()
        {
            var closet = R.Rooms["closet"].Board;
            var hall = R.Rooms["hall"].Board;
            Assert.That(closet.InBounds(T("F5")), Is.True);
            Assert.That(closet.InBounds(T("G5")), Is.False);
            Assert.That(closet.InBounds(T("F6")), Is.False);
            Assert.That(hall.InBounds(T("L12")), Is.True);
            bool Moved(string id, string to) => Actions.LegalCommands(R, Engine.NewGame(R, Hybrid, new FightSettings { Room = id }).State).OfType<MoveCommand>().Any(c => c.Path[c.Path.Count - 1] == to);
            Assert.That(Moved("hall", "F10"), Is.True);
            Assert.That(Moved("closet", "B5"), Is.False);
        }

        [Test]
        public void VoidTilesBlockMovementSightAndShots()
        {
            var b = R.Rooms["hallways"].Board;
            Assert.That(b.TerrainId(T("A1")), Is.EqualTo("void"));
            Assert.That(new[] { b.BlocksMove(T("A1")), b.BlocksSight(T("A1")), b.BlocksProjectile(T("A1")) }, Is.EqualTo(new[] { true, true, true }));
            Assert.That(b.HasLos(T("H3"), T("K3")), Is.False);
        }

        [Test]
        public void RoomsEndAtTheirOwnRoundCap()
        {
            var c = WithRooms(new JArray(SealedRoom(3)));
            var state = Engine.NewGame(c, Hybrid, new FightSettings { Room = "sealed" }).State;
            for (var round = 1; round <= 3; round++)
            {
                Assert.That(state.Outcome, Is.Null);
                state = Engine.Step(c, state, EndTurn).State;
            }
            Assert.That(state.Outcome!.Result, Is.EqualTo(FightResult.Lose));
            Assert.That(state.Outcome!.Cause, Is.EqualTo("Round 3 ended with the fight unresolved"));
            Assert.That(state.Outcome!.Round, Is.EqualTo(3));
        }

        [Test]
        public void RoomsBringTheirOwnWavesInFightsAndInTheDebugCommand()
        {
            var c = WithRooms(new JArray(SealedRoom(9)));
            var state = Engine.NewGame(c, Hybrid, new FightSettings { Room = "sealed" }).State;
            Assert.That(state.WavesSpawned, Is.EqualTo(1));
            Assert.That(Engine.Step(c, state, new DebugForceWave()).Error, Is.EqualTo("No waves left"));
            Assert.That(Engine.Step(C, Engine.NewGame(C, Hybrid).State, new DebugForceWave()).Ok, Is.True);
        }

        [TestCaseSource(nameof(RoomIds))]
        public void BotsFinishFightsHeadlesslyAndReproduciblyOnEveryPreset(string id)
        {
            var settings = new FightSettings { Room = id };
            foreach (var p in C.Presets)
            {
                var spec = new BatchSpec { Bot = Batch.RandomKind, Loadout = LoadoutChoice.OfPreset(p.Id), Seeds = Batch.SeedRange(1, 30), Settings = settings };
                var rows = Batch.RunBatch(spec, R);
                Assert.That(rows.Where(f => f.Outcome == FightOutcome.Error).Select(f => f.Error), Is.Empty);
                Assert.That(rows.Select(f => (f.Outcome, f.Rounds, f.Commands)), Is.EqualTo(Batch.RunBatch(spec, R).Select(f => (f.Outcome, f.Rounds, f.Commands))));
            }
            var greedy = Batch.RunBatch(new BatchSpec { Bot = Batch.GreedyKind, Loadout = LoadoutChoice.OfPreset("hybrid"), Seeds = Batch.SeedRange(1, 2), Settings = settings }, R);
            Assert.That(greedy.Where(f => f.Outcome == FightOutcome.Error).Select(f => f.Error), Is.Empty);
        }

        [TestCaseSource(nameof(RoomIds))]
        public void PreviewsMatchResults(string id)
        {
            var rng = new Rng(11);
            var settings = new FightSettings { Room = id };
            for (var i = 0; i < 40; i++)
            {
                var s = Engine.NewGame(R, Hybrid, settings).State;
                for (var n = rng.Int(30); n > 0 && s.Outcome == null; n--)
                {
                    var moves = Actions.LegalCommands(R, s);
                    s = Engine.Step(R, s, moves[rng.Int(moves.Count)]).State;
                }
                if (s.Outcome != null) continue;
                var before = s.ToJson();
                var cmds = Actions.LegalCommands(R, s);
                var cmd = cmds[rng.Int(cmds.Count)];
                var preview = Previews.PreviewCommand(R, s, cmd);
                var real = Engine.Step(R, s, cmd);
                Assert.That(Json(preview.Events), Is.EqualTo(Json(real.Events)));
                Assert.That(preview.State!.ToJson(), Is.EqualTo(real.State.ToJson()));
                Assert.That(s.ToJson(), Is.EqualTo(before));
            }
        }

        [Test]
        public void RoomsTakeTuningOverridesAsTheRestOfTheContentDoes()
        {
            var overrides = new OrderedMap<string, JToken>(StringComparer.Ordinal);
            overrides["rooms.closet.roundCap"] = 3;
            var tuned = ContentLoader.Build(ContentLoader.WithOverrides(FilesWith(Rooms), overrides));
            Assert.That(tuned.Rooms["closet"].RoundCap, Is.EqualTo(3));
            Assert.That(tuned.Hash, Is.Not.EqualTo(R.Hash));
        }

        // ------------------------------------------------------------------ replays in a room

        private static FightRun Fight() => Runner.RunFight(R, Hybrid, new RandomBot(5), new FightSettings { Room = "closet" });

        [Test]
        public void ReplaysCarryTheRoomAndReplayExactly()
        {
            var r = Fight();
            Assert.That(r.Record.ToToken()["settings"]!["room"]!.Value<string>(), Is.EqualTo("closet"));
            Assert.That(Records.Replay(R, r.Record).State.ToJson(), Is.EqualTo(r.State.ToJson()));
        }

        [Test]
        public void ReplaysAreRefusedByContentWithoutThatRoom()
        {
            Assert.Throws<ReplayMismatch>(() => Records.CheckCompatible(C, Fight().Record));
        }

        private static JObject WithRoomSetting(FightRun r, JToken room)
        {
            var token = r.Record.ToToken();
            token["settings"] = new JObject { ["room"] = room };
            return token;
        }

        [Test]
        public void ReplaysHaveTheRoomCheckedSinceAReplayFileIsUntrusted()
        {
            var r = Fight();
            foreach (var bad in new JToken[] { new JValue(5), JValue.CreateNull(), new JArray("closet"), new JObject { ["id"] = "closet" } })
            {
                var rec = WithRoomSetting(r, bad);
                Assert.That(Records.RecordProblems(rec), Is.EqualTo(new[] { $"settings.room must be a room ID (got {Hash.Stringify(bad)})" }));
                var e = Assert.Throws<ReplayMismatch>(() => Records.CheckCompatible(R, rec));
                Assert.That(e!.Message, Does.Contain("malformed"));
            }
            foreach (var id in new[] { "nowhere", "constructor", "__proto__" })
            {
                var e = Assert.Throws<ReplayMismatch>(() => Records.CheckCompatible(R, WithRoomSetting(r, id)));
                Assert.That(e!.Message, Is.EqualTo($"Replay was played in room \"{id}\", which this content does not have"));
            }
        }

        // ------------------------------------------------------------------ validation

        private static List<string> Problems(JToken rooms) => ContentLoader.Validate(FilesWith(rooms));

        private static List<string> Edit(string id, Action<JObject> change)
        {
            var rooms = (JArray)Rooms.DeepClone();
            change((JObject)rooms.First(r => r["id"]!.Value<string>() == id));
            return Problems(rooms);
        }

        private static JObject Hazard(JObject room, int i) => (JObject)room["hazards"]![i]!;
        private static JObject WaveUnit(JObject room, int wave) => (JObject)room["waves"]![wave]!["units"]![0]!;

        [Test]
        public void AcceptsTheGoldenRoomsAOneRowRoomAndTheLargestAddressableOne()
        {
            Assert.That(Problems(Rooms), Is.Empty);
            Assert.That(Problems(new JArray(SealedRoom(5))), Is.Empty);
            var huge = SealedRoom(5);
            huge["width"] = 26;
            huge["height"] = 99;
            huge["rows"] = new JArray(Enumerable.Repeat(new string('.', 26), 99));
            huge["playerStart"] = "Z99";
            huge["waves"]![0]!["units"]![0]!["tile"] = "A1";
            Assert.That(Problems(new JArray(huge)), Is.Empty);
        }

        [Test]
        public void AcceptsNoRoomsAtAllButNotRoomsThatAreNotAList()
        {
            Assert.That(ContentLoader.Validate(FreshFiles()), Is.Empty);
            Assert.That(Problems(new JArray()), Is.Empty);
            Assert.That(Problems(JValue.CreateNull()), Is.EqualTo(new[] { "rooms must be a list" }));
            Assert.That(Problems(new JObject { ["id"] = "x" }), Is.EqualTo(new[] { "rooms must be a list" }));
        }

        [Test]
        public void WantsRoomsWithAnIdOfTheirOwn()
        {
            Assert.That(Problems(new JArray(new JObject { ["name"] = "x" })), Is.EqualTo(new[] { "rooms[0] needs an id" }));
            Assert.That(Problems(new JArray(7)), Is.EqualTo(new[] { "rooms[0] needs an id" }));
            Assert.That(Problems(new JArray(RoomFile("closet"), RoomFile("closet"))), Is.EqualTo(new[] { "rooms has two rooms with the id \"closet\"" }));
            var clash = RoomFile("closet");
            clash["id"] = C.DefaultRoom.Id;
            Assert.That(Problems(new JArray(clash)), Is.EqualTo(new[] { $"rooms has two rooms with the id \"{C.DefaultRoom.Id}\"" }));
        }

        private static readonly (string Name, string Room, Action<JObject> Change, string[] Expected)[] BadRooms =
        {
            ("round cap", "closet", r => r["roundCap"] = 0, new[] { "rooms.closet.roundCap must be an integer >= 1 (got 0)" }),
            ("missing round cap", "closet", r => r.Remove("roundCap"), new[] { "rooms.closet.roundCap must be an integer >= 1 (got undefined)" }),
            ("legend", "closet", r => r["legend"] = "x", new[] { "rooms.closet.legend must be an object" }),
            ("terrain", "closet", r => r["terrain"] = 3, new[] { "rooms.closet.terrain must be an object" }),
            ("row count", "closet", r => ((JArray)r["rows"]!).RemoveAt(4), new[] { "rooms.closet has 4 rows but height 5" }),
            ("row length", "closet", r => r["rows"]![1] = ".....", new[] { "rooms.closet row 2 has 5 tiles but width 6" }),
            ("unknown tile character", "closet", r => r["rows"]![1] = ".....Q", new[] { "rooms.closet legend has no entry for \"Q\"" }),
            ("legend pointing nowhere", "closet", r => { r["legend"] = new JObject { ["Q"] = "lava" }; r["rows"]![1] = ".....Q"; }, new[] { "rooms.closet terrain has no definition for \"lava\"" }),
            ("player start off the map", "closet", r => r["playerStart"] = "G5", new[] { "rooms.closet.playerStart \"G5\" is off the map" }),
            ("player start not a tile", "closet", r => r["playerStart"] = "here", new[] { "rooms.closet.playerStart \"here\" is not a tile name" }),
            ("player start in a wall", "closet", r => r["playerStart"] = "F1", new[] { "rooms.closet.playerStart \"F1\" is on terrain that blocks movement" }),
            ("spawn in the void", "hallways", r => WaveUnit(r, 0)["tile"] = "A1", new[] { "rooms.hallways.waves[0] spawn tile \"A1\" is on terrain that blocks movement" }),
            ("spawn off the map", "closet", r => WaveUnit(r, 1)["tile"] = "A9", new[] { "rooms.closet.waves[1] spawn tile \"A9\" is off the map" }),
            ("unknown unit", "closet", r => WaveUnit(r, 0)["def"] = "dragon", new[] { "rooms.closet.waves[0] uses unknown unit \"dragon\"" }),
            ("wave order", "closet", r => r["waves"]![1]!["round"] = 0, new[] { "rooms.closet.waves[1].round must be an integer >= 1 (got 0)", "rooms.closet.waves[1] arrives before the wave listed above it" }),
            ("wave units", "closet", r => r["waves"]![0]!["units"] = "all", new[] { "rooms.closet.waves[0].units must be a list" }),
            ("no waves", "closet", r => r["waves"] = new JArray(), new[] { "rooms.closet has no waves" }),
            ("hazard def", "closet", r => Hazard(r, 1)["def"] = "lava", new[] { "rooms.closet hazard vent uses unknown hazard def \"lava\"" }),
            ("hazard tile", "closet", r => Hazard(r, 1)["tiles"] = new JArray("H9"), new[] { "rooms.closet hazard vent tile \"H9\" is off the map" }),
            ("hazard with no tiles", "closet", r => Hazard(r, 1)["tiles"] = new JArray(), new[] { "rooms.closet hazard vent needs at least one tile" }),
            ("two hazards with one id", "closet", r => Hazard(r, 1)["id"] = "gun", new[] { "rooms.closet has two hazards with the id \"gun\"" }),
            ("hazard with no id", "closet", r => Hazard(r, 1).Remove("id"), new[] { "rooms.closet has a hazard with no id" }),
            ("lane with no direction", "closet", r => Hazard(r, 0).Remove("direction"), new[] { "rooms.closet hazard gun is a lane and needs a direction" }),
            ("hazards not a list", "closet", r => r["hazards"] = new JObject(), new[] { "rooms.closet.hazards must be a list" }),
            ("terrain flag", "hallways", r => ((JObject)r["terrain"]!["void"]!).Remove("blocksSight"), new[] { "rooms.hallways terrain \"void\" needs blocksSight to be true or false (got undefined)" }),
        };

        private static IEnumerable<TestCaseData> BadRoomCases() => BadRooms.Select(b => new TestCaseData(b.Room, b.Change, b.Expected).SetName($"Rejects a bad {b.Name}"));

        [TestCaseSource(nameof(BadRoomCases))]
        public void RejectsABadRoom(string room, Action<JObject> change, string[] expected)
        {
            Assert.That(Edit(room, change), Is.EqualTo(expected));
        }

        [Test]
        public void LimitsARoomToWhatTileNamesCanAddress()
        {
            var wide = SealedRoom(5);
            wide["width"] = 27;
            wide["rows"] = new JArray("@#." + new string('.', 24));
            Assert.That(Problems(new JArray(wide)), Is.EqualTo(new[] { "rooms.sealed.width must be at most 26, the columns A to Z (got 27)" }));
            var tall = SealedRoom(5);
            tall["width"] = 1;
            tall["height"] = 100;
            tall["rows"] = new JArray(Enumerable.Repeat(".", 100));
            tall["waves"]![0]!["units"]![0]!["tile"] = "A2";
            Assert.That(Problems(new JArray(tall)), Is.EqualTo(new[] { "rooms.sealed.height must be at most 99, the rows 1 to 99 (got 100)" }));
        }

        [Test]
        public void AppliesTheSameChecksToTheArmoryUnderItsOwnFileNames()
        {
            var start = FreshFiles();
            start["map"]!["playerStart"] = "H2";
            Assert.That(ContentLoader.Validate(start), Is.EqualTo(new[] { "map.playerStart \"H2\" is on terrain that blocks movement" }));
            var noRows = FreshFiles();
            ((JObject)noRows["map"]!).Remove("rows");
            Assert.That(ContentLoader.Validate(noRows), Does.Contain("map.rows must be a list"));
            var flags = FreshFiles();
            ((JObject)flags["map"]!["terrain"]!["wall"]!).Remove("blocksMove");
            Assert.That(ContentLoader.Validate(flags), Is.EqualTo(new[] { "map terrain \"wall\" needs blocksMove to be true or false (got undefined)" }));
        }

        [Test]
        public void RejectsBadRoomsWhenContentIsBuilt()
        {
            var bad = RoomFile("closet");
            bad["roundCap"] = 0;
            var e = Assert.Throws<ContentError>(() => WithRooms(new JArray(bad)));
            Assert.That(e!.Message, Does.Contain("rooms.closet.roundCap"));
        }
    }
}
