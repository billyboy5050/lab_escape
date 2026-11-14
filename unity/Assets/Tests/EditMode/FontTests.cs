#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine.TextCore.Text;

namespace LabEscape.Client.Tests
{
    /// <summary>
    /// The faces the game ships for a machine with no system fonts, which the web build is. There a face that lacks a character
    /// draws a blank, so each of them must have every character the game writes, counting its fallback.
    /// </summary>
    public class FontTests
    {
        /// <summary>Every character above ASCII in the client's code, its UXML and the content, leaving out whole-line comments.</summary>
        private static List<char> Written()
        {
            var files = Directory.GetFiles(GameContent.RepoPath("unity", "Assets", "Scripts"), "*.cs", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(GameContent.RepoPath("unity", "Assets", "Resources", "UI"), "*.uxml"))
                .Concat(Directory.GetFiles(GameContent.RepoPath("content"), "*.json"));
            return files
                .SelectMany(File.ReadAllLines)
                .Where(line => !line.TrimStart().StartsWith("//"))
                .SelectMany(line => line)
                .Where(c => c > 0x7F)
                .Distinct()
                .OrderBy(c => c)
                .ToList();
        }

        private static IEnumerable<(string Name, FontAsset? Face)> Faces() => new[]
        {
            ("display", Fonts.Shipped.Display),
            ("mono", Fonts.Shipped.Mono),
            ("default", Fonts.Shipped.Body),
        };

        [Test]
        public void TheShippedFacesLoad()
        {
            foreach (var (name, face) in Faces())
            {
                Assert.That(face, Is.Not.Null, $"The shipped {name} face did not load. A checkout made without Git LFS has pointer files in place of the fonts.");
            }
        }

        [Test]
        public void EveryShippedFaceDrawsEveryCharacterTheGameWrites()
        {
            var written = Written();
            // The arrow is in the action line and the log, so a scan that misses it is not reading the code.
            Assert.That(written, Does.Contain('→'));
            foreach (var (name, face) in Faces())
            {
                Assert.That(face, Is.Not.Null);
                var missing = written.Where(c => !face!.HasCharacter(c, true, true)).ToList();
                Assert.That(missing, Is.Empty, $"The shipped {name} face has no {string.Join(" ", missing.Select(c => $"U+{(int)c:X4} {c}"))}");
            }
        }

        [Test]
        public void BarlowAloneHasNoArrowSoTheCheckAboveCanFail()
        {
            // Were the system's fonts searched too, every face would pass on a Mac and the check above would prove nothing.
            var alone = FontAsset.CreateFontAsset(UnityEngine.Resources.Load<UnityEngine.Font>("Fonts/BarlowCondensed-SemiBold"));
            try
            {
                Assert.That(alone.HasCharacter('A', true, true), Is.True);
                Assert.That(alone.HasCharacter('→', true, true), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(alone);
            }
        }

        [Test]
        public void TheDisplayAndDefaultFacesFallBackToMono()
        {
            Assert.That(Fonts.Shipped.Display!.fallbackFontAssetTable, Is.EqualTo(new[] { Fonts.Shipped.Mono }));
            Assert.That(Fonts.Shipped.Body!.fallbackFontAssetTable, Is.EqualTo(new[] { Fonts.Shipped.Mono }));
        }
    }
}
