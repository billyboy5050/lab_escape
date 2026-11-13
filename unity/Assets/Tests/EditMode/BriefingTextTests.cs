#nullable enable
using System.Linq;
using LabEscape.Core;
using NUnit.Framework;

namespace LabEscape.Client.Tests
{
    /// <summary>What the briefing says, from the content: no number in it is written here.</summary>
    public class BriefingTextTests
    {
        private static readonly Content C = GameContent.Load();

        [Test]
        public void TheOpeningIsTheIntercomsLineForRoundOneInQuotes()
        {
            var line = C.Intercom.Lines.First(l => l.Trigger.Type == TriggerTypes.RoundStart && l.Trigger.Round == 1);
            Assert.That(BriefingText.Opening(C), Is.EqualTo($"\"{line.Text}\""));
        }

        [Test]
        public void TheObjectiveCountsTheWavesInPlayAndNamesTheRoundCap()
        {
            var room = C.DefaultRoom;
            var all = BriefingText.Objective(C, room, room.Waves.Count);
            Assert.That(all[0], Does.Contain($"{C.Units["player"].Hp} HP").And.Contain($"arrives in {room.Waves.Count} waves"));
            Assert.That(all[2], Is.EqualTo($"Lose at 0 HP, or if round {room.RoundCap} ends with the fight undecided."));
            Assert.That(BriefingText.Objective(C, room, 1)[0], Does.Contain("arrives in 1 wave:"));
        }

        [Test]
        public void HoldingWave3BackLeavesTwoWavesInPlayAndNeverMoreThanTheRoomHas()
        {
            var room = C.DefaultRoom;
            Assert.That(BriefingText.WavesInPlay(room, new FightSettings()), Is.EqualTo(room.Waves.Count));
            Assert.That(BriefingText.WavesInPlay(room, new FightSettings { MaxWaves = 2 }), Is.EqualTo(2));
            Assert.That(BriefingText.WavesInPlay(room, new FightSettings { MaxWaves = 99 }), Is.EqualTo(room.Waves.Count));
            Assert.That(BriefingText.Grid(room, 2), Is.EqualTo($"{room.Width}x{room.Height} grid · 2 waves"));
        }

        [Test]
        public void TheDevicesAreNamedWithTheTilesTheyStandOn()
        {
            var text = BriefingText.Devices(C, C.DefaultRoom);
            foreach (var h in C.DefaultRoom.Hazards)
            {
                Assert.That(text, Does.Contain(C.HazardDefs[h.Def].Name));
                foreach (var tile in h.Tiles) Assert.That(text, Does.Contain(tile));
            }
        }

        [Test]
        public void TheKitIsTheAbilitiesThenTheUpgradesWithTheirKits()
        {
            var tech = C.Presets.First(p => p.Id == "tech");
            var kit = BriefingText.Kit(C, new Loadout(tech.Abilities, tech.Upgrades));
            Assert.That(kit.Select(k => k.Name), Is.EqualTo(tech.Abilities.Select(a => C.Abilities[a].Name).Concat(tech.Upgrades.Select(u => C.Upgrades[u].Name))));
            Assert.That(kit.First().Kit, Is.EqualTo(C.Abilities[tech.Abilities[0]].Kit));
        }

        [Test]
        public void ThePlanSeesWallsFloorsAndBarriersAsTheBoardDoes()
        {
            var room = C.DefaultRoom;
            var shutter = Enumerable.Range(0, room.Width).SelectMany(x => Enumerable.Range(0, room.Height).Select(y => new Pos(x, y))).First(p => room.Board.TerrainId(p) == "shutter");
            Assert.That(RoomPlan.Kind(room, shutter), Is.EqualTo("barrier"), "a shutter blocks movement and not sight");
            var wall = Enumerable.Range(0, room.Width).SelectMany(x => Enumerable.Range(0, room.Height).Select(y => new Pos(x, y))).First(p => room.Board.TerrainId(p) == "wall");
            Assert.That(RoomPlan.Kind(room, wall), Is.EqualTo("wall"));
            Assert.That(RoomPlan.Kind(room, Tiles.Parse(room.PlayerStart)), Is.EqualTo("floor"));
        }
    }
}
