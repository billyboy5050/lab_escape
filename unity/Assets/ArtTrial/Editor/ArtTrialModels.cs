#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using LabEscape.Client;
using LabEscape.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LabEscape.ArtTrial.Editor
{
    /// <summary>
    /// A line-up of every unit through the game's camera: the greybox's figures behind, the generated models in front, then the
    /// generated ones closer. Needs no packs. In batch mode, with graphics:
    /// -executeMethod LabEscape.ArtTrial.Editor.ArtTrialModels.Lineup -artTrialOut &lt;folder&gt;.
    /// </summary>
    public static class ArtTrialModels
    {
        public static readonly (string Def, Team Team)[] Units =
        {
            ("player", Team.Player), ("guard", Team.Enemy), ("medic", Team.Enemy), ("flamer", Team.Enemy), ("warden", Team.Enemy),
            ("turret", Team.Player), ("drone", Team.Player), ("hatchling", Team.Player), ("spitter", Team.Player), ("burster", Team.Player),
        };

        // Along the screen and into it, for the game's camera (pitch 30, yaw -45).
        private static readonly Vector3 Across = new Vector3(0.7071f, 0f, 0.7071f);
        private static readonly Vector3 Away = new Vector3(-0.7071f, 0f, 0.7071f);

        /// <summary>Which way the line-up faces: toward the camera, turned a little to show a side.</summary>
        private const float FaceYaw = 160f;

        [MenuItem("Lab Escape/Art Trial/Line-up")]
        public static void Lineup()
        {
            try
            {
                var outDir = ArtTrial.OutDir();
                Directory.CreateDirectory(outDir);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var cam = ArtTrial.MakeCamera();
                var root = new GameObject("Line-up").transform;
                var n = Units.Length;
                var centre = Across * ((n - 1) * 0.5f);
                Lighting.Reset();
                Lighting.SetRoom(new Vector3(centre.x, 0f, centre.z), n * 0.6f);
                Floor(root, centre, n);

                var grey = new List<GameObject>();
                for (var i = 0; i < n; i++)
                {
                    var (def, team) = Units[i];
                    var g = Figures.Unit(def, team, root, false);
                    Place(g, Across * i + Away * 0.9f);
                    g.Paint(1f);
                    grey.Add(g.Root);
                    var made = Figures.Unit(def, team, root, true);
                    made.Paint(1f);
                    Place(made, Across * i - Away * 0.2f);
                    var tris = 0;
                    foreach (var f in made.Body.GetComponentsInChildren<MeshFilter>()) tris += f.sharedMesh.triangles.Length / 3;
                    Debug.Log($"[ArtTrial] {def}: {tris} triangles in {made.BodyParts.Count} pieces");
                }
                Frame(cam, Across * -0.6f - Away * 0.8f, Across * (n - 0.4f) + Away * 1.5f, 1.1f);
                ArtTrial.Shoot(cam, Path.Combine(outDir, "lineup.png"));

                // Closer, the generated models alone: people, then machines and hatchlings.
                foreach (var g in grey) UnityEngine.Object.DestroyImmediate(g);
                Frame(cam, Across * -0.55f - Away * 0.65f, Across * 4.55f + Away * 0.35f, 1.0f);
                ArtTrial.Shoot(cam, Path.Combine(outDir, "models-people.png"));
                Frame(cam, Across * 4.45f - Away * 0.65f, Across * 9.55f + Away * 0.35f, 1.0f);
                ArtTrial.Shoot(cam, Path.Combine(outDir, "models-machines.png"));
                Debug.Log($"[ArtTrial] Line-up captured into {outDir}");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        private static void Place(Model m, Vector3 at)
        {
            m.Root.transform.position = at;
            m.Body.rotation = Quaternion.Euler(0f, FaceYaw, 0f);
        }

        private static void Frame(Camera cam, Vector3 a, Vector3 b, float height)
        {
            ArtTrial.Frame(cam, Vector3.Min(a, b) + Vector3.down * 0.05f, Vector3.Max(a, b) + Vector3.up * height);
        }

        /// <summary>A plain floor in the greybox's colours under the line-up.</summary>
        private static void Floor(Transform root, Vector3 centre, int n)
        {
            var m = new Model("floor", root);
            m.Root.transform.position = centre + Away * 0.35f + Vector3.down * 0.06f;
            m.Root.transform.rotation = Quaternion.LookRotation(Across, Vector3.up);
            m.Add(m.Root.transform, Shapes.Box, Vector3.zero, new Vector3(4f, 0.06f, n + 1.5f), Palette.FloorA);
            m.Paint(1f);
        }
    }
}
