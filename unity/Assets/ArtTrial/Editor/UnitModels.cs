#nullable enable
using LabEscape.Client;
using LabEscape.Core;
using UnityEngine;

namespace LabEscape.ArtTrial.Editor
{
    /// <summary>
    /// Generated unit models: low-poly figures built in code, as the greybox's are, so they need no files and add nothing to the
    /// download. They return the game's own Model, with its base plate and a body, so UnitView could show them in place of
    /// Figures.Unit. People wear their unit's colour as armour over a dark suit, and a machine is several meshes, so it still
    /// breaks into pieces.
    /// </summary>
    public static class UnitModels
    {
        // Lighter than the room's steel, since the shader darkens faces near the floor and legs would sink into it.
        private static readonly Color Suit = Palette.Hex(0x4a565e);
        private static readonly Color SuitDark = Palette.Hex(0x2e383e);
        private static readonly Color Strap = Palette.Hex(0x1b2226);
        private static readonly Color Metal = Palette.Hex(0x6b7880);
        private static readonly Color Gun = Palette.Hex(0x20272b);
        private static readonly Color White = Palette.Hex(0xe8eeeb);
        private static readonly Color Glass = Palette.Hex(0x10171b);

        public static Model Build(string def, Team team, Transform parent)
        {
            var m = new Model(def, parent);
            var c = Palette.UnitColor(def, team);
            BasePlate(m, def, team, c);
            switch (def)
            {
                case "player": Intruder(m, c); break;
                case "guard": Guard(m, c); break;
                case "medic": Medic(m, c); break;
                case "flamer": Flamer(m, c); break;
                case "warden": Warden(m, c); break;
                case "turret": Turret(m, c); break;
                case "drone": Drone(m, c); break;
                case "hatchling": Hatchling(m, c); break;
                case "spitter": Spitter(m, c); break;
                case "burster": Burster(m, c); break;
                default: Guard(m, c); break;
            }
            m.Paint(1f);
            return m;
        }

        private static void Part(Model m, Sculpt s, string name, float scale = 1f)
        {
            if (!s.IsEmpty) m.Add(m.Body, s.Bake(name), Vector3.zero, Vector3.one * scale, Color.white);
        }

        /// <summary>As the greybox has it: round for yours, a diamond for the lab's soldiers, a hexagon for the Warden.</summary>
        private static void BasePlate(Model m, string def, Team team, Color colour)
        {
            var dark = Palette.Shade(colour, 0.28f);
            var k = Figures.IsSmall(def) ? 0.7f : 1f;
            if (def == "warden")
            {
                m.Add(m.Plate, Shapes.Hexagon, Vector3.zero, new Vector3(0.84f, 0.03f, 0.84f), colour);
                m.Add(m.Plate, Shapes.Hexagon, Vector3.zero, new Vector3(0.68f, 0.05f, 0.68f), dark);
            }
            else if (team == Team.Enemy)
            {
                m.Add(m.Plate, Shapes.Box, Vector3.zero, new Vector3(0.56f * k, 0.03f, 0.56f * k), colour);
                m.Add(m.Plate, Shapes.Box, Vector3.zero, new Vector3(0.42f * k, 0.05f, 0.42f * k), dark);
            }
            else
            {
                m.Add(m.Plate, Shapes.Cylinder, Vector3.zero, new Vector3(0.68f * k, 0.03f, 0.68f * k), colour);
                m.Add(m.Plate, Shapes.Cylinder, Vector3.zero, new Vector3(0.52f * k, 0.05f, 0.52f * k), dark);
            }
        }

        // ---- People ----

        /// <summary>A person's joints, in tiles, facing +z with the right hand on +x.</summary>
        private sealed class Pose
        {
            public Vector3 FootL = new Vector3(-0.07f, 0.05f, 0.04f), FootR = new Vector3(0.07f, 0.05f, -0.03f);
            public Vector3 KneeL = new Vector3(-0.068f, 0.17f, 0.055f), KneeR = new Vector3(0.072f, 0.17f, 0.01f);
            public Vector3 HipL = new Vector3(-0.06f, 0.29f, 0f), HipR = new Vector3(0.06f, 0.29f, 0f);
            public Vector3 ShoulderL = new Vector3(-0.145f, 0.52f, 0f), ShoulderR = new Vector3(0.145f, 0.52f, 0f);
            public Vector3 ElbowL = new Vector3(-0.175f, 0.42f, -0.01f), ElbowR = new Vector3(0.175f, 0.42f, -0.01f);
            public Vector3 HandL = new Vector3(-0.165f, 0.33f, 0.03f), HandR = new Vector3(0.165f, 0.33f, 0.03f);
            public Vector3 Head = new Vector3(0f, 0.635f, 0.005f);

            /// <summary>Both hands on a long gun held across the body at the ready.</summary>
            public Pose Rifle()
            {
                ElbowR = new Vector3(0.175f, 0.415f, 0.05f);
                HandR = new Vector3(0.07f, 0.43f, 0.14f);
                ElbowL = new Vector3(-0.07f, 0.41f, 0.1f);
                HandL = new Vector3(0.035f, 0.45f, 0.21f);
                return this;
            }

            /// <summary>A sidearm aimed with the right hand, the left at the side.</summary>
            public Pose Pistol()
            {
                ElbowR = new Vector3(0.175f, 0.45f, 0.1f);
                HandR = new Vector3(0.13f, 0.44f, 0.21f);
                return this;
            }
        }

        /// <summary>The body every person shares: suit, legs, belt, torso armour in `armour`, shoulder pads, gloves and boots.</summary>
        private static void Figure(Sculpt s, Pose p, Color armour, float thick = 1.12f)
        {
            float t = thick;
            s.Limb(p.HipL, p.KneeL, 0.05f * t, 0.043f * t, Suit);
            s.Limb(p.KneeL, p.FootL, 0.043f * t, 0.037f * t, Suit);
            s.Limb(p.HipR, p.KneeR, 0.05f * t, 0.043f * t, Suit);
            s.Limb(p.KneeR, p.FootR, 0.043f * t, 0.037f * t, Suit);
            s.Box(p.KneeL + new Vector3(0f, 0f, 0.032f * t), new Vector3(0.065f, 0.065f, 0.035f) * t, armour, 0.012f);
            s.Box(p.KneeR + new Vector3(0f, 0f, 0.032f * t), new Vector3(0.065f, 0.065f, 0.035f) * t, armour, 0.012f);
            Boot(s, p.FootL, t);
            Boot(s, p.FootR, t);

            s.Box(new Vector3(0f, 0.305f, 0f), new Vector3(0.19f * t, 0.09f, 0.13f * t), Suit, 0.025f);
            s.Box(new Vector3(0f, 0.345f, 0f), new Vector3(0.2f * t, 0.035f, 0.14f * t), Strap, 0.01f);
            s.Box(new Vector3(0.075f * t, 0.33f, 0.05f * t), new Vector3(0.05f, 0.05f, 0.04f), Strap, 0.008f);
            s.Box(new Vector3(0f, 0.39f, 0f), new Vector3(0.17f * t, 0.08f, 0.115f * t), Suit, 0.02f);
            // A vest wider at the shoulders than the waist, and a lighter plate on its chest.
            s.Taper(new Vector3(0f, 0.405f, 0f), new Vector3(0.21f * t, 0.15f, 0.155f * t), new Vector2(1.22f, 1.04f), armour);
            s.Box(new Vector3(0f, 0.49f, 0.077f * t), new Vector3(0.14f * t, 0.07f, 0.02f), Palette.Lighten(armour, 0.18f), 0.01f);
            s.Rod(new Vector3(0f, 0.55f, 0f), new Vector3(0f, 0.595f, 0.005f), 0.036f * t, 0.034f * t, Suit, 7);

            s.Limb(p.ShoulderL, p.ElbowL, 0.04f * t, 0.035f * t, Suit);
            s.Limb(p.ElbowL, p.HandL, 0.035f * t, 0.031f * t, Suit);
            s.Limb(p.ShoulderR, p.ElbowR, 0.04f * t, 0.035f * t, Suit);
            s.Limb(p.ElbowR, p.HandR, 0.035f * t, 0.031f * t, Suit);
            Pad(s, p.ShoulderL, -1f, armour, t);
            Pad(s, p.ShoulderR, 1f, armour, t);
            s.Ball(p.HandL, new Vector3(0.036f, 0.034f, 0.04f) * t, Strap);
            s.Ball(p.HandR, new Vector3(0.036f, 0.034f, 0.04f) * t, Strap);
        }

        private static void Boot(Sculpt s, Vector3 ankle, float t)
        {
            s.Taper(new Vector3(ankle.x, 0f, ankle.z + 0.02f), new Vector3(0.085f * t, 0.075f, 0.15f * t), new Vector2(0.85f, 0.65f), Strap);
        }

        private static void Pad(Sculpt s, Vector3 shoulder, float side, Color armour, float t)
        {
            s.Box(shoulder + new Vector3(side * 0.022f * t, 0.025f, 0f), new Vector3(0.1f, 0.06f, 0.12f) * t, armour, 0.022f,
                Quaternion.Euler(0f, 0f, -side * 22f));
        }

        /// <summary>A rounded helmet in `shell` over the head, with a dark visor and a lit slit in `glow`.</summary>
        private static void Helmet(Sculpt s, Vector3 head, Color shell, Color glow)
        {
            s.Ball(head + new Vector3(0f, 0.008f, -0.004f), new Vector3(0.1f, 0.094f, 0.106f), shell, null, 10, 6);
            s.Box(head + new Vector3(0f, -0.004f, 0.074f), new Vector3(0.15f, 0.052f, 0.06f), Glass, 0.016f);
            s.Box(head + new Vector3(0f, 0f, 0.104f), new Vector3(0.11f, 0.013f, 0.008f), glow);
            s.Box(head + new Vector3(0f, -0.062f, 0.055f), new Vector3(0.1f, 0.035f, 0.05f), SuitDark, 0.012f);
        }

        private static void Rifle(Sculpt s, Color stripe)
        {
            var x = 0.045f;
            s.Box(new Vector3(x, 0.452f, 0.02f), new Vector3(0.034f, 0.058f, 0.1f), Gun, 0.01f);
            s.Box(new Vector3(x, 0.466f, 0.15f), new Vector3(0.046f, 0.07f, 0.17f), Gun, 0.012f);
            s.Box(new Vector3(x, 0.468f, 0.17f), new Vector3(0.05f, 0.018f, 0.08f), stripe);
            s.Box(new Vector3(x, 0.415f, 0.18f), new Vector3(0.03f, 0.07f, 0.04f), Gun, 0.006f, Quaternion.Euler(-14f, 0f, 0f));
            s.Rod(new Vector3(x, 0.476f, 0.235f), new Vector3(x, 0.476f, 0.385f), 0.015f, 0.012f, Gun, 7);
            s.Box(new Vector3(x, 0.512f, 0.13f), new Vector3(0.022f, 0.026f, 0.055f), Metal, 0.005f);
        }

        private static void Intruder(Model m, Color c)
        {
            var p = new Pose().Pistol();
            p.FootL = new Vector3(-0.08f, 0.05f, 0.05f);
            p.KneeL = new Vector3(-0.078f, 0.17f, 0.07f);
            var s = new Sculpt();
            Figure(s, p, c);
            var h = p.Head;
            var hood = Palette.Shade(c, 0.5f);
            // A hood over a dark face, with lit goggles, a teal scarf at the neck and a short cape: a shape no soldier has.
            s.Ball(h + new Vector3(0f, 0.012f, -0.014f), new Vector3(0.102f, 0.104f, 0.112f), hood, null, 10, 6);
            s.Rod(h + new Vector3(0f, 0.07f, -0.04f), h + new Vector3(0f, 0.01f, -0.165f), 0.06f, 0f, hood, 7);
            s.Taper(new Vector3(0f, 0.25f, -0.1f), new Vector3(0.27f, 0.31f, 0.035f), new Vector2(0.8f, 1f), hood, Quaternion.Euler(-10f, 0f, 0f));
            s.Box(h + new Vector3(0f, -0.012f, 0.07f), new Vector3(0.11f, 0.09f, 0.05f), Glass, 0.02f);
            s.Box(h + new Vector3(-0.03f, 0.004f, 0.096f), new Vector3(0.038f, 0.026f, 0.012f), Palette.Move, 0.004f);
            s.Box(h + new Vector3(0.03f, 0.004f, 0.096f), new Vector3(0.038f, 0.026f, 0.012f), Palette.Move, 0.004f);
            s.Rod(new Vector3(0f, 0.53f, 0f), new Vector3(0f, 0.575f, 0.01f), 0.075f, 0.062f, Palette.Accent, 8);
            s.Box(new Vector3(-0.03f, 0.5f, 0.09f), new Vector3(0.04f, 0.09f, 0.015f), Palette.Accent, 0.006f, Quaternion.Euler(0f, 0f, 12f));
            Part(m, s, "intruder");

            var g = new Sculpt();
            var hand = p.HandR;
            g.Box(hand + new Vector3(0f, 0.024f, 0.055f), new Vector3(0.034f, 0.042f, 0.12f), Gun, 0.008f);
            g.Box(hand + new Vector3(0f, -0.004f, 0.012f), new Vector3(0.03f, 0.06f, 0.035f), Gun, 0.006f, Quaternion.Euler(-15f, 0f, 0f));
            g.Box(hand + new Vector3(0f, 0.045f, 0.06f), new Vector3(0.012f, 0.008f, 0.07f), Palette.Move);
            // A pouch on the hip.
            g.Box(new Vector3(-0.1f, 0.32f, 0.02f), new Vector3(0.045f, 0.07f, 0.08f), SuitDark, 0.012f);
            Part(m, g, "intruder gear");
        }

        private static void Guard(Model m, Color c)
        {
            var p = new Pose().Rifle();
            var s = new Sculpt();
            Figure(s, p, c);
            Helmet(s, p.Head, c, Palette.Lighten(Palette.Hazard, 0.3f));
            // A ridge along the crown.
            s.Box(p.Head + new Vector3(0f, 0.095f, -0.01f), new Vector3(0.026f, 0.03f, 0.13f), Palette.Shade(c, 0.6f), 0.008f);
            Part(m, s, "guard");
            var g = new Sculpt();
            Rifle(g, c);
            g.Box(new Vector3(0f, 0.44f, -0.105f), new Vector3(0.15f, 0.13f, 0.06f), SuitDark, 0.02f);
            Part(m, g, "guard rifle");
        }

        private static void Medic(Model m, Color c)
        {
            var p = new Pose().Pistol();
            // The left hand carries a case.
            p.ElbowL = new Vector3(-0.17f, 0.42f, -0.02f);
            p.HandL = new Vector3(-0.165f, 0.33f, 0.0f);
            var s = new Sculpt();
            Figure(s, p, c);
            Helmet(s, p.Head, White, Palette.Lighten(c, 0.5f));
            Cross(s, p.Head + new Vector3(0f, 0.1f, -0.01f), Vector3.up, 0.07f, c);
            Part(m, s, "medic");

            var g = new Sculpt();
            var hand = p.HandR;
            // An injector: a white body, a coloured vial and a needle.
            g.Box(hand + new Vector3(0f, 0.022f, 0.05f), new Vector3(0.036f, 0.04f, 0.1f), White, 0.01f);
            g.Rod(hand + new Vector3(0f, 0.052f, 0.02f), hand + new Vector3(0f, 0.052f, 0.08f), 0.014f, 0.014f, c, 7);
            g.Rod(hand + new Vector3(0f, 0.022f, 0.1f), hand + new Vector3(0f, 0.022f, 0.15f), 0.006f, 0.002f, Metal, 5);
            var bag = p.HandL + new Vector3(0f, -0.07f, 0f);
            g.Box(bag, new Vector3(0.06f, 0.09f, 0.12f), White, 0.015f);
            Cross(g, bag + new Vector3(-0.031f, 0f, 0f), Vector3.left, 0.05f, c);
            // A pack with a cross.
            g.Box(new Vector3(0f, 0.46f, -0.115f), new Vector3(0.16f, 0.17f, 0.08f), White, 0.025f);
            Cross(g, new Vector3(0f, 0.465f, -0.156f), Vector3.back, 0.08f, c);
            Part(m, g, "medic kit");
        }

        /// <summary>A flat plus on the plane facing `normal`.</summary>
        private static void Cross(Sculpt s, Vector3 centre, Vector3 normal, float size, Color colour)
        {
            var rot = Quaternion.FromToRotation(Vector3.forward, normal);
            s.Box(centre, new Vector3(size, size * 0.32f, 0.01f), colour, 0f, rot);
            s.Box(centre, new Vector3(size * 0.32f, size, 0.01f), colour, 0f, rot);
        }

        private static void Flamer(Model m, Color c)
        {
            var p = new Pose();
            p.ElbowR = new Vector3(0.16f, 0.4f, 0.04f);
            p.HandR = new Vector3(0.1f, 0.38f, 0.12f);
            p.ElbowL = new Vector3(-0.08f, 0.39f, 0.1f);
            p.HandL = new Vector3(0.05f, 0.39f, 0.22f);
            var s = new Sculpt();
            Figure(s, p, c, 1.2f);
            var h = p.Head;
            // A rounded mask with a snout, two filters and lit goggles.
            s.Ball(h + new Vector3(0f, 0.008f, -0.004f), new Vector3(0.1f, 0.095f, 0.104f), c, null, 10, 6);
            s.Box(h + new Vector3(0f, 0.01f, 0.072f), new Vector3(0.13f, 0.05f, 0.05f), Glass, 0.015f);
            s.Ball(h + new Vector3(-0.034f, 0.012f, 0.098f), new Vector3(0.024f, 0.022f, 0.012f), Palette.Hazard, null, 8, 4);
            s.Ball(h + new Vector3(0.034f, 0.012f, 0.098f), new Vector3(0.024f, 0.022f, 0.012f), Palette.Hazard, null, 8, 4);
            s.Rod(h + new Vector3(0f, -0.04f, 0.06f), h + new Vector3(0f, -0.06f, 0.125f), 0.042f, 0.03f, Strap, 8);
            s.Disc(h + new Vector3(-0.052f, -0.05f, 0.09f), new Vector3(-1f, 0f, 0.4f), 0.026f, 0.022f, Metal, 8);
            s.Disc(h + new Vector3(0.052f, -0.05f, 0.09f), new Vector3(1f, 0f, 0.4f), 0.026f, 0.022f, Metal, 8);
            Part(m, s, "flamer");

            var g = new Sculpt();
            // Two fuel tanks on the back, and a hose to the gun.
            foreach (var x in new[] { -0.048f, 0.048f })
            {
                g.Rod(new Vector3(x, 0.34f, -0.13f), new Vector3(x, 0.58f, -0.13f), 0.046f, 0.046f, Palette.Danger, 10);
                g.Ball(new Vector3(x, 0.58f, -0.13f), new Vector3(0.046f, 0.03f, 0.046f), Metal, null, 10, 4);
                g.Box(new Vector3(x, 0.47f, -0.13f), new Vector3(0.096f, 0.016f, 0.096f), Palette.Hazard);
            }
            g.Rod(new Vector3(0.06f, 0.345f, -0.12f), new Vector3(0.13f, 0.32f, -0.06f), 0.013f, 0.013f, Strap, 6);
            g.Rod(new Vector3(0.13f, 0.32f, -0.06f), new Vector3(0.12f, 0.35f, 0.07f), 0.013f, 0.013f, Strap, 6);
            g.Rod(new Vector3(0.12f, 0.35f, 0.07f), new Vector3(0.075f, 0.375f, 0.11f), 0.013f, 0.013f, Strap, 6);
            g.Box(new Vector3(0.075f, 0.385f, 0.16f), new Vector3(0.05f, 0.06f, 0.19f), Gun, 0.012f);
            g.Box(new Vector3(0.075f, 0.388f, 0.16f), new Vector3(0.054f, 0.02f, 0.07f), c);
            g.Rod(new Vector3(0.075f, 0.395f, 0.255f), new Vector3(0.075f, 0.395f, 0.35f), 0.019f, 0.027f, Metal, 8);
            g.Ball(new Vector3(0.075f, 0.395f, 0.368f), 0.02f, Palette.Hazard, 7, 4);
            Part(m, g, "flamer tanks");
        }

        private static void Warden(Model m, Color c)
        {
            // A wide frame for its bulk, and the whole figure drawn a fifth larger than a person.
            const float scale = 1.2f;
            var p = new Pose
            {
                FootL = new Vector3(-0.095f, 0.05f, 0.03f), FootR = new Vector3(0.095f, 0.05f, -0.02f),
                KneeL = new Vector3(-0.09f, 0.17f, 0.05f), KneeR = new Vector3(0.09f, 0.17f, 0.01f),
                HipL = new Vector3(-0.075f, 0.29f, 0f), HipR = new Vector3(0.075f, 0.29f, 0f),
                ShoulderL = new Vector3(-0.18f, 0.53f, 0f), ShoulderR = new Vector3(0.18f, 0.53f, 0f),
                ElbowL = new Vector3(-0.22f, 0.41f, 0.03f), HandL = new Vector3(-0.2f, 0.31f, 0.1f),
                ElbowR = new Vector3(0.22f, 0.41f, 0.03f), HandR = new Vector3(0.2f, 0.31f, 0.1f),
            };
            var s = new Sculpt();
            Figure(s, p, c, 1.35f);
            var h = p.Head + new Vector3(0f, -0.01f, 0.01f);
            // A squared helm with a hazard slit, sunk between huge pauldrons.
            s.Taper(h + new Vector3(0f, -0.07f, 0f), new Vector3(0.17f, 0.15f, 0.18f), new Vector2(0.82f, 0.85f), Palette.Shade(c, 0.75f));
            s.Box(h + new Vector3(0f, 0.005f, 0.089f), new Vector3(0.12f, 0.02f, 0.012f), Palette.Hazard);
            s.Box(h + new Vector3(0f, 0.085f, -0.005f), new Vector3(0.03f, 0.035f, 0.15f), Palette.Shade(c, 0.5f), 0.01f);
            foreach (var side in new[] { -1f, 1f })
            {
                var sh = side < 0 ? p.ShoulderL : p.ShoulderR;
                s.Box(sh + new Vector3(side * 0.05f, 0.05f, 0f), new Vector3(0.15f, 0.1f, 0.18f), c, 0.03f, Quaternion.Euler(0f, 0f, -side * 18f));
                s.Box(sh + new Vector3(side * 0.06f, 0.1f, 0f), new Vector3(0.11f, 0.02f, 0.15f), Palette.Shade(c, 0.6f), 0.008f, Quaternion.Euler(0f, 0f, -side * 18f));
            }
            Part(m, s, "warden", scale);

            var g = new Sculpt();
            // Gauntlets, and a reactor on the back that lights its purge.
            foreach (var hand in new[] { p.HandL, p.HandR })
            {
                g.Box(hand + new Vector3(0f, -0.01f, 0.01f), new Vector3(0.1f, 0.1f, 0.11f), Gun, 0.02f);
                g.Box(hand + new Vector3(0f, 0.025f, 0.01f), new Vector3(0.104f, 0.02f, 0.114f), Palette.Hazard);
            }
            g.Box(new Vector3(0f, 0.47f, -0.14f), new Vector3(0.22f, 0.2f, 0.1f), Gun, 0.025f);
            g.Disc(new Vector3(0f, 0.47f, -0.195f), Vector3.forward, 0.055f, 0.02f, Palette.Hazard, 10);
            g.Rod(new Vector3(-0.07f, 0.56f, -0.15f), new Vector3(-0.07f, 0.68f, -0.16f), 0.018f, 0.014f, Metal, 6);
            g.Rod(new Vector3(0.07f, 0.56f, -0.15f), new Vector3(0.07f, 0.68f, -0.16f), 0.018f, 0.014f, Metal, 6);
            Part(m, g, "warden gear", scale);
        }

        // ---- Machines ----

        private static void Turret(Model m, Color c)
        {
            var legs = new Sculpt();
            var hub = new Vector3(0f, 0.2f, 0f);
            for (var i = 0; i < 3; i++)
            {
                var a = (90f + i * 120f) * Mathf.Deg2Rad;
                var foot = new Vector3(Mathf.Cos(a) * 0.22f, 0.015f, -Mathf.Sin(a) * 0.22f + 0.02f);
                legs.Rod(hub, foot, 0.022f, 0.016f, Metal, 6);
                legs.Box(foot, new Vector3(0.05f, 0.03f, 0.05f), Gun, 0.008f);
            }
            legs.Rod(new Vector3(0f, 0.12f, 0f), new Vector3(0f, 0.27f, 0f), 0.045f, 0.04f, Gun, 8);
            legs.Box(new Vector3(-0.1f, 0.06f, -0.08f), new Vector3(0.09f, 0.08f, 0.07f), Palette.Shade(c, 0.55f), 0.01f);
            Part(m, legs, "turret legs", 1.15f);

            var head = new Sculpt();
            head.Box(new Vector3(0f, 0.32f, 0f), new Vector3(0.22f, 0.12f, 0.22f), c, 0.03f);
            head.Box(new Vector3(0f, 0.385f, -0.02f), new Vector3(0.15f, 0.03f, 0.15f), Palette.Shade(c, 0.6f), 0.01f);
            head.Box(new Vector3(0f, 0.34f, 0.108f), new Vector3(0.12f, 0.05f, 0.02f), Glass, 0.008f);
            head.Ball(new Vector3(0.035f, 0.345f, 0.12f), new Vector3(0.016f, 0.016f, 0.01f), Palette.Move, null, 7, 4);
            head.Box(new Vector3(-0.135f, 0.31f, 0f), new Vector3(0.05f, 0.09f, 0.12f), Gun, 0.01f);
            Part(m, head, "turret head", 1.15f);

            var guns = new Sculpt();
            foreach (var x in new[] { -0.04f, 0.04f })
            {
                guns.Rod(new Vector3(x, 0.3f, 0.1f), new Vector3(x, 0.3f, 0.34f), 0.018f, 0.016f, Gun, 7);
                guns.Rod(new Vector3(x, 0.3f, 0.3f), new Vector3(x, 0.3f, 0.35f), 0.024f, 0.024f, Metal, 7);
            }
            Part(m, guns, "turret guns", 1.15f);
        }

        private static void Drone(Model m, Color c)
        {
            var y = 0.36f;
            var core = new Sculpt();
            core.Ball(new Vector3(0f, y, 0f), new Vector3(0.11f, 0.065f, 0.12f), c, null, 10, 6);
            core.Box(new Vector3(0f, y + 0.055f, -0.01f), new Vector3(0.09f, 0.025f, 0.1f), Palette.Shade(c, 0.6f), 0.01f);
            core.Ball(new Vector3(0f, y - 0.03f, 0.075f), new Vector3(0.045f, 0.04f, 0.04f), Glass, null, 8, 5);
            core.Ball(new Vector3(0f, y - 0.03f, 0.105f), new Vector3(0.022f, 0.022f, 0.012f), Palette.Move, null, 8, 4);
            // Its zap prongs underneath.
            core.Rod(new Vector3(-0.025f, y - 0.05f, 0f), new Vector3(-0.025f, y - 0.11f, 0.01f), 0.008f, 0f, Palette.Move, 5);
            core.Rod(new Vector3(0.025f, y - 0.05f, 0f), new Vector3(0.025f, y - 0.11f, 0.01f), 0.008f, 0f, Palette.Move, 5);
            Part(m, core, "drone core", 1.2f);
            // Four arms with rotors, each its own piece.
            for (var i = 0; i < 4; i++)
            {
                var a = (45f + i * 90f) * Mathf.Deg2Rad;
                var tip = new Vector3(Mathf.Cos(a) * 0.19f, y + 0.02f, Mathf.Sin(a) * 0.19f);
                var r = new Sculpt();
                r.Rod(new Vector3(0f, y, 0f), tip, 0.014f, 0.012f, Gun, 6);
                r.Disc(tip, Vector3.up, 0.028f, 0.04f, Gun, 8);
                r.Disc(tip + new Vector3(0f, 0.025f, 0f), Vector3.up, 0.085f, 0.006f, Palette.WithAlpha(Palette.Lighten(Metal, 0.3f), 1f), 14);
                r.Disc(tip + new Vector3(0f, 0.03f, 0f), Vector3.up, 0.018f, 0.012f, c, 8);
                Part(m, r, $"drone rotor {i}", 1.2f);
            }
        }

        // ---- Hatchlings ----

        private static void Legs(Sculpt s, float y, float zFront, float zBack, float reach, Color colour, int pairs = 3, float r = 0.012f)
        {
            for (var i = 0; i < pairs; i++)
            {
                var z = Mathf.Lerp(zFront, zBack, pairs == 1 ? 0.5f : i / (float)(pairs - 1));
                foreach (var side in new[] { -1f, 1f })
                {
                    var hip = new Vector3(side * 0.05f, y, z);
                    var knee = new Vector3(side * (reach * 0.75f), y + 0.05f, z + (z - (zFront + zBack) * 0.5f) * 0.4f);
                    var foot = new Vector3(side * reach, 0.005f, z + (z - (zFront + zBack) * 0.5f) * 0.9f);
                    s.Limb(hip, knee, r, r * 0.85f, colour);
                    s.Rod(knee, foot, r * 0.85f, 0.003f, colour, 5);
                }
            }
        }

        private static void Hatchling(Model m, Color c)
        {
            c = Palette.Lighten(c, 0.12f);
            var dark = Palette.Shade(c, 0.45f);
            var s = new Sculpt();
            s.Ball(new Vector3(0f, 0.1f, -0.06f), new Vector3(0.1f, 0.075f, 0.12f), c, null, 10, 6);
            s.Ball(new Vector3(0f, 0.105f, 0.05f), new Vector3(0.07f, 0.06f, 0.07f), Palette.Shade(c, 0.8f), null, 9, 5);
            s.Ball(new Vector3(0f, 0.11f, 0.12f), new Vector3(0.055f, 0.045f, 0.055f), dark, null, 9, 5);
            s.Ball(new Vector3(-0.026f, 0.13f, 0.158f), 0.016f, Palette.Target, 6, 4);
            s.Ball(new Vector3(0.026f, 0.13f, 0.158f), 0.016f, Palette.Target, 6, 4);
            s.Rod(new Vector3(-0.025f, 0.095f, 0.16f), new Vector3(-0.008f, 0.085f, 0.22f), 0.012f, 0f, dark, 5);
            s.Rod(new Vector3(0.025f, 0.095f, 0.16f), new Vector3(0.008f, 0.085f, 0.22f), 0.012f, 0f, dark, 5);
            for (var i = 0; i < 3; i++)
            {
                var z = -0.01f - i * 0.05f;
                s.Rod(new Vector3(0f, 0.16f - i * 0.005f, z), new Vector3(0f, 0.23f - i * 0.02f, z - 0.04f), 0.018f, 0f, Palette.Lighten(c, 0.35f), 5);
            }
            Legs(s, 0.1f, 0.07f, -0.05f, 0.15f, dark);
            Part(m, s, "hatchling", 1.25f);
        }

        private static void Spitter(Model m, Color c)
        {
            c = Palette.Lighten(c, 0.12f);
            var dark = Palette.Shade(c, 0.45f);
            var acid = Palette.Lighten(Palette.Corrode, 0.15f);
            var s = new Sculpt();
            s.Ball(new Vector3(0f, 0.1f, -0.05f), new Vector3(0.1f, 0.07f, 0.12f), c, null, 10, 6);
            // A sac of acid on its back, and a raised neck with an open mouth.
            s.Ball(new Vector3(0f, 0.2f, -0.07f), new Vector3(0.085f, 0.075f, 0.09f), acid, null, 10, 6);
            s.Ball(new Vector3(0.03f, 0.25f, -0.05f), 0.02f, Palette.Lighten(acid, 0.4f), 6, 4);
            s.Limb(new Vector3(0f, 0.12f, 0.05f), new Vector3(0f, 0.2f, 0.12f), 0.04f, 0.035f, Palette.Shade(c, 0.8f));
            s.Ball(new Vector3(0f, 0.22f, 0.15f), new Vector3(0.05f, 0.045f, 0.06f), dark, null, 9, 5);
            s.Rod(new Vector3(0f, 0.215f, 0.17f), new Vector3(0f, 0.205f, 0.22f), 0.035f, 0.045f, acid, 8);
            s.Ball(new Vector3(-0.028f, 0.245f, 0.18f), 0.013f, Palette.Target, 6, 4);
            s.Ball(new Vector3(0.028f, 0.245f, 0.18f), 0.013f, Palette.Target, 6, 4);
            Legs(s, 0.1f, 0.05f, -0.09f, 0.15f, dark);
            Part(m, s, "spitter", 1.2f);
        }

        private static void Burster(Model m, Color c)
        {
            c = Palette.Lighten(c, 0.12f);
            var dark = Palette.Shade(c, 0.45f);
            var glow = Palette.Lighten(Palette.Corrode, 0.5f);
            var s = new Sculpt();
            s.Ball(new Vector3(0f, 0.17f, -0.01f), new Vector3(0.17f, 0.14f, 0.17f), c, null, 12, 7);
            foreach (var (x, y, z, r) in new[] { (0.11f, 0.25f, 0.07f, 0.05f), (-0.12f, 0.22f, 0.05f, 0.045f), (0.02f, 0.31f, -0.06f, 0.05f), (-0.07f, 0.15f, -0.14f, 0.04f), (0.13f, 0.14f, -0.08f, 0.04f), (-0.03f, 0.29f, 0.09f, 0.035f) })
            {
                s.Ball(new Vector3(x, y, z), r, glow, 7, 4);
            }
            s.Ball(new Vector3(0f, 0.12f, 0.16f), new Vector3(0.05f, 0.04f, 0.05f), dark, null, 9, 5);
            s.Ball(new Vector3(-0.022f, 0.14f, 0.2f), 0.012f, Palette.Target, 6, 4);
            s.Ball(new Vector3(0.022f, 0.14f, 0.2f), 0.012f, Palette.Target, 6, 4);
            Legs(s, 0.07f, 0.08f, -0.08f, 0.17f, dark, 2, 0.016f);
            Part(m, s, "burster", 1.15f);
        }
    }
}
