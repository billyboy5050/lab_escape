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
    /// </summary>
    public sealed class SmokeRun : MonoBehaviour
    {
        private FightDirector _d = null!;
        private FightRecord _record = null!;
        private string _out = "";

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
            _d.StartFight(_record.Loadout, _record.Settings, null);
            var flow = _d.Flow!;
            flow.SetSpeed(FightSpeed.Fast);
            yield return Shot("00-opening");
            var seen = new HashSet<string>();
            var i = 0;
            foreach (var cmd in _record.Commands)
            {
                i++;
                while (!_d.Idle) yield return null;
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
            Application.Quit(0);
        }
    }
}
