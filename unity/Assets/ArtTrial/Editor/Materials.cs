#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace LabEscape.ArtTrial.Editor
{
    /// <summary>
    /// Standard-shader materials for the packs. They are made for URP, with a packed occlusion-roughness-metal map the built-in
    /// Standard shader cannot read, so each material takes the pack's base colour, normal and emissive maps by name and a fixed
    /// smoothness. The materials are saved among the packs, which git ignores.
    /// </summary>
    public static class Materials
    {
        private const string Folder = ArtTrial.Packs + "/Materials";
        private static readonly Dictionary<string, Material> Made = new Dictionary<string, Material>();

        /// <summary>A flat colour, for the slab and the base plates.</summary>
        public static Material Plain(Color colour, float smoothness = 0.2f)
        {
            var key = $"Plain_{ColorUtility.ToHtmlStringRGB(colour)}";
            if (Made.TryGetValue(key, out var m)) return m;
            m = new Material(Shader.Find("Standard")) { name = key, color = colour };
            m.SetFloat("_Glossiness", smoothness);
            return Made[key] = Save(m);
        }

        /// <summary>Gives every renderer under `go` a Standard material with its textures, found by the material's name.</summary>
        public static void Fix(GameObject go)
        {
            var pack = PackOf(go);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.sharedMaterials = r.sharedMaterials.Select(m => m == null ? m : For(pack, m)).ToArray();
            }
        }

        /// <summary>A character's skin: the light or dark base colour of its body, with its normal map.</summary>
        public static void Skin(GameObject go, string body, string tone)
        {
            var tex = $"{ArtTrial.Packs}/Characters/Textures";
            var sex = body.Contains("Female") ? "Female" : "Male";
            var baseName = sex == "Male" ? (tone == "Light" ? "T_Superhero_Male_Ligh" : "T_Superhero_Male_Dark") : $"T_Superhero_Female_{tone}_BaseColor";
            var key = $"Skin_{sex}_{tone}";
            if (!Made.TryGetValue(key, out var m))
            {
                m = new Material(Shader.Find("Standard")) { name = key };
                m.mainTexture = Load($"{tex}/{baseName}.png");
                var normal = Load($"{tex}/Normals Unity - Godot/T_Superhero_{sex}_Normal.png") ?? Load($"{tex}/T_Superhero_{sex}_Normal.png");
                if (normal != null)
                {
                    m.SetTexture("_BumpMap", normal);
                    m.EnableKeyword("_NORMALMAP");
                }
                m.SetFloat("_Glossiness", 0.35f);
                Made[key] = m = Save(m);
            }
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                // Eyes keep their own material.
                r.sharedMaterials = r.sharedMaterials.Select(x => x != null && x.name.ToLowerInvariant().Contains("eye") ? x : m).ToArray();
            }
        }

        private static string PackOf(GameObject go)
        {
            var path = AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(go));
            return Path.GetDirectoryName(path)!.Replace('\\', '/');
        }

        private static Material For(string pack, Material imported)
        {
            var name = imported.name;
            var key = pack + "/" + name;
            if (Made.TryGetValue(key, out var m)) return m;
            m = new Material(Shader.Find("Standard")) { name = "Trial_" + name };
            var stem = Stem(name);
            var albedo = imported.mainTexture as Texture2D ?? Find(pack, stem, "BaseColor") ?? Find(pack, stem, "");
            if (albedo != null) m.mainTexture = albedo;
            else m.color = imported.color;
            var normal = Find(pack, stem, "Normal");
            if (normal != null)
            {
                m.SetTexture("_BumpMap", normal);
                m.EnableKeyword("_NORMALMAP");
            }
            var emissive = Find(pack, stem, "Emissive");
            if (emissive != null)
            {
                m.SetTexture("_EmissionMap", emissive);
                m.SetColor("_EmissionColor", Color.white);
                m.EnableKeyword("_EMISSION");
            }
            m.SetFloat("_Glossiness", 0.45f);
            m.SetFloat("_Metallic", 0.25f);
            Debug.Log($"[ArtTrial] {key}: base {(albedo != null ? albedo.name : "colour")}, normal {(normal != null ? normal.name : "none")}, emissive {(emissive != null ? emissive.name : "none")}");
            return Made[key] = Save(m);
        }

        /// <summary>The part of a material's name its textures share: "MI_Trim_01" and "Trim_01.001" are both "Trim_01".</summary>
        private static string Stem(string name)
        {
            var s = name.Split('.')[0];
            foreach (var prefix in new[] { "MI_", "M_", "T_" })
            {
                if (s.StartsWith(prefix)) s = s.Substring(prefix.Length);
            }
            return s;
        }

        private static Texture2D? Find(string pack, string stem, string kind)
        {
            var folder = pack + "/Textures";
            var want = kind.Length == 0 ? $"T_{stem}" : $"T_{stem}_{kind}";
            var guid = AssetDatabase.FindAssets(want + " t:Texture2D", new[] { folder })
                .FirstOrDefault(g => Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g)) == want);
            return guid == null ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(guid));
        }

        private static Texture2D? Load(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path);

        private static Material Save(Material m)
        {
            Directory.CreateDirectory(Folder);
            var path = $"{Folder}/{m.name}.mat";
            AssetDatabase.CreateAsset(m, path);
            return AssetDatabase.LoadAssetAtPath<Material>(path);
        }
    }
}
