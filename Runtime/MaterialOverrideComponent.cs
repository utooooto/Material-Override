using System;
using System.Collections.Generic;
using nadena.dev.ndmf;
using UnityEngine;
using UnityEngine.Rendering;

namespace Shinano.MaterialOverride
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

    // Records the final state of one material slot of the renderer on the same GameObject as a
    // set of differences from TargetMaterial. The NDMF plugin clones the slot's material at build
    // time and applies the differences, so the original material asset is never modified.
    [AddComponentMenu("Shinano/Material Override")]
    public sealed class MaterialOverrideComponent : MonoBehaviour, INDMFEditorOnly
    {
        // Slot of Renderer.sharedMaterials to modify.
        public int SlotIndex;

        // The baseline the differences are recorded against. The inspector keeps it in sync with the
        // slot's material; the build and the preview always start from the slot's current material.
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

        // Unity only draws the enable checkbox for a component that defines one of its event methods.
        // A disabled component is ignored by the build and the preview.
        private void Start()
        {
        }
    }
}
