#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LabEscape.Core;
using Newtonsoft.Json.Linq;

namespace LabEscape.Client
{
    /// <summary>Small saved values: PlayerPrefs in the game, a dictionary in tests. What it gives back is untrusted.</summary>
    public interface IPrefs
    {
        string? Get(string key);
        void Set(string key, string value);
    }

    /// <summary>
    /// A fight's telemetry, as the web client keeps it (src/client/telemetry.ts): the replay, the summary and the event stream,
    /// in a folder named for the time, the tester and the outcome, so the playtest report (`npm run sim -- telemetry`) reads
    /// Unity's fights and the web client's alike. No Unity code: where the files go is TelemetrySink's business.
    /// </summary>
    public static class Telemetry
    {
        public const string TesterKey = "lab-escape.tester";
        public const string AttemptsKey = "lab-escape.attemptCounts";

        /// <summary>The three files of a fight, by name, in the web client's order and format.</summary>
        public static List<(string Name, string Text)> Files(FightSession s) => new List<(string, string)>
        {
            ("replay.json", s.Record.ToJson()),
            ("summary.json", Hash.Stringify(s.Summary().ToToken(), 2)),
            ("events.jsonl", Records.EventsToJsonl(s.Events)),
        };

        /// <summary>The outcome as a word, as the web client writes it into folder names.</summary>
        public static string OutcomeWord(Outcome? o) => o == null ? "unfinished" : o.Result == FightResult.Win ? "win" : "lose";

        public static string FolderName(double unixMs, FightSession s) => Labels.TelemetryFolderName(unixMs, s.Record.Meta, OutcomeWord(s.State.Outcome));

        /// <summary>Given as the tester, forgets the remembered one: for after a playtest.</summary>
        public const string NoTester = "-";

        /// <summary>
        /// Who is playing: the tester given this run (`-labTester`, or `?tester=` in the web build), which is remembered, or else
        /// the one remembered from last time. Cleaned as the web client cleans it; null for nobody. "-" forgets the tester.
        /// </summary>
        public static string? ChooseTester(string? given, IPrefs prefs)
        {
            if (given == NoTester)
            {
                prefs.Set(TesterKey, "");
                return null;
            }
            var cleaned = Labels.CleanTester(given);
            if (cleaned != null)
            {
                prefs.Set(TesterKey, cleaned);
                return cleaned;
            }
            return Labels.CleanTester(prefs.Get(TesterKey));
        }

        /// <summary>
        /// Numbers a finished fight for its tester: one more than the fights that tester has finished here, kept in the replay's
        /// meta. A fight with no tester gets none. The stored counts are untrusted: anything but a map of whole numbers is read
        /// as no counts at all.
        /// </summary>
        public static void AssignAttempt(RecordMeta meta, IPrefs prefs)
        {
            var tester = meta.Tester;
            if (tester == null) return;
            var counts = ReadCounts(prefs.Get(AttemptsKey));
            var attempt = (counts.TryGetValue(tester, out var n) ? n : 0) + 1;
            meta.Attempt = attempt;
            counts[tester] = attempt;
            var o = new JObject();
            foreach (var pair in counts) o[pair.Key] = pair.Value;
            prefs.Set(AttemptsKey, o.ToString(Newtonsoft.Json.Formatting.None));
        }

        private static Dictionary<string, long> ReadCounts(string? raw)
        {
            var counts = new Dictionary<string, long>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(raw)) return counts;
            try
            {
                if (!(JToken.Parse(raw!) is JObject o)) return counts;
                foreach (var p in o.Properties())
                {
                    if (p.Value.Type != JTokenType.Integer) return new Dictionary<string, long>(StringComparer.Ordinal);
                    var v = p.Value.Value<long>();
                    if (v < 0 || v > 1_000_000) return new Dictionary<string, long>(StringComparer.Ordinal);
                    counts[p.Name] = v;
                }
                return counts;
            }
            catch (Exception)
            {
                return new Dictionary<string, long>(StringComparer.Ordinal);
            }
        }

        /// <summary>
        /// A replay from untrusted text: it must parse, and be a whole replay of this engine version and this content, or it is
        /// refused with the reason, which the start screen shows.
        /// </summary>
        public static FightRecord LoadReplay(Content c, string text)
        {
            JToken raw;
            try
            {
                raw = JsonIn.Parse(text);
            }
            catch (Exception e)
            {
                throw new ReplayMismatch($"The file is not JSON: {e.Message}");
            }
            return Records.CheckCompatible(c, raw);
        }

        /// <summary>A saved fight on disk: its folder, and a label for the start screen.</summary>
        public sealed class SavedFight
        {
            public string Folder = "";
            public string Label = "";
        }

        /// <summary>
        /// The most recent saved fights under `root` that have a replay, newest first. Folder names start with the time, so their
        /// order is the order the fights were played in.
        /// </summary>
        public static List<SavedFight> Recent(string? root, int most)
        {
            if (root == null || !Directory.Exists(root)) return new List<SavedFight>();
            return Directory.GetDirectories(root)
                .Where(d => File.Exists(Path.Combine(d, "replay.json")))
                .OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal)
                .Take(most)
                .Select(d => new SavedFight { Folder = d, Label = LabelFor(Path.GetFileName(d)) })
                .ToList();
        }

        /// <summary>
        /// "2026-11-12T15-30-04-512Z-P3-a2-tech-win" reads "2026-11-12 15:30  P3 a2 tech win". A name in another form is shown as
        /// it is.
        /// </summary>
        public static string LabelFor(string folder)
        {
            if (folder.Length < 25 || folder[10] != 'T' || folder[23] != 'Z') return folder;
            var date = folder.Substring(0, 10);
            var time = folder.Substring(11, 5).Replace('-', ':');
            var rest = folder.Length > 25 ? folder.Substring(25).Replace('-', ' ') : "";
            return rest.Length > 0 ? $"{date} {time}  {rest}" : $"{date} {time}";
        }

        /// <summary>The value of one query parameter in a page's URL, or null. For `?tester=` and `?replay=` in the web build.</summary>
        public static string? QueryParam(string? url, string name)
        {
            if (string.IsNullOrEmpty(url)) return null;
            var q = url!.IndexOf('?');
            if (q < 0) return null;
            var query = url.Substring(q + 1);
            var hash = query.IndexOf('#');
            if (hash >= 0) query = query.Substring(0, hash);
            foreach (var part in query.Split('&'))
            {
                var eq = part.IndexOf('=');
                var key = eq < 0 ? part : part.Substring(0, eq);
                if (Unescape(key) != name) continue;
                return eq < 0 ? "" : Unescape(part.Substring(eq + 1));
            }
            return null;
        }

        private static string Unescape(string s) => Uri.UnescapeDataString(s.Replace('+', ' '));

        /// <summary>The value after a command-line flag such as `-labTester P3`, or null.</summary>
        public static string? Arg(string[] args, string flag)
        {
            var at = Array.IndexOf(args, flag);
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
        }
    }
}
