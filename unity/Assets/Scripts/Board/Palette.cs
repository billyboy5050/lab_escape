#nullable enable
using LabEscape.Core;
using UnityEngine;

namespace LabEscape.Client
{
    /// <summary>The web client's dark theme, so a unit or status is the same colour in both while they exist side by side.</summary>
    public static class Palette
    {
        public static Color Hex(uint rgb, float a = 1f) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, a);

        public static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        public static Color Shade(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);

        public static Color Lighten(Color c, float k) => Color.Lerp(c, Color.white, k);

        public static readonly Color Background = Hex(0x070a0c);
        public static readonly Color FloorA = Hex(0x2c353a);
        public static readonly Color FloorB = Hex(0x262e33);
        public static readonly Color FloorEdge = Hex(0x13181b);
        public static readonly Color Frame = Hex(0x0d1113);
        public static readonly Color FrameHi = Hex(0x253035);
        public static readonly Color WallTop = Hex(0x4b575e);
        public static readonly Color WallFace = Hex(0x262f34);
        public static readonly Color PillarTop = Hex(0x59656c);
        public static readonly Color PillarFace = Hex(0x323c42);
        public static readonly Color Paint = Hex(0xf2b01e);
        public static readonly Color Copper = Hex(0xc87a3a);
        public static readonly Color Steel = Hex(0x1a2024);
        public static readonly Color Accent = Hex(0x22c3a6);
        public static readonly Color Ink = Hex(0xe1eae6);

        public static readonly Color Player = Hex(0x3d9be0);
        public static readonly Color Minion = Hex(0x56b4e9);
        public static readonly Color Hatch = Hex(0x1fc28a);
        public static readonly Color Enemy = Hex(0xff6b1f);
        public static readonly Color Elite = Hex(0xb8321e);
        public static readonly Color Medic = Hex(0xe38fc4);
        public static readonly Color Flamer = Hex(0xf0a020);
        public static readonly Color Hazard = Hex(0xffb020);
        public static readonly Color Poison = Hex(0x22c98f);
        public static readonly Color Parasite = Hex(0xe38fc4);
        public static readonly Color Corrode = Hex(0xd8c21c);
        public static readonly Color Danger = Hex(0xff4d3d);
        public static readonly Color Good = Hex(0x36d07a);
        public static readonly Color Target = Hex(0xf0e442);
        public static readonly Color Move = Hex(0x3fe0c5);

        public static Color UnitColor(string def, Team team)
        {
            switch (def)
            {
                case "player": return Player;
                case "turret":
                case "drone": return Minion;
                case "hatchling":
                case "spitter":
                case "burster": return Hatch;
                case "guard": return Enemy;
                case "medic": return Medic;
                case "flamer": return Flamer;
                case "warden": return Elite;
                default: return team == Team.Enemy ? Enemy : Minion;
            }
        }

        public static Color StatusColor(StatusId id)
        {
            switch (id)
            {
                case StatusId.Poison: return Poison;
                case StatusId.Parasite: return Parasite;
                case StatusId.Corrode: return Corrode;
                default: return Ink;
            }
        }
    }

    /// <summary>The materials every greybox object shares; colour and fading travel per object in a property block.</summary>
    public static class Mats
    {
        public static readonly int ColorId = Shader.PropertyToID("_Color");
        public static readonly int FlashId = Shader.PropertyToID("_Flash");

        private static Material? _solid;
        private static Material? _fade;
        private static Material? _floor;
        private static Material? _ground;
        private static Material? _top;
        private static Material? _fx;

        private static Shader Find(string name)
        {
            var s = Shader.Find(name);
            if (s == null) throw new System.InvalidOperationException($"Shader {name} is missing from the build");
            return s;
        }

        public static Material Solid => _solid ??= new Material(Find("LabEscape/Solid")) { renderQueue = 2000 };

        /// <summary>The same look, drawn after everything opaque: faded walls and preview ghosts, which units must show through.</summary>
        public static Material SolidFade => _fade ??= new Material(Find("LabEscape/Solid")) { renderQueue = 3000 };

        /// <summary>What the room stands on, and the shade at the foot of its walls: drawn before anything else on the floor.</summary>
        public static Material Ground => _ground ??= Overlay(2850, UnityEngine.Rendering.CompareFunction.LessEqual, UnityEngine.Rendering.BlendMode.SrcAlpha);

        /// <summary>On the floor: hidden by anything standing in front of it. Drawn before faded walls, so they tint it.</summary>
        public static Material FloorOverlay => _floor ??= Overlay(2900, UnityEngine.Rendering.CompareFunction.LessEqual, UnityEngine.Rendering.BlendMode.SrcAlpha);

        /// <summary>Above everything: the lines of an action being previewed.</summary>
        public static Material TopOverlay => _top ??= Overlay(3100, UnityEngine.Rendering.CompareFunction.Always, UnityEngine.Rendering.BlendMode.SrcAlpha);

        /// <summary>Combat effects, with premultiplied alpha: a vertex with colour and no alpha adds light.</summary>
        public static Material Fx => _fx ??= Overlay(3200, UnityEngine.Rendering.CompareFunction.Always, UnityEngine.Rendering.BlendMode.One);

        private static Material Overlay(int queue, UnityEngine.Rendering.CompareFunction ztest, UnityEngine.Rendering.BlendMode src)
        {
            var m = new Material(Find("LabEscape/Overlay")) { renderQueue = queue };
            m.SetInt("_ZTest", (int)ztest);
            m.SetInt("_SrcBlend", (int)src);
            return m;
        }
    }
}
