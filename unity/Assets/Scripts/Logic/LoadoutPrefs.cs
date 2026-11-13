#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using LabEscape.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LabEscape.Client
{
    /// <summary>
    /// The last loadout, remembered between sessions as the web client remembers it. What comes back from the store is untrusted
    /// (CLAUDE.md, invariant 7): anything that is not a loadout this content accepts is dropped for the default preset.
    /// </summary>
    public static class LoadoutPrefs
    {
        public const string LoadoutKey = "lab-escape.loadout";
        public const string PresetKey = "lab-escape.preset";

        /// <summary>The preset the first session opens on, when the content has it.</summary>
        public const string DefaultPresetId = "hybrid";

        public static void Save(IPrefs prefs, Loadout loadout, string? presetId)
        {
            var o = new JObject { ["abilities"] = new JArray(loadout.Abilities), ["upgrades"] = new JArray(loadout.Upgrades) };
            prefs.Set(LoadoutKey, o.ToString(Formatting.None));
            prefs.Set(PresetKey, presetId ?? "");
        }

        /// <summary>
        /// The loadout and preset to open the loadout screen on. A preset that still exists gives its own picks, so it follows an
        /// edit to the content; otherwise the saved picks stand if they are a loadout this content accepts, and if they are not the
        /// default preset's do.
        /// </summary>
        public static (Loadout Loadout, string? PresetId) Load(IPrefs prefs, Content c)
        {
            var presetId = prefs.Get(PresetKey);
            var preset = string.IsNullOrEmpty(presetId) ? null : c.Presets.FirstOrDefault(p => p.Id == presetId);
            if (preset != null) return (new Loadout(preset.Abilities, preset.Upgrades), preset.Id);
            var saved = ParseLoadout(prefs.Get(LoadoutKey));
            if (saved != null && saved.Abilities.Count > 0 && Engine.LoadoutProblems(c, saved).Count == 0) return (saved, null);
            var fallback = c.Presets.FirstOrDefault(p => p.Id == DefaultPresetId) ?? c.Presets[0];
            return (new Loadout(fallback.Abilities, fallback.Upgrades), fallback.Id);
        }

        private static Loadout? ParseLoadout(string? text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            try
            {
                var o = JsonIn.Parse(text!) as JObject;
                var abilities = Strings(o?["abilities"]);
                var upgrades = Strings(o?["upgrades"]);
                return abilities == null || upgrades == null ? null : new Loadout(abilities, upgrades);
            }
            catch (Exception e) when (e is JsonException || e is FormatException || e is InvalidCastException)
            {
                return null;
            }
        }

        /// <summary>The strings of an array of strings, or null if it is anything else.</summary>
        private static List<string>? Strings(JToken? token)
        {
            if (!(token is JArray array)) return null;
            var list = new List<string>();
            foreach (var item in array)
            {
                if (item.Type != JTokenType.String) return null;
                list.Add(item.Value<string>()!);
            }
            return list;
        }
    }
}
