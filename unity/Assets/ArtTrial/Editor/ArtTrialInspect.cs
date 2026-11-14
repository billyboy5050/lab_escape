#nullable enable
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LabEscape.ArtTrial.Editor
{
    /// <summary>Logs what the importer made of the trial's packs: each model's materials and textures, and how the clips bind to the rig.</summary>
    public static class ArtTrialInspect
    {
        [MenuItem("Lab Escape/Art Trial/Inspect Packs")]
        public static void Inspect()
        {
            var sb = new StringBuilder();
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { ArtTrial.Packs }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var renderers = go.GetComponentsInChildren<Renderer>(true);
                var bounds = renderers.Length > 0 ? renderers.Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; }) : new Bounds();
                sb.AppendLine($"{path} rig={importer.animationType} scale={importer.globalScale} bounds={bounds.size} root.rot={go.transform.localRotation.eulerAngles}");
                foreach (var r in renderers)
                {
                    foreach (var m in r.sharedMaterials)
                    {
                        sb.AppendLine($"   {r.GetType().Name} {r.name}: {(m == null ? "null" : $"{m.name} [{m.shader.name}] main={(m.mainTexture != null ? m.mainTexture.name : "none")}")}");
                    }
                }
                var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToList();
                if (clips.Count > 0)
                {
                    sb.AppendLine($"   {clips.Count} clips: {string.Join(" ", clips.Take(50).Select(c => c.name))}");
                    var bindings = AnimationUtility.GetCurveBindings(clips[0]);
                    sb.AppendLine($"   first bindings: {string.Join(" | ", bindings.Take(4).Select(b => b.path + "." + b.propertyName))}");
                }
                if (path.Contains("/Characters/") || path.Contains("/Animations/"))
                {
                    sb.AppendLine("   hierarchy: " + string.Join(" ", go.GetComponentsInChildren<Transform>(true).Take(8).Select(t => AnimationUtility.CalculateTransformPath(t, go.transform))));
                }
            }
            Debug.Log("[ArtTrial] Inspect\n" + sb);
        }
    }
}
