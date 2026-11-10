#nullable enable
using LabEscape.Core;
using UnityEngine;

namespace LabEscape.Client
{
    /// <summary>A value on a damped spring. Push it, or move its target, and it follows, overshoots and settles.</summary>
    public struct Spring
    {
        public float Value;
        public float Velocity;
        public float Target;

        /// <summary>`hz` is how fast it swings; `damping` of 1 settles without overshoot, and less than 1 wobbles first.</summary>
        public void Tick(float dt, float hz, float damping)
        {
            if (dt <= 0f) return;
            var w = hz * 2f * Mathf.PI;
            // Short fixed steps keep a stiff spring stable through a long frame.
            var steps = Mathf.Clamp(Mathf.CeilToInt(dt / 0.006f), 1, 12);
            var h = dt / steps;
            for (var i = 0; i < steps; i++)
            {
                Velocity += (-(Value - Target) * w * w - Velocity * 2f * damping * w) * h;
                Value += Velocity * h;
            }
        }

        public void Rest(float at = 0f)
        {
            Value = Target = at;
            Velocity = 0f;
        }
    }

    /// <summary>Two springs for something that moves on the floor: X is world x, Z is world z.</summary>
    public struct Spring2
    {
        public Spring X;
        public Spring Z;

        public Vector3 Value => new Vector3(X.Value, 0f, Z.Value);

        public void Push(Vector3 velocity)
        {
            X.Velocity += velocity.x;
            Z.Velocity += velocity.z;
        }

        public void Aim(Vector3 target)
        {
            X.Target = target.x;
            Z.Target = target.z;
        }

        public void Tick(float dt, float hz, float damping)
        {
            X.Tick(dt, hz, damping);
            Z.Tick(dt, hz, damping);
        }

        public void Rest()
        {
            X.Rest();
            Z.Rest();
        }
    }

    /// <summary>
    /// How the fight feels: every number that gives a blow its weight, in one place so it can be tuned by hand. None of them is a
    /// rule. Lengths are in tiles, times in seconds, and a unit's heft (see Heft) scales most of them: a heavy body moves less,
    /// swings slower and lands harder.
    /// </summary>
    public static class Feel
    {
        // ---- bodies
        /// <summary>How fast a hit shoves a unit of the player's weight, per point of damage and at the least.</summary>
        public const float ShoveBase = 2.6f;
        public const float ShovePerDamage = 1.1f;
        public const float ShoveHz = 3.6f;
        public const float ShoveDamping = 0.82f;
        /// <summary>Degrees a second a hit tips a body over, and the spring that stands it back up.</summary>
        public const float TipBase = 520f;
        public const float TipPerDamage = 170f;
        public const float LeanHz = 3.2f;
        public const float LeanDamping = 0.42f;
        public const float SquashHz = 5.5f;
        public const float SquashDamping = 0.36f;
        /// <summary>
        /// How hard things squash a body, as the speed given to its squash spring. About 10 flattens it by a sixth before it
        /// springs back; negative is shorter and wider, positive taller and thinner.
        /// </summary>
        public const float HitSquash = -6f;
        public const float HitSquashPerDamage = -1.5f;
        public const float BlockSquash = -4f;
        public const float RecoilSquash = 5.5f;
        public const float BlowSquash = -8f;
        public const float SlamSquash = -10f;
        public const float PulseSquash = 8f;
        /// <summary>The pose a unit holds while it gathers: leaning back from its target, and crouched.</summary>
        public const float WindupLean = 13f;
        public const float WindupCrouch = -0.16f;
        public const float WindupBack = 0.07f;
        /// <summary>The kick of a gun on the unit that fires it.</summary>
        public const float RecoilShove = 2.4f;
        public const float RecoilTip = 300f;
        /// <summary>How far a melee lunge carries a unit towards its target, and how long it takes to come back.</summary>
        public const float StrikeReach = 0.46f;
        public const float StrikeReturn = 0.26f;
        public const float WalkLean = 9f;
        public const float WalkHop = 0.085f;
        public const float FootfallSquash = -4.2f;
        public const float DashLean = 24f;
        public const float DashStretch = 0.2f;
        /// <summary>A wave unit falls from this height, stretched, and lands this hard.</summary>
        public const float DropHeight = 5.5f;
        public const float DropStretch = 0.22f;
        public const float LandSquash = -4.2f;

        // ---- deaths
        public const float Gravity = 15f;
        public const float DeathPop = 2.5f;
        public const float DeathFling = 1.25f;
        public const float DeathSpin = 420f;
        public const float BodyRest = 0.3f;
        public const float BodyFade = 0.32f;
        public const float Bounce = 0.28f;

        // ---- the clock
        /// <summary>Seconds the board stops for a hit of each size. A kill or a hit on the player holds a little longer.</summary>
        public static float HitStop(int amount, bool killed, bool onPlayer)
        {
            var s = amount <= 0 ? 0.02f : amount <= 2 ? 0.04f : amount <= 4 ? 0.065f : 0.09f;
            if (killed) s += 0.03f;
            if (onPlayer && amount > 0) s = Mathf.Max(s, 0.075f);
            return s;
        }

        public const float BlastStop = 0.075f;
        public const float SlamStop = 0.11f;
        /// <summary>The blow that ends the fight plays at this speed for this long.</summary>
        public const float FinalSlow = 0.22f;
        public const float FinalSlowSeconds = 1.1f;

        // ---- the camera
        /// <summary>Trauma is 0 to 1; the shake is its square, so small knocks barely show and big ones do.</summary>
        public static float HitTrauma(int amount, bool killed, bool onPlayer) => Mathf.Clamp01(0.1f + 0.07f * amount + (killed ? 0.08f : 0f) + (onPlayer ? 0.16f : 0f));

        public const float BlastTrauma = 0.62f;
        public const float SlamTrauma = 0.75f;
        public const float HazardTrauma = 0.3f;
        /// <summary>Trauma from something landing, for a body of the player's weight falling from a walk; drops and deaths scale it.</summary>
        public const float ThudTrauma = 0.22f;
        /// <summary>A landing also knocks the view down, by this many tiles for each point of weight, up to a limit.</summary>
        public const float ThudKick = 0.022f;
        public const float ThudKickMost = 0.14f;
        public const float TraumaDecay = 1.7f;
        public const float ShakeTiles = 0.16f;
        public const float ShakeHz = 23f;
        /// <summary>The camera is knocked along a blow's direction, this many tiles, and springs back.</summary>
        public const float KickBase = 0.03f;
        public const float KickPerDamage = 0.028f;
        public const float BlastPunch = 0.045f;
        public const float SlamKick = 0.18f;
        public const float KickHz = 5f;
        public const float KickDamping = 0.55f;
        public const float PunchHz = 4.2f;
        public const float PunchDamping = 0.5f;

        // ---- the entrance
        /// <summary>
        /// Seconds the fight waits on its title before the first wave falls. The title has gone and the camera has all but come
        /// to rest by then, so nothing covers the tiles the wave lands on.
        /// </summary>
        public const float OpenerLead = 1.35f;
        public const float OpenerTitle = 1.05f;
        public const float OpenerZoom = 1.42f;
        public const float OpenerEase = 1.5f;

        /// <summary>How light or heavy a thing swings: springs slow down for heavy bodies.</summary>
        public static float Swing(float heft) => 1f / Mathf.Pow(Mathf.Max(0.2f, heft), 0.4f);

        /// <summary>How much a blow moves a body: heavy ones shrug it off.</summary>
        public static float Give(float heft) => 1f / Mathf.Pow(Mathf.Max(0.2f, heft), 0.8f);

        /// <summary>A fixed number from 0 to 1 for decoration only, so a replay draws the same debris.</summary>
        public static float Noise(int a, int b, int salt)
        {
            unchecked
            {
                var h = (uint)((a + 17) * 374761393) + (uint)((b + 31) * 668265263) + (uint)((salt + 7) * -2048144777);
                h = (h ^ (h >> 13)) * 1274126177u;
                return (h ^ (h >> 16)) / 4294967296f;
            }
        }

        public static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// <summary>The direction on the floor from a cue's origin to a tile, or zero when the blow came from nowhere.</summary>
        public static Vector3 From(Cue cue, Pos target)
        {
            if (!cue.HasOrigin) return Vector3.zero;
            var d = Iso.Tile(target.X, target.Y) - Iso.Tile(cue.OriginX, cue.OriginY);
            return d.sqrMagnitude < 0.0001f ? Vector3.zero : d.normalized;
        }
    }
}
