#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace LabEscape.Client
{
    /// <summary>
    /// Reveals text a character at a time, as the lab's terminals print it (the web client's `useTypewriter`): several lines show
    /// in turn, each starting when the one before it has finished. It has no Unity code and no clock; the screen ticks it.
    /// </summary>
    public sealed class Typewriter
    {
        private readonly IReadOnlyList<string> _lines;
        private readonly float _perSecond;
        private readonly float _delay;
        private readonly int _total;
        private float _time;

        public Typewriter(IReadOnlyList<string> lines, float perSecond, float delay = 0f)
        {
            _lines = lines;
            _perSecond = Math.Max(1f, perSecond);
            _delay = Math.Max(0f, delay);
            _total = lines.Sum(l => l.Length);
        }

        /// <summary>How many characters, across all the lines, have been printed.</summary>
        public int Count => (int)Math.Max(0f, Math.Min(_total, (_time - _delay) * _perSecond));

        public bool Done => Count >= _total;

        public void Tick(float dt) => _time += dt;

        /// <summary>Prints everything at once.</summary>
        public void Finish() => _time = _delay + _total / _perSecond + 1f;

        /// <summary>Each line as far as it has been printed.</summary>
        public IReadOnlyList<string> Shown()
        {
            var left = Count;
            var shown = new List<string>(_lines.Count);
            foreach (var line in _lines)
            {
                var take = Math.Max(0, Math.Min(line.Length, left));
                left -= take;
                shown.Add(line.Substring(0, take));
            }
            return shown;
        }

        /// <summary>The line being printed now: the first that is not yet whole, or -1 once everything is printed.</summary>
        public int TypingLine()
        {
            if (Done) return -1;
            var left = Count;
            for (var i = 0; i < _lines.Count; i++)
            {
                if (left < _lines[i].Length) return i;
                left -= _lines[i].Length;
            }
            return -1;
        }
    }
}
