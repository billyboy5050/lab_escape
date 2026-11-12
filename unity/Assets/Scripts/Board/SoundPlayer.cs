#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace LabEscape.Client
{
    /// <summary>
    /// The fight's sound. Every sound is rendered once into a clip when the game starts (Synth), so the project keeps no audio
    /// files. Sounds asked for during a frame are played together at its end, through the gate that keeps a burst to a few.
    /// Muting is remembered between runs.
    /// </summary>
    public sealed class SoundPlayer
    {
        private const string MutedKey = "lab-escape.muted";

        private readonly AudioSource _source;
        private readonly Dictionary<Sfx, AudioClip> _clips = new Dictionary<Sfx, AudioClip>();
        private readonly SoundGate _gate = new SoundGate();
        private readonly List<Sfx> _asked = new List<Sfx>();

        public bool Muted { get; private set; }

        /// <summary>A sound has started: for the filmed smoke run, which notes it beside the cues.</summary>
        public event Action<Sfx>? Played;

        public SoundPlayer(Transform parent)
        {
            var go = new GameObject("Sound");
            go.transform.SetParent(parent, false);
            // The camera is made in code and has no listener of its own.
            if (UnityEngine.Object.FindAnyObjectByType<AudioListener>() == null) go.AddComponent<AudioListener>();
            _source = go.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            // A saved preference is untrusted: anything but 1 means sound on.
            Muted = PlayerPrefs.GetInt(MutedKey, 0) == 1;

            var rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 44100;
            foreach (Sfx s in Enum.GetValues(typeof(Sfx)))
            {
                var data = Synth.Render(SoundCues.Recipe(s), rate);
                var clip = AudioClip.Create(s.ToString(), data.Length, 1, rate, false);
                clip.SetData(data, 0);
                clip.hideFlags = HideFlags.HideAndDontSave;
                _clips[s] = clip;
            }
        }

        public void Ask(Sfx s) => _asked.Add(s);

        /// <summary>Plays what this frame asked for. Called once a frame, after everything that can ask.</summary>
        public void Flush(float now)
        {
            if (_asked.Count == 0) return;
            var play = _gate.Take(_asked, now);
            _asked.Clear();
            if (Muted) return;
            foreach (var s in play)
            {
                if (!_clips.TryGetValue(s, out var clip)) continue;
                _source.PlayOneShot(clip);
                Played?.Invoke(s);
            }
        }

        /// <summary>The clips are made in code and are not freed with the scene: the director frees them when it goes.</summary>
        public void Dispose()
        {
            foreach (var clip in _clips.Values) UnityEngine.Object.Destroy(clip);
            _clips.Clear();
        }

        public void SetMuted(bool muted)
        {
            Muted = muted;
            PlayerPrefs.SetInt(MutedKey, muted ? 1 : 0);
            PlayerPrefs.Save();
            if (muted) _source.Stop();
        }
    }
}
