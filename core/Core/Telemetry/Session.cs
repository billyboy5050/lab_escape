#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace LabEscape.Core
{
    /// <summary>
    /// One fight as the client plays it: the current state, the command log for replay, every event so far, and undo for free
    /// movement. Undo works until the player spends AP or a move sets something off; undone moves are dropped from the log, so
    /// the log replays exactly what happened.
    /// </summary>
    public sealed class FightSession
    {
        private sealed class UndoEntry
        {
            public GameState State = null!;
            public int Commands;
            public int Events;
        }

        private readonly Content _c;
        private readonly Func<double> _now;
        private readonly bool _timed;
        private readonly List<UndoEntry> _undoStack = new List<UndoEntry>();
        private double _turnStart;
        /// <summary>True from the start of a player turn until TurnReady restarts the clock, once the client has finished animating.</summary>
        private bool _clockPending = true;

        public GameState State;
        public readonly FightRecord Record;
        public readonly List<GameEvent> Events;
        public readonly List<Frame> StartFrames;

        /// <summary>
        /// timed is false when the commands come from a recorded replay: meta then carries the original record's turn times, and
        /// playing the commands back adds no timings of its own. now gives milliseconds since the Unix epoch.
        /// </summary>
        public FightSession(Content c, Loadout loadout, FightSettings? settings = null, RecordMeta? meta = null, Func<double>? now = null, bool timed = true)
        {
            _c = c;
            _now = now ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            _timed = timed;
            settings ??= new FightSettings();
            var g = Engine.NewGame(c, loadout, settings, frames: true);
            State = g.State;
            Events = new List<GameEvent>(g.Events);
            StartFrames = g.Frames;
            var start = new RecordMeta { Created = Labels.IsoTime(_now()), TurnTimesMs = new List<double>() };
            Record = Records.NewRecord(c, loadout, settings, start.MergedWith(meta));
            _turnStart = _now();
        }

        /// <summary>Applies a command. Illegal commands leave everything unchanged and return Ok = false.</summary>
        public StepResult Apply(Command cmd, bool frames = true)
        {
            var before = State;
            var r = Engine.Step(_c, before, cmd, frames);
            if (!r.Ok) return r;
            // Free movement is undoable until AP is spent or something is set off: a move made after the turn is committed is not.
            if (cmd is MoveCommand && !r.Commits && !before.Committed) _undoStack.Add(new UndoEntry { State = before, Commands = Record.Commands.Count, Events = Events.Count });
            else _undoStack.Clear();
            // A turn ends when the player ends it, or when the fight does: the winning (or fatal) action closes the last turn.
            if (_timed && (cmd is EndTurnCommand || r.State.Outcome != null)) Record.Meta?.AddTurnTime(Math.Max(0, Stats.RoundHalfUp(_now() - _turnStart)));
            if (cmd is EndTurnCommand)
            {
                // Timed from here unless the client calls TurnReady, which is how it leaves out the enemy phase playing out.
                _turnStart = _now();
                _clockPending = true;
            }
            Record.Commands.Add(cmd);
            Events.AddRange(r.Events);
            State = r.State;
            return r;
        }

        /// <summary>
        /// The client calls this once the animation has finished and the player can act. It starts the clock for the turn that
        /// has just begun, so a turn time measures the player's own time. Later calls in the same turn do nothing. Callers that
        /// never call it (the simulator, tests) time a turn from the end of the previous one.
        /// </summary>
        public void TurnReady()
        {
            if (!_clockPending || State.Outcome != null || State.Phase != Phase.Player) return;
            _turnStart = _now();
            _clockPending = false;
        }

        public bool CanUndo => _undoStack.Count > 0 && State.Outcome == null;

        /// <summary>Undoes the last free move. Returns false when there is nothing to undo.</summary>
        public bool Undo()
        {
            if (_undoStack.Count == 0) return false;
            var u = _undoStack[_undoStack.Count - 1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            State = u.State;
            Record.Commands.RemoveRange(u.Commands, Record.Commands.Count - u.Commands);
            Events.RemoveRange(u.Events, Events.Count - u.Events);
            return true;
        }

        public bool Over => State.Outcome != null;

        public FightSummary Summary() => Summaries.Summarize(Events, State);
    }

    public static class Labels
    {
        /// <summary>The longest tester ID kept; it goes into folder names and report rows.</summary>
        public const int MaxTesterLength = 20;

        /// <summary>
        /// A tester ID made safe for a folder name and a column: letters, digits, "_" and "-" only, at most MaxTesterLength
        /// characters. Anything else becomes "_". Returns null for an empty value, meaning "no tester".
        /// </summary>
        public static string? CleanTester(string? raw)
        {
            if (raw == null) return null;
            var chars = JsText.Trim(raw).ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                var c = chars[i];
                var ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-';
                if (!ok) chars[i] = '_';
            }
            var t = new string(chars);
            if (t.Length > MaxTesterLength) t = t.Substring(0, MaxTesterLength);
            return t.Length == 0 ? null : t;
        }

        /// <summary>
        /// The folder name for one fight's telemetry: time, then tester and attempt when the fight has a tester, then preset and
        /// outcome, so a round's folders sort by time and group by tester at a glance.
        /// </summary>
        public static string TelemetryFolderName(double unixMs, RecordMeta? meta, string outcome)
        {
            var stamp = IsoTime(unixMs).Replace(':', '-').Replace('.', '-');
            var tester = CleanTester(meta?.Tester);
            var who = tester != null ? $"-{tester}{(meta?.Attempt != null ? $"-a{JsText.Number(meta.Attempt.Value)}" : "")}" : "";
            return $"{stamp}{who}-{meta?.Preset ?? "custom"}-{outcome}";
        }

        /// <summary>Date.prototype.toISOString, from milliseconds since the Unix epoch.</summary>
        public static string IsoTime(double unixMs)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Floor(unixMs)).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        }
    }
}
