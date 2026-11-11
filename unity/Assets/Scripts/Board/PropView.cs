#nullable enable
using LabEscape.Core;
using UnityEngine;

namespace LabEscape.Client
{
    /// <summary>A mine, an egg, a shield segment or a corpse, and how it arrives and takes a blow.</summary>
    public sealed class PropView
    {
        private Spring _squash;
        private Spring _grow;
        private readonly Vector3 _at;
        private Vector3 _from;
        private float _toss = -1f;
        private float _tossFor;
        private float _appearIn;
        private float _fade = 1f;
        private float _flash;
        private float _arm = -1f;
        private float _armFor;
        private bool _rise;

        public readonly Model Model;
        public BoardObject Data;
        public int? ShownHp;
        public float Thud;

        public PropView(BoardObject o, Transform parent)
        {
            Model = Figures.Object(o, parent);
            Data = o;
            ShownHp = o.Hp;
            _at = Iso.Tile(o.Pos.X, o.Pos.Y);
            _grow.Rest(1f);
            Model.Root.transform.position = _at;
            Model.Paint(1f);
        }

        public Vector3 Position => Model.Root.transform.position;

        /// <summary>False while a corpse waits for its body to fall, so its tag waits too.</summary>
        public bool Arrived => _appearIn <= 0f && _toss < 0f;

        /// <summary>Puts the prop on its tile as it will be once it has arrived: whatever was bringing it there is over.</summary>
        public void Snap()
        {
            _toss = -1f;
            _appearIn = 0f;
            _fade = 1f;
            _arm = -1f;
            _flash = 0f;
            Thud = 0f;
            _squash.Rest();
            _grow.Rest(1f);
            Compose();
        }

        /// <summary>Thrown from a hand: it arcs to its tile and lands.</summary>
        public void Toss(Vector3 from, float seconds)
        {
            _from = from + Vector3.up * 0.4f;
            _toss = 0f;
            _tossFor = Mathf.Max(0.01f, seconds);
            Compose();
        }

        /// <summary>Grows up out of the floor.</summary>
        public void Rise()
        {
            _rise = true;
            _grow.Value = 0.02f;
            _grow.Velocity = 0f;
            _grow.Target = 1f;
            Compose();
        }

        public void Pop()
        {
            _grow.Value = 0.1f;
            _grow.Velocity = 0f;
            _grow.Target = 1f;
            Compose();
        }

        /// <summary>Stays unseen for a while, then fades in: a corpse, once the body that left it has fallen.</summary>
        public void Appear(float after)
        {
            _appearIn = after;
            _fade = 0f;
            Compose();
        }

        /// <summary>A mine that has been set off: it jumps and blinks until it bursts.</summary>
        public void Arm(float seconds)
        {
            _arm = 0f;
            _armFor = Mathf.Max(0.01f, seconds);
        }

        public void Hit()
        {
            _flash = 1f;
            _squash.Velocity -= 9f;
        }

        public void Tick(float dt)
        {
            if (_toss >= 0f)
            {
                _toss += dt;
                if (_toss >= _tossFor)
                {
                    _toss = -1f;
                    _squash.Value = -0.25f;
                    _squash.Velocity = -2.5f;
                    Thud = 0.45f;
                }
            }
            if (_appearIn > 0f) _appearIn -= dt;
            else if (_fade < 1f) _fade = Mathf.Min(1f, _fade + dt * 4f);
            if (_arm >= 0f) _arm += dt;
            if (_flash > 0f) _flash = Mathf.Max(0f, _flash - dt * 5f);
            _squash.Tick(dt, 6f, 0.4f);
            _grow.Tick(dt, 5.5f, 0.45f);
            Compose();
        }

        private void Compose()
        {
            var pos = _at;
            var size = Mathf.Max(0.001f, _grow.Value);
            if (_toss >= 0f)
            {
                var k = Mathf.Clamp01(_toss / _tossFor);
                pos = Vector3.Lerp(_from, _at, k) + Vector3.up * (0.9f * 4f * k * (1f - k));
                size = Mathf.Lerp(0.5f, 1f, k);
            }
            var flash = _flash;
            if (_arm >= 0f)
            {
                var k = Mathf.Clamp01(_arm / _armFor);
                pos += Vector3.up * (0.34f * Mathf.Sin(k * Mathf.PI * 0.5f));
                // Blinks faster as the fuse runs down.
                flash = Mathf.Max(flash, Mathf.Sin(k * k * 22f) > 0f ? 0.9f : 0f);
            }
            Model.Root.transform.position = pos;
            var tall = 1f + Mathf.Clamp(_squash.Value, -0.5f, 0.5f);
            var wide = 1f / Mathf.Sqrt(tall);
            Model.Body.localScale = _rise ? new Vector3(1f, tall * size, 1f) : new Vector3(wide * size, tall * size, wide * size);
            Model.Paint(_fade, flash * 0.9f);
        }

        /// <summary>Destroyed: it breaks into its pieces, thrown the way the blow went.</summary>
        public Remains Break(Vector3 dir, int seed)
        {
            return new Remains(Model, Remains.Style.Wreck, dir.sqrMagnitude < 0.0001f ? Vector3.zero : dir, 0.8f, 0.5f, seed);
        }

        /// <summary>Taken off the board without being destroyed.</summary>
        public Remains Leave(RemovalReason reason)
        {
            switch (reason)
            {
                case RemovalReason.Hatched: return new Remains(Model, Remains.Style.Wreck, Vector3.zero, 0.6f, 0.4f, Data.Id);
                case RemovalReason.Detonated: return new Remains(Model, Remains.Style.Gone, Vector3.zero, 0f, 0.4f, 0);
                default: return new Remains(Model, Remains.Style.Shrink, Vector3.zero, 0f, 0.4f, 0);
            }
        }
    }
}
