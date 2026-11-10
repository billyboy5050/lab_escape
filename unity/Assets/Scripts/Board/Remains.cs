#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace LabEscape.Client
{
    /// <summary>
    /// What a unit or object leaves on the board for a moment after it is gone: a body that is knocked off its feet and falls, a
    /// machine that breaks into its parts, a hatchling that bursts. It owns the model until it has faded, then destroys it.
    /// </summary>
    public sealed class Remains
    {
        public enum Style { Tumble, Splat, Wreck, Shrink, Gone }

        private struct Shard
        {
            public Transform T;
            public Vector3 Velocity;
            public Vector3 Axis;
            public float Spin;
            public int Bounces;
        }

        private readonly Model _model;
        private readonly Style _style;
        private readonly float _heft;
        private readonly Vector3 _root;
        private readonly Quaternion _stood;
        private readonly Vector3 _axis;
        private readonly List<Shard> _shards = new List<Shard>();
        private Vector3 _offset;
        private Vector3 _velocity;
        private float _angle;
        private float _spin;
        private int _bounces;
        private bool _resting;
        private float _rest;
        private float _age;
        private bool _landed;

        public bool Done { get; private set; }

        /// <summary>A landing since the board last looked, by weight.</summary>
        public float Thud;

        public Vector3 Position => _root + Feel.Flat(_offset);

        public Remains(Model model, Style style, Vector3 dir, float force, float heft, int seed)
        {
            _model = model;
            _style = style;
            _heft = heft;
            _root = model.Root.transform.position;
            _stood = model.Body.rotation;
            _offset = model.Body.position - _root;
            var give = Mathf.Sqrt(Feel.Give(heft));
            switch (style)
            {
                case Style.Tumble:
                    _axis = Vector3.Cross(Vector3.up, dir);
                    _velocity = dir * (Feel.DeathFling * force * give) + Vector3.up * (Feel.DeathPop * give * Mathf.Lerp(0.45f, 1f, force));
                    _spin = Feel.DeathSpin * Feel.Swing(heft) * Mathf.Lerp(0.55f, 1f, force);
                    break;
                case Style.Wreck:
                {
                    var parts = model.BodyParts;
                    var centre = model.Body.position + Vector3.up * 0.3f;
                    for (var i = 0; i < parts.Count; i++)
                    {
                        var t = parts[i];
                        t.SetParent(model.Root.transform, true);
                        var away = t.position - centre;
                        away.y = 0f;
                        var a = Feel.Noise(seed, i, 1) * Mathf.PI * 2f;
                        away = away.sqrMagnitude > 0.0004f ? away.normalized : new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                        var v = away * (0.8f + 1.5f * Feel.Noise(seed, i, 2)) + dir * (1.6f * force) + Vector3.up * (2.2f + 2.4f * Feel.Noise(seed, i, 3));
                        var axis = new Vector3(Feel.Noise(seed, i, 4) - 0.5f, Feel.Noise(seed, i, 5) - 0.5f, Feel.Noise(seed, i, 6) - 0.5f);
                        _shards.Add(new Shard { T = t, Velocity = v, Axis = axis.sqrMagnitude > 0.001f ? axis.normalized : Vector3.right, Spin = 300f + 500f * Feel.Noise(seed, i, 7) });
                    }
                    break;
                }
            }
        }

        public void Destroy() => _model.Destroy();

        public void Tick(float dt)
        {
            if (Done) return;
            _age += dt;
            _model.Plate.localScale = Vector3.one * Mathf.Clamp01(1f - _age / 0.16f);
            switch (_style)
            {
                case Style.Tumble:
                    Tumble(dt);
                    break;
                case Style.Wreck:
                    Scatter(dt);
                    break;
                case Style.Splat:
                {
                    var k = Mathf.Clamp01(_age / 0.1f);
                    _model.Body.localScale = new Vector3(1f + 0.8f * k, Mathf.Lerp(1f, 0.1f, k), 1f + 0.8f * k);
                    if (!_landed && k >= 1f)
                    {
                        _landed = true;
                        Thud = _heft * 0.8f;
                    }
                    Fade((_age - 0.3f) / 0.25f);
                    break;
                }
                case Style.Shrink:
                {
                    var k = Mathf.Clamp01(_age / 0.18f);
                    _model.Root.transform.localScale = Vector3.one * (1f - k);
                    if (k >= 1f) Finish();
                    break;
                }
                default:
                    Finish();
                    break;
            }
        }

        private void Tumble(float dt)
        {
            if (!_resting)
            {
                _velocity.y -= Feel.Gravity * dt;
                _offset += _velocity * dt;
                _angle = Mathf.Min(90f, _angle + _spin * dt);
                if (_offset.y <= 0f && _velocity.y < 0f)
                {
                    _offset.y = 0f;
                    Thud = _heft * (_bounces == 0 ? 1.7f : 0.6f);
                    if (_bounces == 0 && _velocity.y < -1.2f)
                    {
                        _velocity = new Vector3(_velocity.x * 0.4f, -_velocity.y * Feel.Bounce, _velocity.z * 0.4f);
                        _bounces++;
                    }
                    else _resting = true;
                }
            }
            else
            {
                _angle = Mathf.MoveTowards(_angle, 90f, 700f * dt);
                _rest += dt;
                var k = (_rest - Feel.BodyRest) / Feel.BodyFade;
                if (k > 0f) _offset.y = -0.12f * Mathf.Clamp01(k);
                Fade(k);
            }
            // Lying down, the body rests on its back instead of half under the floor.
            var lift = Vector3.up * (0.11f * Mathf.Sin(_angle * Mathf.Deg2Rad));
            _model.Body.position = _root + _offset + lift;
            _model.Body.rotation = Quaternion.AngleAxis(_angle, _axis) * _stood;
        }

        private void Scatter(float dt)
        {
            for (var i = 0; i < _shards.Count; i++)
            {
                var s = _shards[i];
                if (s.Bounces < 3)
                {
                    s.Velocity.y -= Feel.Gravity * dt;
                    var p = s.T.position + s.Velocity * dt;
                    s.T.Rotate(s.Axis, s.Spin * dt, Space.World);
                    if (p.y <= 0.01f && s.Velocity.y < 0f)
                    {
                        p.y = 0.01f;
                        s.Bounces = s.Velocity.y > -1.4f ? 3 : s.Bounces + 1;
                        s.Velocity = new Vector3(s.Velocity.x * 0.5f, -s.Velocity.y * 0.34f, s.Velocity.z * 0.5f);
                        s.Spin *= 0.4f;
                        if (!_landed)
                        {
                            _landed = true;
                            Thud = _heft * 0.9f;
                        }
                    }
                    s.T.position = p;
                    _shards[i] = s;
                }
            }
            Fade((_age - 0.6f) / 0.3f);
        }

        private void Fade(float k)
        {
            if (k <= 0f) return;
            _model.Paint(Mathf.Clamp01(1f - k));
            if (k >= 1f) Finish();
        }

        private void Finish()
        {
            Done = true;
            _model.Destroy();
        }
    }
}
