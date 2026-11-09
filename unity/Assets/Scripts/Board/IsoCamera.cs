#nullable enable
using UnityEngine;

namespace LabEscape.Client
{
    /// <summary>Tile coordinates in the world: tile (0, 0) covers x 0 to 1 and z -1 to 0, and the floor's top is at height 0.</summary>
    public static class Iso
    {
        public static Vector3 Tile(float x, float y, float height = 0f) => new Vector3(x + 0.5f, height, -(y + 0.5f));

        public static Vector3 Floor(float x, float y, float height = 0f) => new Vector3(x, height, -y);
    }

    /// <summary>
    /// The fixed isometric camera: orthographic and never rotated, with tile A1 at the back. A tile is a diamond twice as wide as
    /// deep. The room is framed inside the part of the screen the HUD leaves free, and the player can pan and zoom from there.
    /// </summary>
    public sealed class IsoCamera
    {
        private const float Pitch = 30f;
        private const float Yaw = -45f;
        private const float MinZoom = 0.8f;
        private const float MaxZoom = 2.6f;

        public readonly Camera Cam;
        public readonly Vector3 Right;
        public readonly Vector3 Up;
        public readonly Vector3 Forward;

        private float _minR, _maxR, _minU, _maxU;
        private Rect _free = new Rect(0, 0, 1, 1);
        private float _zoom = 1f;
        private Vector2 _pan;
        private float _shakeUntil;
        private float _shakeAmp;
        private float _ppu = 100f;

        public IsoCamera()
        {
            var go = new GameObject("Camera");
            Cam = go.AddComponent<Camera>();
            go.tag = "MainCamera";
            Cam.orthographic = true;
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = Palette.Background;
            Cam.nearClipPlane = 0.1f;
            Cam.farClipPlane = 200f;
            Cam.allowHDR = false;
            go.transform.rotation = Quaternion.Euler(Pitch, Yaw, 0f);
            Right = go.transform.right;
            Up = go.transform.up;
            Forward = go.transform.forward;
        }

        /// <summary>Sets the world box to keep in view.</summary>
        public void Frame(Vector3 min, Vector3 max)
        {
            _minR = _minU = float.MaxValue;
            _maxR = _maxU = float.MinValue;
            for (var i = 0; i < 8; i++)
            {
                var p = new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z);
                var r = Vector3.Dot(p, Right);
                var u = Vector3.Dot(p, Up);
                _minR = Mathf.Min(_minR, r);
                _maxR = Mathf.Max(_maxR, r);
                _minU = Mathf.Min(_minU, u);
                _maxU = Mathf.Max(_maxU, u);
            }
        }

        /// <summary>The part of the screen the board may use, in pixels from the bottom left.</summary>
        public void SetFreeArea(Rect free)
        {
            if (free.width > 1f && free.height > 1f) _free = free;
        }

        public void Zoom(float wheel)
        {
            if (Mathf.Approximately(wheel, 0f)) return;
            _zoom = Mathf.Clamp(_zoom * (1f + wheel * 0.08f), MinZoom, MaxZoom);
        }

        /// <summary>Moves the view by a distance in screen pixels.</summary>
        public void PanPixels(Vector2 delta) => _pan -= delta / _ppu;

        public void Shake(bool heavy)
        {
            _shakeUntil = Time.unscaledTime + (heavy ? 0.32f : 0.18f);
            _shakeAmp = heavy ? 0.075f : 0.03f;
        }

        public void Tick()
        {
            float w = Screen.width, h = Screen.height;
            float rangeR = _maxR - _minR, rangeU = _maxU - _minU;
            _ppu = Mathf.Min(_free.width / rangeR, _free.height / rangeU) * 0.96f * _zoom;
            Cam.orthographicSize = h / (2f * _ppu);
            // Zoomed in, the view can travel as far as the room's edge; at the framing zoom it stays put.
            var limitR = Mathf.Max(0f, rangeR - _free.width / _ppu) * 0.5f;
            var limitU = Mathf.Max(0f, rangeU - _free.height / _ppu) * 0.5f;
            _pan = new Vector2(Mathf.Clamp(_pan.x, -limitR, limitR), Mathf.Clamp(_pan.y, -limitU, limitU));
            var r = (_minR + _maxR) * 0.5f - (_free.center.x - w * 0.5f) / _ppu + _pan.x;
            var u = (_minU + _maxU) * 0.5f - (_free.center.y - h * 0.5f) / _ppu + _pan.y;
            if (Time.unscaledTime < _shakeUntil)
            {
                var left = (_shakeUntil - Time.unscaledTime) / 0.3f;
                r += Mathf.Sin(Time.unscaledTime * 83f) * _shakeAmp * left;
                u += Mathf.Sin(Time.unscaledTime * 67f + 1.3f) * _shakeAmp * left;
            }
            Cam.transform.position = Right * r + Up * u - Forward * 60f;
        }

        /// <summary>Where a screen point meets the floor.</summary>
        public Vector3 GroundPoint(Vector2 screen)
        {
            var ray = Cam.ScreenPointToRay(screen);
            var t = -ray.origin.y / ray.direction.y;
            return ray.origin + ray.direction * t;
        }

        /// <summary>The screen box of something standing at a point: halfWidth to each side of it, and height above it.</summary>
        public Rect ScreenBox(Vector3 feet, float halfWidth, float height)
        {
            var a = Cam.WorldToScreenPoint(feet - Right * halfWidth);
            var b = Cam.WorldToScreenPoint(feet + Right * halfWidth + Vector3.up * height);
            return Rect.MinMaxRect(a.x, a.y, b.x, b.y);
        }
    }
}
