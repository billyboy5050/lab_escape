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
        private float _wait;

        public readonly Content C;
        public readonly FightSession Session;
        public GameState Shown;
        public IReadOnlyList<GameEvent> FrameEvents = Array.Empty<GameEvent>();
        public string? Banner;
        public bool Busy;
        public FightSpeed Speed = FightSpeed.Normal;
        public readonly List<LogLine> Log = new List<LogLine>();

        /// <summary>A frame has reached the screen: the board moves to its state and plays its events.</summary>
        public event Action<GameState, IReadOnlyList<GameEvent>>? FrameShown;

        /// <summary>The state on screen jumped without animation: undo, or the end of a skip.</summary>
        public event Action<GameState>? Snapped;

        public FightFlow(Content c, Loadout loadout, FightSettings settings, RecordMeta? meta = null)
        {
            C = c;
            Session = new FightSession(c, loadout, settings, meta);
            Shown = Session.State;
            _fmt = new EventFormatter(c, Session.State);
        }

        /// <summary>Plays the opening: wave 1 arrives and the briefing line. Separate from the constructor so listeners can attach first.</summary>
        public void Begin() => Play(Session.StartFrames);

        public GameState Live => Session.State;

        public bool Idle => !Busy && Live.Phase == Phase.Player && Live.Outcome == null;

        public void Tick(float dt)
        {
            if (!_waiting) return;
            _wait -= dt;
            if (_wait <= 0) Pump();
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

        /// <summary>How long a frame's animation holds the queue, in seconds.</summary>
        public float FrameSeconds(IReadOnlyList<GameEvent> events)
        {
            var b = (float)C.Rules.Ui.SecondsPerActor;
            var visual = events.Any(e => e is UnitMoved || e is DamageDealt || e is AreaEffect || e is ProjectileFired || e is UnitSpawned || e is HazardFired);
            var d = visual ? b : events.Any(e => e is PhaseStarted) ? b * 0.8f : b * 0.25f;
            return d / (Speed == FightSpeed.Fast ? 2f : 1f);
        }

        private void Play(List<Frame> frames)
        {
            if (frames.Count == 0)
            {
                Finish();
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
                Finish();
                return;
            }
            var f = _queue.Dequeue();
            ShowFrame(f);
            _wait = FrameSeconds(f.Events);
            _waiting = true;
        }

        private void ShowFrame(Frame f)
        {
            Shown = f.State;
            FrameEvents = f.Events;
            // A frame can hold several phase starts (an empty minion phase, then the enemy phase): show the last.
            var ph = f.Events.OfType<PhaseStarted>().LastOrDefault(e => e.Phase != Phase.RoundStart);
            if (ph != null) Banner = BannerFor(ph.Phase);
            AppendLog(f.Events, f.State);
            FrameShown?.Invoke(f.State, f.Events);
        }

        private void Flush()
        {
            _waiting = false;
            IReadOnlyList<GameEvent> last = Array.Empty<GameEvent>();
            while (_queue.Count > 0)
            {
                var f = _queue.Dequeue();
                AppendLog(f.Events, f.State);
                last = f.Events;
            }
            FrameEvents = last;
            Finish();
        }

        private void Finish()
        {
            _waiting = false;
            // The animation is over and the player can act: this is when a new turn's clock starts.
            Session.TurnReady();
            Shown = Session.State;
            Busy = false;
            Banner = null;
            Snapped?.Invoke(Shown);
        }

        private void AppendLog(IEnumerable<GameEvent> events, GameState s)
        {
            foreach (var e in events)
            {
                var line = _fmt.Format(e, s);
                if (line != null) Log.Add(line);
            }
            if (Log.Count > 600) Log.RemoveRange(0, Log.Count - 600);
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
