#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using LabEscape.Core;
using UnityEngine;

namespace LabEscape.Client
{
    /// <summary>
    /// Plays a saved fight through the real fight screen with no one at the keyboard, taking screenshots on the way, then quits with
    /// exit code 0 for a fight that reached its end. Turned on from the command line: -labSmoke replay.json output-folder.
    ///
    /// Adding -labFilm 0,3,17 films the animation that follows those commands (0 is the fight's opening) instead: the game then
    /// advances a sixtieth of a second for every frame drawn, each frame is saved half-size under film/, and the commands in
    /// between are skipped. Motion cannot be judged from a still, so this is how an animation change gets looked at.
    /// </summary>
    public sealed class SmokeRun : MonoBehaviour
    {
        private const float FilmStep = 1f / 60f;
        /// <summary>Frames filmed after the queue has run out, so what is still falling is seen to land.</summary>
        private const int FilmTail = 50;

        private FightDirector _d = null!;
        private FightRecord _record = null!;
        private string _out = "";
        private HashSet<int>? _film;
        private int _frame;
        private StreamWriter? _cues;

        public static void AttachIfAsked(FightDirector d)
        {
            var args = Environment.GetCommandLineArgs();
            var at = Array.IndexOf(args, "-labSmoke");
            if (at < 0 || at + 2 >= args.Length) return;
            var run = d.gameObject.AddComponent<SmokeRun>();
            run._d = d;
            run._record = Records.CheckCompatible(d.Content!, JsonIn.Parse(File.ReadAllText(args[at + 1])));
            run._out = args[at + 2];
            Directory.CreateDirectory(run._out);
            var film = Array.IndexOf(args, "-labFilm");
            if (film < 0 || film + 1 >= args.Length) return;
            run._film = new HashSet<int>();
            foreach (var part in args[film + 1].Split(','))
            {
                if (int.TryParse(part, out var n)) run._film.Add(n);
            }
        }

        /// <summary>Saves the frame just drawn at half size, and films until the fight is idle and the tail has run.</summary>
        private IEnumerator Film(int command)
        {
            var folder = Path.Combine(_out, "film", command.ToString("000"));
            Directory.CreateDirectory(folder);
            var tail = FilmTail;
            _frame = 0;
            while (tail > 0)
            {
                yield return new WaitForEndOfFrame();
                var shot = ScreenCapture.CaptureScreenshotAsTexture();
                int w = shot.width / 2, h = shot.height / 2;
                var rt = RenderTexture.GetTemporary(w, h, 0);
                Graphics.Blit(shot, rt);
                var before = RenderTexture.active;
                RenderTexture.active = rt;
                var small = new Texture2D(w, h, TextureFormat.RGB24, false);
                small.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                small.Apply();
                RenderTexture.active = before;
                RenderTexture.ReleaseTemporary(rt);
                File.WriteAllBytes(Path.Combine(folder, $"{_frame:0000}.jpg"), small.EncodeToJPG(84));
                Destroy(shot);
                Destroy(small);
                _frame++;
                if (_d.Idle || _d.EndScreenShown || _d.Flow!.Live.Outcome != null && !_d.Flow.Busy) tail--;
            }
        }

        private void NoteCue(Cue cue)
        {
            _cues?.WriteLine($"{_frame:0000} {cue.At:0.000} {cue.Kind} {cue.Event.T}{(cue.Duration > 0f ? $" for {cue.Duration:0.000}" : "")}");
            _cues?.Flush();
        }

        private IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(_out, name + ".png"));
            yield return null;
        }

        /// <summary>The tile a command acts on, so its preview can be shown first.</summary>
        public static Pos? TileOf(Command cmd)
        {
            switch (cmd)
            {
                case MoveCommand m: return Tiles.Parse(m.Path[m.Path.Count - 1]);
                case AbilityCommand a: return Tiles.Parse(a.Target2 ?? a.Target);
                case ReloadCommand r: return Tiles.Parse(r.Target);
                case PickUpMineCommand p: return Tiles.Parse(p.Target);
                case RedeployCommand r: return Tiles.Parse(r.Target);
                default: return null;
            }
        }

        private IEnumerator Start()
        {
            yield return null;
            _d.ScriptedPointer = true;
            if (_film != null)
            {
                _d.FixedStep = FilmStep;
                Directory.CreateDirectory(Path.Combine(_out, "film"));
                _cues = new StreamWriter(Path.Combine(_out, "film", "cues.txt"));
            }
            _d.StartFight(_record.Loadout, _record.Settings, null);
            var flow = _d.Flow!;
            if (_film != null)
            {
                _d.Board!.CuePlayed += NoteCue;
                _cues!.WriteLine("command 000");
                if (_film.Contains(0)) yield return Film(0);
                else flow.SetSpeed(FightSpeed.Skip);
            }
            else
            {
                flow.SetSpeed(FightSpeed.Fast);
                yield return Shot("00-opening");
            }
            var seen = new HashSet<string>();
            var i = 0;
            foreach (var cmd in _record.Commands)
            {
                i++;
                while (!_d.Idle) yield return null;
                if (_film != null)
                {
                    var filmed = _film.Contains(i);
                    flow.SetSpeed(filmed ? FightSpeed.Normal : FightSpeed.Skip);
                    _cues!.WriteLine($"command {i:000} {cmd.Type}");
                    var done = _d.Commit(cmd);
                    if (done == null || !done.Ok) throw new InvalidOperationException($"Command {i} was refused: {done?.Error}");
                    if (filmed) yield return Film(i);
                    if (flow.Live.Outcome != null) break;
                    continue;
                }
                var tile = TileOf(cmd);
                _d.SetPointer(tile);
                yield return null;
                yield return null;
                if (seen.Add(cmd.Type) || i % 12 == 0) yield return Shot($"{i:000}-preview-{cmd.Type}");
                _d.SetPointer(null);
                var r = _d.Commit(cmd);
                if (r == null || !r.Ok) throw new InvalidOperationException($"Command {i} was refused: {r?.Error}");
                if (i % 9 == 0)
                {
                    yield return new WaitForSecondsRealtime(0.15f);
                    yield return Shot($"{i:000}-resolving");
                }
            }
            while (flow.Busy || !_d.EndScreenShown) yield return null;
            yield return Shot("zz-end");
            var outcome = flow.Live.Outcome;
            File.WriteAllText(Path.Combine(_out, "result.txt"), $"{outcome?.Result} round {outcome?.Round}: {outcome?.Cause}\ncommands {_record.Commands.Count}\nhash {_d.Content!.Hash}\n");
            _cues?.Dispose();
            Application.Quit(0);
        }
    }
}
