#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace LabEscape.Core
{
    /// <summary>
    /// Intercom rules: each line plays at most once per fight and at most one line plays per phase. Lines triggered together
    /// queue in table order and play one per phase. The player's death line plays at once and clears the queue. Lines raised
    /// during a root action wait for it to finish, so lines triggered by the same action play in table order.
    /// </summary>
    public static class IntercomRules
    {
        /// <param name="threshold">For hpBelow: the threshold that was just crossed.</param>
        public static void Trigger(World w, string type, int? round = null, string? def = null, int? threshold = null)
        {
            var lines = w.C.Intercom.Lines;
            var ic = w.S.Intercom;
            var changed = false;
            foreach (var line in lines)
            {
                if (!Matches(line, type, round, def, threshold)) continue;
                if (ic.Played.Contains(line.Id) || ic.Queue.Contains(line.Id)) continue;
                if (line.Immediate)
                {
                    ic.Queue = new List<string>();
                    Play(w, line);
                    return;
                }
                ic.Queue.Add(line.Id);
                changed = true;
            }
            if (changed)
            {
                var order = new Dictionary<string, int>(StringComparer.Ordinal);
                for (var i = 0; i < lines.Count; i++) order[lines[i].Id] = i;
                ic.Queue = Sequences.StableSorted(ic.Queue, (a, b) => order[a] - order[b]);
                if (!w.InChain) TryPlay(w);
            }
        }

        /// <summary>Called when a root action finishes: plays the first queued line if none has played this phase.</summary>
        public static void Flush(World w) => TryPlay(w);

        /// <summary>
        /// Damage that leaves a unit alive below a line's threshold triggers it ("Warden, report."). Each line is raised by its
        /// own threshold, so one hit across two thresholds raises both and a hit that crosses only the higher one does not raise
        /// the lower.
        /// </summary>
        public static void HpCheck(World w, Unit u, int before)
        {
            foreach (var line in w.C.Intercom.Lines)
            {
                var t = line.Trigger;
                if (t.Type == TriggerTypes.HpBelow && t.Def == u.Def && t.Threshold.HasValue && u.Hp > 0 && u.Hp < t.Threshold.Value && before >= t.Threshold.Value)
                {
                    Trigger(w, TriggerTypes.HpBelow, def: u.Def, threshold: t.Threshold);
                }
            }
        }

        /// <summary>A new phase may play one more line.</summary>
        public static void PhaseStart(World w)
        {
            w.S.Intercom.LineThisPhase = false;
            TryPlay(w);
        }

        private static void TryPlay(World w)
        {
            var ic = w.S.Intercom;
            if (ic.LineThisPhase || ic.Queue.Count == 0) return;
            var id = Sequences.Shift(ic.Queue);
            var line = w.C.Intercom.Lines.FirstOrDefault(l => l.Id == id);
            if (line != null) Play(w, line);
        }

        private static void Play(World w, IntercomLineDef line)
        {
            var ic = w.S.Intercom;
            ic.Played.Add(line.Id);
            ic.LineThisPhase = true;
            ic.Log.Add(new IntercomEntry { Round = w.S.Round, Phase = w.S.Phase, Line = line.Id, Text = line.Text });
            w.Emit(new IntercomLine { Line = line.Id, Text = line.Text });
        }

        private static bool Matches(IntercomLineDef line, string type, int? round, string? def, int? threshold)
        {
            var t = line.Trigger;
            if (t.Type != type) return false;
            if (t.Round.HasValue && t.Round != round) return false;
            if (t.Def != null && t.Def != def) return false;
            // An hpBelow line is raised by the threshold it names, so a second line on the same unit waits for its own.
            if (type == TriggerTypes.HpBelow && t.Threshold != threshold) return false;
            return true;
        }
    }
}
