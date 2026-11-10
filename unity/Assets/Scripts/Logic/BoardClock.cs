#nullable enable
using System;

namespace LabEscape.Client
{
    /// <summary>
    /// The board's own clock. A heavy blow stops it for a few hundredths of a second, and the blow that ends the fight slows it,
    /// while the interface and the camera keep real time. It holds no Unity code.
    /// </summary>
    public sealed class BoardClock
    {
        private float _freeze;
        private float _slow = 1f;
        private float _slowFor;
        private float _slowLeft;

        public bool Frozen => _freeze > 0f;

        /// <summary>Stops the board for this long. Blows landing together share the longest stop instead of adding up.</summary>
        public void Freeze(float seconds)
        {
            if (seconds > _freeze) _freeze = seconds;
        }

        /// <summary>Runs the board at `factor` of its speed for this many real seconds, easing back over the last third.</summary>
        public void Slow(float factor, float seconds)
        {
            _slow = factor;
            _slowFor = _slowLeft = seconds;
        }

        public void Reset()
        {
            _freeze = 0f;
            _slowLeft = 0f;
        }

        /// <summary>Turns a step of real time into the board's time.</summary>
        public float Step(float dt)
        {
            if (_freeze > 0f)
            {
                var held = Math.Min(_freeze, dt);
                _freeze -= held;
                dt -= held;
            }
            if (_slowLeft <= 0f || dt <= 0f) return dt;
            var left = _slowLeft / _slowFor;
            var factor = left > 1f / 3f ? _slow : 1f + (_slow - 1f) * left * 3f;
            _slowLeft -= dt;
            return dt * factor;
        }
    }
}
