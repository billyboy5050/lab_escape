#nullable enable
using System.IO;
using System.Linq;
using LabEscape.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace LabEscape.Client.Tests
{
    /// <summary>Hot reload's file watching: what counts as a change, what is accepted, and what leaves the old values in place.</summary>
    public class ContentWatchTests
    {
        private ScratchContent _scratch = null!;
        private ContentWatch _watch = null!;
        private string _hash = "";

        [SetUp]
        public void CopyContent()
        {
            _scratch = new ScratchContent();
            _watch = new ContentWatch(_scratch.Folder);
            _hash = ContentLoader.FromDirectory(_scratch.Folder).Hash;
            _watch.Hash = _hash;
        }

        [TearDown]
        public void RemoveCopy() => _scratch.Dispose();

        /// <summary>Polls until something is found, as the game does: a change is read on the second poll that sees it.</summary>
        private WatchResult Settle()
        {
            var first = _watch.Poll();
            Assert.That(first.Kind, Is.EqualTo(WatchKind.Nothing), "a change is not read the first time it is seen");
            return _watch.Poll();
        }

        [Test]
        public void NothingHappensWhileTheFilesAreUntouched()
        {
            for (var i = 0; i < 4; i++) Assert.That(_watch.Poll().Kind, Is.EqualTo(WatchKind.Nothing));
            Assert.That(_watch.Hash, Is.EqualTo(_hash));
        }

        [Test]
        public void ASaveThatChangesNothingIsNotAReload()
        {
            _scratch.Touch("units");
            Assert.That(Settle().Kind, Is.EqualTo(WatchKind.Nothing));
            Assert.That(_watch.Hash, Is.EqualTo(_hash));
            Assert.That(_watch.Poll().Kind, Is.EqualTo(WatchKind.Nothing), "and it is not read again");
        }

        [Test]
        public void AChangeIsReadOnceItHasHeldStill()
        {
            _scratch.SetUnitHp("guard", 7);
            var r = Settle();
            Assert.That(r.Kind, Is.EqualTo(WatchKind.Reloaded));
            Assert.That(r.Content!.Units["guard"].Hp, Is.EqualTo(7));
            Assert.That(r.Content.Hash, Is.Not.EqualTo(_hash));
            Assert.That(_watch.Hash, Is.EqualTo(r.Content.Hash), "the watch now compares with the new content");
            Assert.That(_watch.Poll().Kind, Is.EqualTo(WatchKind.Nothing), "and reads it once");
        }

        [Test]
        public void ReloadedContentIsWhatALoadOfTheSameFilesGives()
        {
            _scratch.SetUnitHp("guard", 7);
            var r = Settle();
            Assert.That(r.Content!.Hash, Is.EqualTo(ContentLoader.FromDirectory(_scratch.Folder).Hash));
        }

        [Test]
        public void ASaveStillInProgressIsNotRead()
        {
            _scratch.SetUnitHp("guard", 7);
            Assert.That(_watch.Poll().Kind, Is.EqualTo(WatchKind.Nothing));
            _scratch.SetUnitHp("guard", 8);
            Assert.That(_watch.Poll().Kind, Is.EqualTo(WatchKind.Nothing), "the files moved again, so the wait starts over");
            var r = _watch.Poll();
            Assert.That(r.Kind, Is.EqualTo(WatchKind.Reloaded));
            Assert.That(r.Content!.Units["guard"].Hp, Is.EqualTo(8));
        }

        [Test]
        public void InvalidContentIsRejectedAndTheOldValuesKept()
        {
            _scratch.SetUnitHp("guard", -3);
            var r = Settle();
            Assert.That(r.Kind, Is.EqualTo(WatchKind.Rejected));
            Assert.That(r.Content, Is.Null);
            Assert.That(r.Problems, Is.Not.Empty);
            Assert.That(r.Summary, Does.Contain("guard"));
            Assert.That(_watch.Hash, Is.EqualTo(_hash), "the hash the game holds does not move");
            Assert.That(_watch.Rejected, Is.True);
            Assert.That(_watch.Poll().Kind, Is.EqualTo(WatchKind.Nothing), "a rejection is reported once");
        }

        [Test]
        public void AFixAfterARejectionReloads()
        {
            _scratch.SetUnitHp("guard", -3);
            Assert.That(Settle().Kind, Is.EqualTo(WatchKind.Rejected));
            _scratch.SetUnitHp("guard", 9);
            var r = Settle();
            Assert.That(r.Kind, Is.EqualTo(WatchKind.Reloaded));
            Assert.That(r.Content!.Units["guard"].Hp, Is.EqualTo(9));
            Assert.That(_watch.Rejected, Is.False);
        }

        [Test]
        public void PuttingTheOldValuesBackClearsTheError()
        {
            var original = _scratch.Read("units");
            _scratch.SetUnitHp("guard", -3);
            Assert.That(Settle().Kind, Is.EqualTo(WatchKind.Rejected));
            _scratch.Write("units", original);
            Assert.That(Settle().Kind, Is.EqualTo(WatchKind.Restored));
            Assert.That(_watch.Hash, Is.EqualTo(_hash));
            Assert.That(_watch.Rejected, Is.False);
        }

        [Test]
        public void BrokenJsonIsRejectedWithItsFilesName()
        {
            _scratch.Write("waves", "[ { \"round\": ");
            var r = Settle();
            Assert.That(r.Kind, Is.EqualTo(WatchKind.Rejected));
            Assert.That(r.Problems.Single(), Does.StartWith("waves.json:"));
            Assert.That(_watch.Hash, Is.EqualTo(_hash));
        }

        [Test]
        public void EveryBrokenFileIsNamed()
        {
            _scratch.Write("waves", "nope");
            _scratch.Write("rules", "{");
            var r = Settle();
            Assert.That(r.Kind, Is.EqualTo(WatchKind.Rejected));
            Assert.That(r.Problems.Count, Is.EqualTo(2));
            Assert.That(r.Summary, Does.Contain("and 1 more"));
        }

        [Test]
        public void AMissingFileIsRejectedUntilItComesBack()
        {
            var original = _scratch.Read("hazards");
            _scratch.Delete("hazards");
            var r = Settle();
            Assert.That(r.Kind, Is.EqualTo(WatchKind.Rejected));
            Assert.That(r.Problems.Single(), Does.StartWith("hazards.json:"));
            _scratch.Write("hazards", original);
            Assert.That(Settle().Kind, Is.EqualTo(WatchKind.Restored));
        }

        [Test]
        public void AGameThatStartedOnBrokenContentLoadsItOnceItIsFixed()
        {
            var original = _scratch.Read("units");
            _scratch.Write("units", "{");
            var watch = new ContentWatch(_scratch.Folder);
            Assert.That(watch.Hash, Is.Null, "it holds nothing yet");
            _scratch.Write("units", original);
            Assert.That(watch.Poll().Kind, Is.EqualTo(WatchKind.Nothing));
            var r = watch.Poll();
            Assert.That(r.Kind, Is.EqualTo(WatchKind.Reloaded));
            Assert.That(r.Content!.Hash, Is.EqualTo(_hash));
        }

        [Test]
        public void AFileInTheFolderThatIsNotContentIsIgnored()
        {
            File.WriteAllText(Path.Combine(_scratch.Folder, "notes.json"), "{}");
            Assert.That(_watch.Poll().Kind, Is.EqualTo(WatchKind.Nothing));
            Assert.That(_watch.Poll().Kind, Is.EqualTo(WatchKind.Nothing));
        }

        [Test]
        public void ARejectionGivesTheProblemsTheLoaderGives()
        {
            _scratch.SetUnitHp("guard", new JValue("many"));
            var r = Settle();
            Assert.That(r.Kind, Is.EqualTo(WatchKind.Rejected));
            var e = Assert.Throws<ContentError>(() => ContentLoader.FromDirectory(_scratch.Folder))!;
            Assert.That(r.Problems, Is.EqualTo(e.Problems));
        }
    }
}
