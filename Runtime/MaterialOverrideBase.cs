using System;
using System.Collections.Generic;
using nadena.dev.ndmf;
using UnityEngine;
using UnityEngine.Rendering;

namespace Uto.MaterialOverride
{
    // One recorded difference of a single material property (same idea as TTT's MaterialProperty).
    [Serializable]
    public struct MaterialPropertyOverride
    {
        public string PropertyName;
        public ShaderPropertyType PropertyType;
        public Texture TextureValue;
        public Vector2 TextureOffsetValue;
        public Vector2 TextureScaleValue;
        public Color ColorValue;
        public Vector4 VectorValue;
        public int IntValue;
        public float FloatValue;
    }

    // The recorded set of differences from TargetMaterial, shared by every kind of override component.
    // What it applies to (one slot, a whole subtree) is up to the subclass.
    public abstract class MaterialOverrideBase : MonoBehaviour, INDMFEditorOnly
    {
        // The baseline the differences are recorded against.
        public Material TargetMaterial;

        // Only used by the transfer window as the values to copy from.
        public Material SourceMaterial;

        public bool IsOverrideShader;
        public Shader OverrideShader;
        public bool IsOverrideRenderQueue;
        public int OverrideRenderQueue = 2000;
        public List<MaterialPropertyOverride> OverrideProperties = new List<MaterialPropertyOverride>();

        // Shader keywords that differ from TargetMaterial.
        public List<string> EnabledKeywords = new List<string>();
        public List<string> DisabledKeywords = new List<string>();
    }
}
