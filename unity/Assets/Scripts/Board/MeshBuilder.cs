#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace LabEscape.Client
{
    /// <summary>Collects vertices into a Mesh. Greybox models and every overlay are built this way, so the project needs no binary assets.</summary>
    public sealed class MeshBuilder
    {
        private readonly List<Vector3> _v = new List<Vector3>();
        private readonly List<Vector3> _n = new List<Vector3>();
        private readonly List<Color32> _c = new List<Color32>();
        private readonly List<Vector2> _uv = new List<Vector2>();
        private readonly List<int> _t = new List<int>();

        public void Clear()
        {
            _v.Clear();
            _n.Clear();
            _c.Clear();
            _uv.Clear();
            _t.Clear();
        }

        public int Vert(Vector3 p, Vector3 n, Color32 c, float pulse = 0f)
        {
            _v.Add(p);
            _n.Add(n);
            _c.Add(c);
            _uv.Add(new Vector2(pulse, 0f));
            return _v.Count - 1;
        }

        public void Tri(int a, int b, int c)
        {
            _t.Add(a);
            _t.Add(b);
            _t.Add(c);
        }

        /// <summary>A quad wound clockwise as seen from its front.</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 col, float pulse = 0f)
        {
            var n = Vector3.Cross(b - a, d - a).normalized;
            var i = Vert(a, n, col, pulse);
            Vert(b, n, col, pulse);
            Vert(c, n, col, pulse);
            Vert(d, n, col, pulse);
            Tri(i, i + 1, i + 2);
            Tri(i, i + 2, i + 3);
        }

        public void Box(Vector3 min, Vector3 max, Color32 col)
        {
            float x0 = min.x, y0 = min.y, z0 = min.z, x1 = max.x, y1 = max.y, z1 = max.z;
            Quad(new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), col);
            Quad(new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x0, y0, z1), col);
            Quad(new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y0, z0), col);
            Quad(new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), new Vector3(x0, y0, z1), col);
            Quad(new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x1, y0, z1), col);
            Quad(new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x0, y1, z0), new Vector3(x0, y0, z0), col);
        }

        /// <summary>A flat strip from a to b, half its width to each side along `side`.</summary>
        public void Strip(Vector3 a, Vector3 b, float width, Vector3 side, Color32 col, float pulse = 0f)
        {
            var s = side * (width * 0.5f);
            Quad(a - s, b - s, b + s, a + s, col, pulse);
        }

        /// <summary>A disc in the plane of `right` and `up`, shaded from its centre to its edge.</summary>
        public void Disc(Vector3 centre, float r, Vector3 right, Vector3 up, Color32 inner, Color32 outer, int sides = 16)
        {
            var c = Vert(centre, Vector3.up, inner);
            var first = _v.Count;
            for (var i = 0; i < sides; i++)
            {
                var a = i * Mathf.PI * 2f / sides;
                Vert(centre + right * (Mathf.Cos(a) * r) + up * (Mathf.Sin(a) * r), Vector3.up, outer);
            }
            for (var i = 0; i < sides; i++) Tri(c, first + i, first + (i + 1) % sides);
        }

        /// <summary>A ring in the plane of `right` and `up`, between radii r0 and r1.</summary>
        public void Ring(Vector3 centre, float r0, float r1, Vector3 right, Vector3 up, Color32 col, int sides = 28, float pulse = 0f)
        {
            var first = _v.Count;
            for (var i = 0; i < sides; i++)
            {
                var a = i * Mathf.PI * 2f / sides;
                var dir = right * Mathf.Cos(a) + up * Mathf.Sin(a);
                Vert(centre + dir * r0, Vector3.up, col, pulse);
                Vert(centre + dir * r1, Vector3.up, col, pulse);
            }
            for (var i = 0; i < sides; i++)
            {
                int a = first + i * 2, b = first + i * 2 + 1, c = first + (i + 1) % sides * 2, d = c + 1;
                Tri(a, b, d);
                Tri(a, d, c);
            }
        }

        /// <summary>Copies what has been collected into a mesh, replacing its contents.</summary>
        public void Apply(Mesh mesh)
        {
            mesh.Clear();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(_v);
            mesh.SetNormals(_n);
            mesh.SetColors(_c);
            mesh.SetUVs(0, _uv);
            mesh.SetTriangles(_t, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 2000f);
        }
    }

    /// <summary>A mesh rebuilt as often as its contents change, for highlights, lines and effects.</summary>
    public sealed class OverlayLayer
    {
        public readonly MeshBuilder B = new MeshBuilder();
        private readonly Mesh _mesh = new Mesh();

        public OverlayLayer(string name, Material material, Transform parent)
        {
            _mesh.MarkDynamic();
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = _mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        public void Begin() => B.Clear();

        public void End() => B.Apply(_mesh);
    }

    /// <summary>The unit meshes models are assembled from: each stands on its origin, one tile wide and high.</summary>
    public static class Shapes
    {
        private static Mesh? _box;
        private static Mesh? _cyl;
        private static Mesh? _oct;
        private static Mesh? _hex;
        private static Mesh? _cone;
        private static Mesh? _ball;

        public static Mesh Box => _box ??= BuildBox();
        public static Mesh Cylinder => _cyl ??= Prism(14, 1f);
        public static Mesh Octagon => _oct ??= Prism(8, 1f);
        public static Mesh Hexagon => _hex ??= Prism(6, 1f);
        public static Mesh Cone => _cone ??= Prism(10, 0f);
        public static Mesh Ball => _ball ??= BuildBall(12, 8);

        private static Mesh Finish(MeshBuilder b, string name)
        {
            var m = new Mesh { name = name };
            b.Apply(m);
            m.RecalculateBounds();
            return m;
        }

        private static Mesh BuildBox()
        {
            var b = new MeshBuilder();
            b.Box(new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 1f, 0.5f), Color.white);
            return Finish(b, "box");
        }

        /// <summary>A prism with flat sides; a top radius of 0 makes it a cone.</summary>
        private static Mesh Prism(int sides, float topScale)
        {
            var b = new MeshBuilder();
            Color32 white = Color.white;
            const float r = 0.5f;
            Vector3 At(int i, float y, float k)
            {
                var a = i * Mathf.PI * 2f / sides;
                return new Vector3(Mathf.Cos(a) * r * k, y, Mathf.Sin(a) * r * k);
            }
            for (var i = 0; i < sides; i++)
            {
                b.Quad(At(i, 0f, 1f), At(i, 1f, topScale), At(i + 1, 1f, topScale), At(i + 1, 0f, 1f), white);
                var bottom = b.Vert(Vector3.zero, Vector3.down, white);
                b.Vert(At(i, 0f, 1f), Vector3.down, white);
                b.Vert(At(i + 1, 0f, 1f), Vector3.down, white);
                b.Tri(bottom, bottom + 1, bottom + 2);
                if (topScale <= 0f) continue;
                var top = b.Vert(Vector3.up, Vector3.up, white);
                b.Vert(At(i + 1, 1f, topScale), Vector3.up, white);
                b.Vert(At(i, 1f, topScale), Vector3.up, white);
                b.Tri(top, top + 1, top + 2);
            }
            return Finish(b, $"prism{sides}");
        }

        private static Mesh BuildBall(int lon, int lat)
        {
            var b = new MeshBuilder();
            Color32 white = Color.white;
            Vector3 Dir(int i, int j)
            {
                var a = i * Mathf.PI * 2f / lon;
                var t = -Mathf.PI / 2f + j * Mathf.PI / lat;
                return new Vector3(Mathf.Cos(t) * Mathf.Cos(a), Mathf.Sin(t), Mathf.Cos(t) * Mathf.Sin(a));
            }
            var centre = new Vector3(0f, 0.5f, 0f);
            for (var j = 0; j < lat; j++)
            {
                for (var i = 0; i < lon; i++)
                {
                    var n0 = Dir(i, j);
                    var n1 = Dir(i, j + 1);
                    var n2 = Dir(i + 1, j + 1);
                    var n3 = Dir(i + 1, j);
                    var q = b.Vert(centre + n0 * 0.5f, n0, white);
                    b.Vert(centre + n1 * 0.5f, n1, white);
                    b.Vert(centre + n2 * 0.5f, n2, white);
                    b.Vert(centre + n3 * 0.5f, n3, white);
                    b.Tri(q, q + 1, q + 2);
                    b.Tri(q, q + 2, q + 3);
                }
            }
            return Finish(b, "ball");
        }
    }
}
