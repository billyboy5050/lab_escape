#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace LabEscape.Client
{
    /// <summary>
    /// The web client's two faces: a condensed face for titles, labels and banners, and a monospace for keys, numbers and the
    /// logs. An element asks for one with the class `display` or `mono`. They are taken from the system as the web client takes
    /// them (`--display` and `--mono` in src/client/styles.css). Where the system has none of a list, the game uses the face it
    /// ships in Resources/Fonts: Barlow Condensed SemiBold for display, and DejaVu Sans Mono, which Menlo is built from, for mono.
    /// A machine with no system fonts at all, such as the web build, also has nothing to draw the symbols the interface writes
    /// (→, –, ⚠ and the rest) when a face lacks them, so there every face falls back to DejaVu Sans Mono, which has them all,
    /// and the text with no face of its own keeps Unity's default face with the same fallback.
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

        private const string ShippedDisplayPath = "Fonts/BarlowCondensed-SemiBold";
        private const string ShippedMonoPath = "Fonts/DejaVuSansMono";

        /// <summary>The face the default theme gives every element, which Unity builds into every player.</summary>
        private const string DefaultFace = "LegacyRuntime.ttf";

        private static bool _loaded;
        private static FontDefinition? _display;
        private static FontDefinition? _mono;
        private static FontDefinition? _body;

        /// <summary>
        /// For a check of the faces that the web build gets: when set before the first use, or when the game is started with
        /// -labNoSystemFonts, no system font is looked up, and the shipped faces are used as they are in the web build.
        /// </summary>
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

        /// <summary>
        /// Text in capitals where a condensed face was found, as the web client sets it; as it is where it was not, because the
        /// default face is too wide for a long name in capitals to fit a button.
        /// </summary>
        public static string Caps(string text)
        {
            Load();
            return _display.HasValue ? text.ToUpperInvariant() : text;
        }

        /// <summary>
        /// Applies the faces to every element under `root` that asks for one: the elements written in Fight.uxml. With no system
        /// fonts, `root` also gets the default face with the symbol fallback, and the elements under it inherit that.
        /// </summary>
        public static void ApplyAll(VisualElement root)
        {
            Load();
            if (_body.HasValue) root.style.unityFontDefinition = _body.Value;
            root.Query(className: "display").ForEach(Apply);
            root.Query(className: "mono").ForEach(Apply);
        }

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            var system = !NoSystemFonts && Array.IndexOf(Environment.GetCommandLineArgs(), "-labNoSystemFonts") < 0 && Installed().Length > 0;
            var display = system ? First(DisplayFaces) : (null, "default");
            var mono = system ? First(MonoFaces) : (null, "default");
            if (display.Asset == null && Shipped.Display != null) display = (Shipped.Display, "Barlow Condensed SemiBold (shipped)");
            if (mono.Asset == null && Shipped.Mono != null) mono = (Shipped.Mono, "DejaVu Sans Mono (shipped)");
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
            if (!system && Shipped.Body != null) _body = FontDefinition.FromSDFFont(Shipped.Body);
            Debug.Log($"[Fonts] display: {DisplayName}; mono: {MonoName}{(_body.HasValue ? "; symbols: DejaVu Sans Mono" : "")}");
        }

        /// <summary>
        /// The faces the game carries for a machine without the system's, each made once. Display and the default face fall back
        /// to mono for the symbols they lack. A face is null if its font cannot be read, as in a checkout made without Git LFS,
        /// which has small pointer files where the fonts should be; text then keeps Unity's default face.
        /// </summary>
        public static class Shipped
        {
            private static bool _made;
            private static FontAsset? _display;
            private static FontAsset? _mono;
            private static FontAsset? _body;

            /// <summary>Barlow Condensed SemiBold, falling back to DejaVu Sans Mono.</summary>
            public static FontAsset? Display { get { Make(); return _display; } }

            /// <summary>DejaVu Sans Mono, which has every symbol the interface writes.</summary>
            public static FontAsset? Mono { get { Make(); return _mono; } }

            /// <summary>Unity's default face, falling back to DejaVu Sans Mono.</summary>
            public static FontAsset? Body { get { Make(); return _body; } }

            private static void Make()
            {
                if (_made) return;
                _made = true;
                _mono = Asset(Resources.Load<Font>(ShippedMonoPath));
                _display = Asset(Resources.Load<Font>(ShippedDisplayPath));
                Font? builtIn = null;
                try
                {
                    builtIn = Resources.GetBuiltinResource<Font>(DefaultFace);
                }
                catch (Exception)
                {
                    // A player without it keeps the theme's face with no fallback.
                }
                _body = Asset(builtIn);
                if (_mono == null) return;
                if (_display != null) _display.fallbackFontAssetTable = new List<FontAsset> { _mono };
                if (_body != null) _body.fallbackFontAssetTable = new List<FontAsset> { _mono };
            }

            private static FontAsset? Asset(Font? font)
            {
                if (font == null) return null;
                FontAsset? asset = null;
                try
                {
                    asset = FontAsset.CreateFontAsset(font);
                }
                catch (Exception)
                {
                    // A font that cannot be read is the same as one that is not there.
                }
                if (asset != null) asset.hideFlags = HideFlags.HideAndDontSave;
                return asset;
            }
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
