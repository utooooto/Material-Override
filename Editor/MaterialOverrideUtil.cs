using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Shinano.MaterialOverride
{
    // Shared by the inspector, the transfer window and the NDMF plugin so that
    // "what is recorded" and "what is applied at build time" cannot drift apart.
    internal static class MaterialOverrideUtil
    {
        // Applies the recorded differences to an editable material.
        // Order matters: changing the shader resets the render queue, so the queue goes last.
        public static void Apply(Material mat, MaterialOverrideComponent c)
        {
            if (c.IsOverrideShader && c.OverrideShader != null)
                mat.shader = c.OverrideShader;

            foreach (var p in c.OverrideProperties)
                TrySet(mat, p);

            foreach (var k in c.EnabledKeywords) mat.EnableKeyword(k);
            foreach (var k in c.DisabledKeywords) mat.DisableKeyword(k);

            if (c.IsOverrideRenderQueue)
                mat.renderQueue = c.OverrideRenderQueue;
        }

        // A component claims its slot once a target is recorded. Two claims on the same slot of the
        // same renderer are not allowed: the component that comes first on the GameObject keeps the slot,
        // every later one is inactive (the inspector says so, the build reports an error and skips it).
        // Disabled components do not claim anything.
        public static bool IsSlotClaimedByOther(MaterialOverrideComponent c, int slot)
        {
            foreach (var other in c.GetComponents<MaterialOverrideComponent>())
            {
                if (other == c) return false;
                if (other.enabled && other.TargetMaterial != null && other.SlotIndex == slot) return true;
            }
            return false;
        }

        // A throwaway copy of the target with the recorded differences applied (what the build will produce).
        public static Material CreateRecordingMaterial(MaterialOverrideComponent c)
        {
            var mat = new Material(c.TargetMaterial)
            {
                name = "Modified Material",
                hideFlags = HideFlags.HideAndDontSave,
            };
            Apply(mat, c);
            return mat;
        }

        // Rewrites the recorded differences so that original + differences == modified.
        public static void WriteDiff(MaterialOverrideComponent c, Material original, Material modified)
        {
            Undo.RecordObject(c, "Material Override");

            c.IsOverrideShader = original.shader != modified.shader;
            c.OverrideShader = c.IsOverrideShader ? modified.shader : null;

            c.IsOverrideRenderQueue = original.renderQueue != modified.renderQueue;
            if (c.IsOverrideRenderQueue) c.OverrideRenderQueue = modified.renderQueue;

            c.OverrideProperties = GetDiffProperties(original, modified).ToList();

            var before = new HashSet<string>(original.shaderKeywords);
            var after = new HashSet<string>(modified.shaderKeywords);
            c.EnabledKeywords = after.Where(k => !before.Contains(k)).ToList();
            c.DisabledKeywords = before.Where(k => !after.Contains(k)).ToList();

            EditorUtility.SetDirty(c);
            PrefabUtility.RecordPrefabInstancePropertyModifications(c);
        }

        public static int CountOverrides(MaterialOverrideComponent c)
        {
            return (c.IsOverrideShader ? 1 : 0) + (c.IsOverrideRenderQueue ? 1 : 0)
                   + c.OverrideProperties.Count + c.EnabledKeywords.Count + c.DisabledKeywords.Count;
        }

        // Recorded properties that the material's shader does not have; they are skipped when applying
        public static List<string> GetMissingProperties(Material material, MaterialOverrideComponent c)
        {
            return c.OverrideProperties
                .Where(p => !material.HasProperty(p.PropertyName))
                .Select(p => p.PropertyName)
                .ToList();
        }

        // "a, b, c, ..." for messages
        public static string JoinNames(List<string> names, int shown = 8)
        {
            return string.Join(", ", names.Take(shown)) + (names.Count > shown ? ", ..." : "");
        }

        private static IEnumerable<MaterialPropertyOverride> GetDiffProperties(Material original, Material modified)
        {
            var shader = modified.shader;
            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                var type = shader.GetPropertyType(i);
                string name = shader.GetPropertyName(i);
                if (!Capture(modified, name, type, out var now)) continue;

                // A property the original does not have is always a difference
                if (original.HasProperty(name)
                    && Capture(original, name, type, out var was)
                    && AreEqual(was, now))
                    continue;

                yield return now;
            }
        }

        private static bool Capture(Material m, string name, ShaderPropertyType type, out MaterialPropertyOverride o)
        {
            o = new MaterialPropertyOverride { PropertyName = name, PropertyType = type };
            switch (type)
            {
                case ShaderPropertyType.Texture:
                    o.TextureValue = m.GetTexture(name);
                    o.TextureOffsetValue = m.GetTextureOffset(name);
                    o.TextureScaleValue = m.GetTextureScale(name);
                    return true;
                case ShaderPropertyType.Color:
                    o.ColorValue = m.GetColor(name);
                    return true;
                case ShaderPropertyType.Vector:
                    o.VectorValue = m.GetVector(name);
                    return true;
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    o.FloatValue = m.GetFloat(name);
                    return true;
                case ShaderPropertyType.Int:
                    o.IntValue = m.GetInteger(name);
                    return true;
                default:
                    return false;
            }
        }

        // Float comparisons are tolerant: values copied between materials can differ in the last bits.
        private static bool AreEqual(MaterialPropertyOverride a, MaterialPropertyOverride b)
        {
            switch (a.PropertyType)
            {
                case ShaderPropertyType.Texture:
                    return a.TextureValue == b.TextureValue
                           && a.TextureOffsetValue == b.TextureOffsetValue
                           && a.TextureScaleValue == b.TextureScaleValue;
                case ShaderPropertyType.Color:
                    return Approx((Vector4)a.ColorValue, (Vector4)b.ColorValue);
                case ShaderPropertyType.Vector:
                    return Approx(a.VectorValue, b.VectorValue);
                case ShaderPropertyType.Int:
                    return a.IntValue == b.IntValue;
                default:
                    return Mathf.Approximately(a.FloatValue, b.FloatValue);
            }
        }

        private static bool Approx(Vector4 a, Vector4 b)
        {
            return Mathf.Approximately(a.x, b.x) && Mathf.Approximately(a.y, b.y)
                   && Mathf.Approximately(a.z, b.z) && Mathf.Approximately(a.w, b.w);
        }

        private static void TrySet(Material m, MaterialPropertyOverride p)
        {
            if (string.IsNullOrEmpty(p.PropertyName) || !m.HasProperty(p.PropertyName)) return;
            switch (p.PropertyType)
            {
                case ShaderPropertyType.Texture:
                    m.SetTexture(p.PropertyName, p.TextureValue);
                    m.SetTextureOffset(p.PropertyName, p.TextureOffsetValue);
                    m.SetTextureScale(p.PropertyName, p.TextureScaleValue);
                    break;
                case ShaderPropertyType.Color:
                    m.SetColor(p.PropertyName, p.ColorValue);
                    break;
                case ShaderPropertyType.Vector:
                    m.SetVector(p.PropertyName, p.VectorValue);
                    break;
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    m.SetFloat(p.PropertyName, p.FloatValue);
                    break;
                case ShaderPropertyType.Int:
                    m.SetInteger(p.PropertyName, p.IntValue);
                    break;
            }
        }
    }
}
