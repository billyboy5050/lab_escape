#nullable enable
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace LabEscape.Client
{
    /// <summary>
    /// The web client's two faces, taken from the system as the web client takes them (`--display` and `--mono` in
    /// src/client/styles.css), so the project keeps no font files: a condensed face for titles, labels and banners, and a
    /// monospace for keys, numbers and the logs. An element asks for one with the class `display` or `mono`. Where no font in
    /// a list is installed, as in the web build, which has no system fonts, the element keeps Unity's default.
    /// </summary>
    public static class Fonts
    {
        private static readonly (string Family, string Style)[] DisplayFaces =
        {
            ("DIN Condensed", "Bold"),
            ("Bahnschrift", "SemiBold SemiCondensed"),
            ("Bahnschrift", "Regular"),
            ("Roboto Condensed", "Bold"),
            ("Roboto Condensed", "Regular"),
            ("Arial Narrow", "Bold"),
            ("Arial Narrow", "Regular"),
        };

        private static readonly (string Family, string Style)[] MonoFaces =
        {
            ("SF Mono", "Regular"),
            ("Menlo", "Regular"),
            ("Consolas", "Regular"),
            ("Liberation Mono", "Regular"),
            ("DejaVu Sans Mono", "Book"),
        };

        private static bool _loaded;
        private static FontDefinition? _display;
        private static FontDefinition? _mono;

        /// <summary>For a check of the fallback: when set before the first use, no system font is looked up.</summary>
        public static bool NoSystemFonts;

        public static string DisplayName { get; private set; } = "default";
        public static string MonoName { get; private set; } = "default";

        /// <summary>
        /// Gives an element the face its class asks for, if that face was found, and marks it with the class `face` so Fight.uss
        /// can size it for that face. The display face is already bold, so it is not made bolder.
        /// </summary>
        public static void Apply(VisualElement e)
        {
            Load();
            if (e.ClassListContains("display") && _display.HasValue)
            {
                e.style.unityFontDefinition = _display.Value;
                e.style.unityFontStyleAndWeight = FontStyle.Normal;
                e.AddToClassList("face");
            }
            else if (e.ClassListContains("mono") && _mono.HasValue)
            {
                e.style.unityFontDefinition = _mono.Value;
                e.AddToClassList("face");
            }
        }

        /// <summary>Applies the faces to every element under `root` that asks for one: the elements written in Fight.uxml.</summary>
        public static void ApplyAll(VisualElement root)
        {
            root.Query(className: "display").ForEach(Apply);
            root.Query(className: "mono").ForEach(Apply);
        }

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            if (NoSystemFonts) return;
            var display = First(DisplayFaces);
            var mono = First(MonoFaces);
            if (display.Asset != null)
            {
                _display = FontDefinition.FromSDFFont(display.Asset);
                DisplayName = display.Name;
            }
            if (mono.Asset != null)
            {
                _mono = FontDefinition.FromSDFFont(mono.Asset);
                MonoName = mono.Name;
            }
            Debug.Log($"[Fonts] display: {DisplayName}; mono: {MonoName}");
        }

        private static string[]? _installed;

        private static string[] Installed()
        {
            if (_installed != null) return _installed;
            try
            {
                _installed = Font.GetOSInstalledFontNames() ?? Array.Empty<string>();
            }
            catch (Exception)
            {
                _installed = Array.Empty<string>();
            }
            return _installed;
        }

        private static (FontAsset? Asset, string Name) First((string Family, string Style)[] faces)
        {
            // Only families the system lists are tried, so a font that is missing is skipped without a warning in the log.
            var installed = Installed();
            foreach (var (family, style) in faces)
            {
                if (!installed.Any(n => n == family || n.StartsWith(family + " ", StringComparison.Ordinal) || n.StartsWith(family + "-", StringComparison.Ordinal))) continue;
                FontAsset? asset = null;
                try
                {
                    asset = FontAsset.CreateFontAsset(family, style);
                }
                catch (Exception)
                {
                    // A font the system cannot open is the same as one it does not have.
                }
                if (asset == null) continue;
                asset.hideFlags = HideFlags.HideAndDontSave;
                return (asset, $"{family} {style}");
            }
            return (null, "default");
        }
    }
}
