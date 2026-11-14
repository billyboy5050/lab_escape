#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace LabEscape.Client
{
    /// <summary>
    /// Builds a low-poly mesh from convex shapes. Every face is flat and carries its colour in its vertices, so one mesh holds a
    /// figure's many colours, and the game's Solid shader lights it as it lights the greybox. Positions are in tiles: a figure
    /// stands on its origin, faces +z, and its right hand is on +x.
    /// </summary>
    public sealed class Sculpt
    {
        private readonly MeshBuilder _b = new MeshBuilder();
        private readonly List<Vector3> _poly = new List<Vector3>(16);
        private int _faces;

        public bool IsEmpty => _faces == 0;

        /// <summary>A convex polygon, turned to face away from `inside`.</summary>
        public void Face(IReadOnlyList<Vector3> pts, Vector3 inside, Color colour)
        {
            _poly.Clear();
            foreach (var p in pts)
            {
                if (_poly.Count == 0 || (p - _poly[_poly.Count - 1]).sqrMagnitude > 1e-12f) _poly.Add(p);
            }
            while (_poly.Count > 1 && (_poly[0] - _poly[_poly.Count - 1]).sqrMagnitude <= 1e-12f) _poly.RemoveAt(_poly.Count - 1);
            if (_poly.Count < 3) return;
            // Newell's normal: for a fan of triangles in this order it is the side Unity draws.
            var n = Vector3.zero;
            var centroid = Vector3.zero;
            for (var i = 0; i < _poly.Count; i++)
            {
                n += Vector3.Cross(_poly[i], _poly[(i + 1) % _poly.Count]);
                centroid += _poly[i];
            }
            if (n.sqrMagnitude < 1e-14f) return;
            centroid /= _poly.Count;
            if (Vector3.Dot(n, centroid - inside) < 0f)
            {
                _poly.Reverse();
                n = -n;
            }
            n.Normalize();
            Color32 c = colour;
            var first = _b.Vert(_poly[0], n, c);
            for (var i = 1; i < _poly.Count; i++) _b.Vert(_poly[i], n, c);
            for (var i = 1; i < _poly.Count - 1; i++) _b.Tri(first, first + i, first + i + 1);
            _faces++;
        }

        private void Face(Vector3 inside, Color colour, params Vector3[] pts) => Face(pts, inside, colour);

        /// <summary>A box with its edges cut off by `bevel`, turned by `rot` about its centre.</summary>
        public void Box(Vector3 centre, Vector3 size, Color colour, float bevel = 0f, Quaternion? rot = null)
        {
            var r = rot ?? Quaternion.identity;
            var h = size * 0.5f;
            var e = Mathf.Clamp(bevel, 0f, Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * 0.9f);
            Vector3 P(int axis, int sx, int sy, int sz) => centre + r * new Vector3(
                sx * (axis == 0 ? h.x : h.x - e),
                sy * (axis == 1 ? h.y : h.y - e),
                sz * (axis == 2 ? h.z : h.z - e));
            foreach (var s in new[] { -1, 1 })
            {
                Face(centre, colour, P(0, s, -1, -1), P(0, s, -1, 1), P(0, s, 1, 1), P(0, s, 1, -1));
                Face(centre, colour, P(1, -1, s, -1), P(1, 1, s, -1), P(1, 1, s, 1), P(1, -1, s, 1));
                Face(centre, colour, P(2, -1, -1, s), P(2, 1, -1, s), P(2, 1, 1, s), P(2, -1, 1, s));
            }
            if (e <= 0f) return;
            foreach (var a in new[] { -1, 1 })
            {
                foreach (var b in new[] { -1, 1 })
                {
                    Face(centre, colour, P(0, a, b, -1), P(0, a, b, 1), P(1, a, b, 1), P(1, a, b, -1));
                    Face(centre, colour, P(1, -1, a, b), P(1, 1, a, b), P(2, 1, a, b), P(2, -1, a, b));
                    Face(centre, colour, P(0, a, -1, b), P(0, a, 1, b), P(2, a, 1, b), P(2, a, -1, b));
                    foreach (var c in new[] { -1, 1 }) Face(centre, colour, P(0, a, b, c), P(1, a, b, c), P(2, a, b, c));
                }
            }
        }

        /// <summary>A box whose top is `top` times the size of its bottom across x and z, standing on `foot`: a helmet's crown, a boot.</summary>
        public void Taper(Vector3 foot, Vector3 size, Vector2 top, Color colour, Quaternion? rot = null)
        {
            var r = rot ?? Quaternion.identity;
            var h = new Vector3(size.x * 0.5f, size.y, size.z * 0.5f);
            Vector3 P(int sx, int y, int sz) => foot + r * new Vector3(sx * h.x * (y == 1 ? top.x : 1f), y * h.y, sz * h.z * (y == 1 ? top.y : 1f));
            var inside = foot + r * new Vector3(0f, h.y * 0.5f, 0f);
            Face(inside, colour, P(-1, 0, -1), P(1, 0, -1), P(1, 0, 1), P(-1, 0, 1));
            Face(inside, colour, P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1));
            Face(inside, colour, P(-1, 0, -1), P(1, 0, -1), P(1, 1, -1), P(-1, 1, -1));
            Face(inside, colour, P(-1, 0, 1), P(1, 0, 1), P(1, 1, 1), P(-1, 1, 1));
            Face(inside, colour, P(-1, 0, -1), P(-1, 0, 1), P(-1, 1, 1), P(-1, 1, -1));
            Face(inside, colour, P(1, 0, -1), P(1, 0, 1), P(1, 1, 1), P(1, 1, -1));
        }

        /// <summary>A prism from a to b, radius r0 at a and r1 at b (0 makes a cone), `flat` squashing it across.</summary>
        public void Rod(Vector3 a, Vector3 b, float r0, float r1, Color colour, int sides = 8, float flat = 1f, bool caps = true)
        {
            var axis = b - a;
            var len = axis.magnitude;
            if (len < 1e-6f) return;
            var up = axis / len;
            var right = Vector3.Cross(up, Mathf.Abs(up.y) < 0.9f ? Vector3.up : Vector3.forward).normalized;
            var fwd = Vector3.Cross(right, up);
            var ring0 = new Vector3[sides];
            var ring1 = new Vector3[sides];
            for (var i = 0; i < sides; i++)
            {
                var t = (i + 0.5f) * Mathf.PI * 2f / sides;
                var d = right * Mathf.Cos(t) + fwd * (Mathf.Sin(t) * flat);
                ring0[i] = a + d * r0;
                ring1[i] = b + d * r1;
            }
            var inside = (a + b) * 0.5f;
            for (var i = 0; i < sides; i++)
            {
                var j = (i + 1) % sides;
                Face(inside, colour, ring0[i], ring0[j], ring1[j], ring1[i]);
            }
            if (!caps) return;
            if (r0 > 0f) Face(ring0, inside, colour);
            if (r1 > 0f) Face(ring1, inside, colour);
        }

        /// <summary>A disc-like cylinder centred on `centre`, its axis along `axis`: a wheel, a rotor, a lens.</summary>
        public void Disc(Vector3 centre, Vector3 axis, float radius, float thickness, Color colour, int sides = 10)
        {
            var half = axis.normalized * (thickness * 0.5f);
            Rod(centre - half, centre + half, radius, radius, colour, sides);
        }

        /// <summary>A faceted ellipsoid with radii along its own axes, turned by `rot`.</summary>
        public void Ball(Vector3 centre, Vector3 radii, Color colour, Quaternion? rot = null, int lon = 10, int lat = 6)
        {
            var r = rot ?? Quaternion.identity;
            Vector3 P(int i, int j)
            {
                var a = i * Mathf.PI * 2f / lon;
                var t = -Mathf.PI / 2f + j * Mathf.PI / lat;
                var d = new Vector3(Mathf.Cos(t) * Mathf.Cos(a) * radii.x, Mathf.Sin(t) * radii.y, Mathf.Cos(t) * Mathf.Sin(a) * radii.z);
                return centre + r * d;
            }
            for (var j = 0; j < lat; j++)
            {
                for (var i = 0; i < lon; i++) Face(centre, colour, P(i, j), P(i + 1, j), P(i + 1, j + 1), P(i, j + 1));
            }
        }

        public void Ball(Vector3 centre, float radius, Color colour, int lon = 8, int lat = 5) => Ball(centre, Vector3.one * radius, colour, null, lon, lat);

        /// <summary>An arm or a leg: a rod between two joints with a ball at each, so bends stay closed.</summary>
        public void Limb(Vector3 a, Vector3 b, float ra, float rb, Color colour)
        {
            Rod(a, b, ra, rb, colour, 7, 1f, false);
            Ball(a, ra, colour, 7, 4);
            Ball(b, rb, colour, 7, 4);
        }

        /// <summary>The mesh, moved so its bounds are centred on its origin, and where that centre was: a piece that flies off a
        /// wreck spins about its own middle.</summary>
        public Mesh Bake(string name, out Vector3 centre)
        {
            var m = new Mesh { name = name };
            _b.Apply(m);
            m.RecalculateBounds();
            centre = m.bounds.center;
            var v = m.vertices;
            for (var i = 0; i < v.Length; i++) v[i] -= centre;
            m.vertices = v;
            m.RecalculateBounds();
            return m;
        }
    }
}
