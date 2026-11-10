#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using LabEscape.Core;

namespace LabEscape.Client
{
    public enum FightSpeed { Skip = 0, Normal = 1, Fast = 2 }

    /// <summary>
    /// One fight as it plays out on screen: the live state in the session, the state on screen, and the queue of animation
    /// frames between them. It holds no Unity code, so tests can drive it by calling Tick with any time step.
    /// </summary>
    public sealed class FightFlow
    {
        private readonly Queue<Frame> _queue = new Queue<Frame>();
        private readonly EventFormatter _fmt;
        private bool _waiting;
        /// <summary>The state the frame on screen ends on, and how many of its cues have had their log line written.</summary>
        private GameState? _stageState;
        private int _logged;

        public readonly Content C;
        public readonly FightSession Session;
        public GameState Shown;
        public IReadOnlyList<GameEvent> FrameEvents = Array.Empty<GameEvent>();
        public string? Banner;
        public bool Busy;
        public FightSpeed Speed = FightSpeed.Normal;
        public readonly List<LogLine> Log = new List<LogLine>();

        /// <summary>The frame on screen, laid out in time. Empty when nothing is playing.</summary>
        public Stage Stage { get; private set; } = Stage.Empty;

        /// <summary>Seconds since the frame on screen arrived, on the clock Tick is given.</summary>
        public float FrameTime { get; private set; }

        /// <summary>A frame has reached the screen: the board moves towards its state, playing its cues as their moments come.</summary>
        public event Action<GameState, Stage>? FrameShown;

        /// <summary>The queue ran out: the state on screen is the live one, and whatever is still moving may finish.</summary>
        public event Action<GameState>? Settled;

        /// <summary>The state on screen jumped without animation: an undo, or a skip.</summary>
        public event Action<GameState>? Snapped;

        public FightFlow(Content c, Loadout loadout, FightSettings settings, RecordMeta? meta = null)
        {
            C = c;
            Session = new FightSession(c, loadout, settings, meta);
            // Until the opening has played, the screen shows the room as the fight finds it: the player and nobody else.
            Shown = Session.StartFrames.Count > 0 ? GameStates.CreateInitial(c, loadout, settings) : Session.State;
            _fmt = new EventFormatter(c, Session.State);
        }

        /// <summary>
        /// Plays the opening: wave 1 arrives and the briefing line. Separate from the constructor so listeners can attach first.
        /// `lead` holds the queue for that long first, for the fight's entrance.
        /// </summary>
        public void Begin(float lead = 0f)
        {
            if (lead > 0f && Speed != FightSpeed.Skip && Session.StartFrames.Count > 0)
            {
                foreach (var f in Session.StartFrames) _queue.Enqueue(f);
                Stage = new Stage(new List<Cue>(), lead);
                FrameTime = 0f;
                Busy = true;
                _waiting = true;
                return;
            }
            Play(Session.StartFrames);
        }

        public GameState Live => Session.State;

        public bool Idle => !Busy && Live.Phase == Phase.Player && Live.Outcome == null;

        /// <summary>How fast the fight's clock runs against real time: the 2x speed doubles everything on the board.</summary>
        public float TimeScale => Speed == FightSpeed.Fast ? 2f : 1f;

        /// <summary>Advances the frame on screen. A frame that has nothing to show passes in the same call.</summary>
        public void Tick(float dt)
        {
            if (!_waiting) return;
            FrameTime += dt;
            while (_waiting && FrameTime >= Stage.Hold)
            {
                var over = FrameTime - Stage.Hold;
                Pump();
                FrameTime = _waiting ? over : 0f;
            }
            if (_waiting) WriteLog(FrameTime);
        }

        /// <summary>
        /// Writes the log lines of the frame on screen whose moment has come. A line appears as its event is seen, so the log
        /// never says a shot hit before the shot has been fired.
        /// </summary>
        private void WriteLog(float upTo)
        {
            if (_stageState == null) return;
            var cues = Stage.Cues;
            while (_logged < cues.Count && cues[_logged].At <= upTo)
            {
                var cue = cues[_logged++];
                if (cue.Kind != CueKind.Event) continue;
                var line = _fmt.Format(cue.Event, _stageState);
                if (line != null) Log.Add(line);
            }
            if (Log.Count > 600) Log.RemoveRange(0, Log.Count - 600);
        }

        public StepResult Apply(Command cmd)
        {
            var r = Session.Apply(cmd);
            if (r.Ok) Play(r.Frames);
            return r;
        }

        public bool Undo()
        {
            if (Busy || !Session.Undo()) return false;
            Log.Add(_fmt.Line("Move undone", LogKind.Phase));
            Shown = Session.State;
            FrameEvents = Array.Empty<GameEvent>();
            Snapped?.Invoke(Shown);
            return true;
        }

        public void SetSpeed(FightSpeed s)
        {
            Speed = s;
            if (s == FightSpeed.Skip && _queue.Count > 0) Flush();
        }

        /// <summary>Shows everything still queued at once.</summary>
        public void Skip()
        {
            if (Busy) Flush();
        }

        private void Play(List<Frame> frames)
        {
            if (frames.Count == 0)
            {
                Finish(false);
                return;
            }
            foreach (var f in frames) _queue.Enqueue(f);
            if (Speed == FightSpeed.Skip)
            {
                Flush();
                return;
            }
            Busy = true;
            if (!_waiting) Pump();
        }

        private void Pump()
        {
            _waiting = false;
            if (_queue.Count == 0)
            {
                Finish(false);
                return;
            }
            WriteLog(float.MaxValue);
            var f = _queue.Dequeue();
            Stage = Staging.Plan(C, Shown, f.Events);
            FrameTime = 0f;
            _stageState = f.State;
            _logged = 0;
            ShowFrame(f);
            WriteLog(0f);
            _waiting = true;
        }

        private void ShowFrame(Frame f)
        {
            Shown = f.State;
            FrameEvents = f.Events;
            // A frame can hold several phase starts (an empty minion phase, then the enemy phase): show the last.
            var ph = f.Events.OfType<PhaseStarted>().LastOrDefault(e => e.Phase != Phase.RoundStart);
            if (ph != null) Banner = BannerFor(ph.Phase);
            FrameShown?.Invoke(f.State, Stage);
        }

        private void Flush()
        {
            _waiting = false;
            WriteLog(float.MaxValue);
            IReadOnlyList<GameEvent> last = Array.Empty<GameEvent>();
            // The frames nobody watched are logged in the order they would have been seen, so a skipped fight and a
            // watched one leave the same log.
            var before = Shown;
            while (_queue.Count > 0)
            {
                var f = _queue.Dequeue();
                Stage = Staging.Plan(C, before, f.Events);
                _stageState = f.State;
                _logged = 0;
                WriteLog(float.MaxValue);
                before = f.State;
                last = f.Events;
            }
            FrameEvents = last;
            Finish(true);
        }

        private void Finish(bool jumped)
        {
            _waiting = false;
            WriteLog(float.MaxValue);
            Stage = Stage.Empty;
            _stageState = null;
            _logged = 0;
            FrameTime = 0f;
            // The animation is over and the player can act: this is when a new turn's clock starts.
            Session.TurnReady();
            Shown = Session.State;
            Busy = false;
            Banner = null;
            if (jumped) Snapped?.Invoke(Shown);
            else Settled?.Invoke(Shown);
        }

        private static string? BannerFor(Phase p)
        {
            switch (p)
            {
                case Phase.Minion: return "Minion phase";
                case Phase.Enemy: return "Enemy phase";
                case Phase.Environment: return "Environment";
                case Phase.Player: return "Your turn";
                default: return null;
            }
        }
    }
}
