#nullable enable
using System.Collections.Generic;
using System.Linq;
using LabEscape.Core;

namespace LabEscape.Client
{
    /// <summary>
    /// What the player can do right now and what they have picked: the action statuses, move options and intents for the live
    /// state, the selected action, and which command a tile would issue. The engine decides what is legal; this only turns a
    /// tile into one of the options it offered.
    /// </summary>
    public sealed class ActionModel
    {
        private readonly Content _c;
        private GameState? _for;
        private bool _idleFor;

        public List<ActionStatus> Statuses = new List<ActionStatus>();
        public List<MoveOption> Moves = new List<MoveOption>();
        public IntentReport? Intents;

        public string? ModeId { get; private set; }

        /// <summary>Two-step actions (Barrier Shield, Lunge): the first tile chosen.</summary>
        public Pos? First { get; private set; }

        public string GrappleMode { get; private set; } = "unit";

        public ActionModel(Content c)
        {
            _c = c;
        }

        public void Refresh(GameState live, bool idle)
        {
            if (ReferenceEquals(_for, live) && _idleFor == idle) return;
            _for = live;
            _idleFor = idle;
            Statuses = Actions.Statuses(_c, live);
            Moves = idle ? Actions.MoveOptions(_c, live) : new List<MoveOption>();
            Intents = live.Phase == Phase.Player && live.Outcome == null ? Previews.ComputeIntents(_c, live) : null;
            // Drop a selection that is no longer usable (after AP is spent, or a new round).
            if (ModeId != null && Selected?.Usable != true) Clear();
        }

        public ActionStatus? Selected => ModeId == null ? null : Statuses.FirstOrDefault(a => a.Id == ModeId);

        public bool TwoStep => ModeId == "barrier_shield" || ModeId == "lunge";

        /// <summary>Barrier Shield pairs are unordered: either tile of a pair can be picked first.</summary>
        private bool Pairs => ModeId == "barrier_shield";

        public IReadOnlyList<TargetOption> Options
        {
            get
            {
                var st = Selected;
                if (st == null) return new List<TargetOption>();
                return st.Id == "grapple_hook" ? st.Options.Where(o => o.Mode == GrappleMode).ToList() : st.Options;
            }
        }

        public List<Pos> FirstTiles
        {
            get
            {
                var output = new List<Pos>();
                foreach (var o in Options)
                {
                    AddOnce(output, o.Target);
                    if (Pairs && o.Target2.HasValue) AddOnce(output, o.Target2.Value);
                }
                return output;
            }
        }

        public List<Pos> SecondTiles
        {
            get
            {
                var output = new List<Pos>();
                if (First == null) return output;
                var f = First.Value;
                foreach (var o in Options)
                {
                    if (!o.Target2.HasValue) continue;
                    if (o.Target == f) AddOnce(output, o.Target2.Value);
                    else if (Pairs && o.Target2.Value == f) AddOnce(output, o.Target);
                }
                return output;
            }
        }

        public void Clear()
        {
            ModeId = null;
            First = null;
        }

        public void ClearFirst() => First = null;

        /// <summary>Picks an action, or clears it when it was already picked. The caller has checked that it is usable.</summary>
        public void Select(ActionStatus a)
        {
            if (ModeId == a.Id)
            {
                Clear();
                return;
            }
            ModeId = a.Id;
            First = null;
            if (a.Id == "grapple_hook") GrappleMode = a.Options.Any(o => o.Mode == "unit") ? "unit" : "self";
        }

        public void SetGrappleMode(string mode) => GrappleMode = mode;

        public void ToggleGrappleMode() => GrappleMode = GrappleMode == "unit" ? "self" : "unit";

        /// <summary>The command a click on this tile would issue now, or null when it would issue none.</summary>
        public Command? CandidateFor(Pos tile)
        {
            var st = Selected;
            if (st == null)
            {
                var m = Moves.FirstOrDefault(x => x.Tile == tile);
                return m != null ? Actions.MoveCommandFor(m.Path) : null;
            }
            if (st.Id == "sprint") return null;
            var options = Options;
            TargetOption? o;
            if (!TwoStep)
            {
                o = options.FirstOrDefault(x => x.Target == tile);
                return o != null ? Actions.CommandFor(st, o) : null;
            }
            if (First == null)
            {
                o = st.Id == "lunge"
                    ? options.FirstOrDefault(x => x.Target == tile && !x.Target2.HasValue)
                    : options.FirstOrDefault(x => x.Target == tile || x.Target2 == tile);
                return o != null ? Actions.CommandFor(st, o) : null;
            }
            var f = First.Value;
            if (st.Id == "lunge" && tile == f)
            {
                o = options.FirstOrDefault(x => x.Target == tile && !x.Target2.HasValue);
                return o != null ? Actions.CommandFor(st, o) : null;
            }
            o = options.FirstOrDefault(x => x.Target2.HasValue && ((x.Target == f && x.Target2.Value == tile) || (st.Id == "barrier_shield" && x.Target2.Value == f && x.Target == tile)));
            return o != null ? Actions.CommandFor(st, o) : null;
        }

        /// <summary>
        /// Acts on a click: returns the command to commit, or null when the click only changed what is selected or did nothing.
        /// </summary>
        public Command? Click(Pos tile)
        {
            var st = Selected;
            if (st == null || !TwoStep) return CandidateFor(tile);
            if (First == null)
            {
                if (!FirstTiles.Contains(tile)) return null;
                var strikes = Options.Any(o => o.Target == tile && o.Target2.HasValue);
                if (st.Id == "lunge" && !strikes) return CandidateFor(tile);
                First = tile;
                return null;
            }
            var cmd = CandidateFor(tile);
            if (cmd == null) First = null;
            return cmd;
        }

        /// <summary>The line of help for the current selection.</summary>
        public string Hint(FightFlow flow)
        {
            if (flow.Busy && flow.StepMode) return "Step mode: N shows the next action, Shift+N runs to the next phase, Space skips.";
            if (!flow.Idle) return flow.Busy ? "Resolving… press Space to skip." : flow.Live.Outcome != null ? "The fight is over." : "";
            var st = Selected;
            if (st == null) return "Click a highlighted tile to move (free, undo with Z). Pick an action below or press 1–9. Hover a unit to see its intent. End the turn with E.";
            if (st.Id == "barrier_shield") return First != null ? "Barrier Shield: pick the second tile (adjacent to the first). Esc to go back." : "Barrier Shield: pick the first tile.";
            if (st.Id == "lunge") return First != null ? "Lunge: pick a unit to hit, or click the landing tile again to land without hitting." : "Lunge: pick a landing tile.";
            return $"{st.Name}: pick a highlighted target. Esc cancels.{(st.Id == "grapple_hook" ? " Tab switches pull self / pull unit." : "")}";
        }

        private static void AddOnce(List<Pos> list, Pos p)
        {
            if (!list.Contains(p)) list.Add(p);
        }
    }
}
