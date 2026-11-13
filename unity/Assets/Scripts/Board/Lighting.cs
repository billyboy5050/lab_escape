#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace LabEscape.Client
{
    /// <summary>
    /// The light that effects throw on the room. The greybox has no light objects: its one shader takes the room's brightness
    /// and up to four short-lived point lights from global values, which are set here.
    /// </summary>
    public static class Lighting
    {
        public const int MaxPoints = 4;

        private static readonly int PosId = Shader.PropertyToID("_LabLightPos");
        private static readonly int ColId = Shader.PropertyToID("_LabLightCol");
        private static readonly int AmbientId = Shader.PropertyToID("_LabAmbient");
        private static readonly int RoomId = Shader.PropertyToID("_LabRoom");
        private static readonly Vector4[] Pos = new Vector4[MaxPoints];
        private static readonly Vector4[] Col = new Vector4[MaxPoints];

        /// <summary>A global value nobody has set reads as zero, which would draw the room black: call this before anything is drawn.</summary>
        public static void Reset()
        {
            SetAmbient(Color.white);
            SetPoints(null);
            SetRoom(Vector3.zero, 0f);
        }

        /// <summary>
        /// Where the room's light pools: brightest in the middle of the floor, falling off toward the walls as under one lamp.
        /// A reach of 0 turns it off.
        /// </summary>
        public static void SetRoom(Vector3 centre, float reach) => Shader.SetGlobalVector(RoomId, new Vector4(centre.x, centre.z, reach, 0f));

        public static void SetAmbient(Color c) => Shader.SetGlobalColor(AmbientId, c);

        /// <summary>The ambient light as it was last set: for tests.</summary>
        public static Color Ambient => Shader.GetGlobalColor(AmbientId);

        /// <summary>The lights for this frame, brightest first; the first four are used.</summary>
        public static void SetPoints(List<(Vector3 At, float Radius, Color Colour)>? lights)
        {
            for (var i = 0; i < MaxPoints; i++)
            {
                if (lights != null && i < lights.Count)
                {
                    var l = lights[i];
                    Pos[i] = new Vector4(l.At.x, l.At.y, l.At.z, Mathf.Max(0.01f, l.Radius));
                    Col[i] = new Vector4(l.Colour.r, l.Colour.g, l.Colour.b, 0f);
                }
                else
                {
                    Pos[i] = new Vector4(0f, -100f, 0f, 0.01f);
                    Col[i] = Vector4.zero;
                }
            }
            Shader.SetGlobalVectorArray(PosId, Pos);
            Shader.SetGlobalVectorArray(ColId, Col);
        }
    }

    /// <summary>The room's own light over time: the lights striking at the start of a fight, and the alarm's red wash.</summary>
    public sealed class Ambience
    {
        private static readonly Color Alarm = new Color(1.35f, 0.5f, 0.42f);
        private const float StrikeSeconds = 1.05f;
        private const float AlarmSeconds = 1.5f;

        private float _strike = -1f;
        private float _alarm;

        /// <summary>The lights come on as a tube does: two false starts, then they hold.</summary>
        public void Strike()
        {
            if (Motion.Reduced)
            {
                Steady();
                return;
            }
            _strike = 0f;
            Lighting.SetAmbient(Color.white * Brightness(0f));
        }

        public void SoundAlarm()
        {
            if (!Motion.Reduced) _alarm = AlarmSeconds;
        }

        /// <summary>Straight to full, steady light.</summary>
        public void Steady()
        {
            _strike = -1f;
            _alarm = 0f;
            Lighting.SetAmbient(Color.white);
        }

        private static float Brightness(float t)
        {
            if (t < 0.16f) return 0.1f;
            if (t < 0.21f) return 0.8f;
            if (t < 0.36f) return 0.14f;
            if (t < 0.42f) return 0.95f;
            if (t < 0.55f) return 0.3f;
            return Mathf.Lerp(0.72f, 1f, Mathf.InverseLerp(0.55f, StrikeSeconds, t));
        }

        public void Tick(float dt)
        {
            if (_strike < 0f && _alarm <= 0f) return;
            var light = Color.white;
            if (_strike >= 0f)
            {
                _strike += dt;
                if (_strike >= StrikeSeconds) _strike = -1f;
                else light *= Brightness(_strike);
            }
            if (_alarm > 0f)
            {
                _alarm = Mathf.Max(0f, _alarm - dt);
                // Two slow pulses that die away.
                var pulse = Mathf.Pow(Mathf.Sin((AlarmSeconds - _alarm) / AlarmSeconds * Mathf.PI * 2f), 2f) * (_alarm / AlarmSeconds);
                light = Color.Lerp(light, light * Alarm, pulse * 0.75f);
            }
            light.a = 1f;
            Lighting.SetAmbient(light);
        }
    }
}
