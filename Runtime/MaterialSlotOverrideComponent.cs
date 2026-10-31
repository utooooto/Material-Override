using UnityEngine;

namespace Uto.MaterialOverride
{
    // Records the final state of one material slot of the renderer on the same GameObject as a
    // set of differences from TargetMaterial. The NDMF plugin clones the slot's material at build
    // time and applies the differences, so the original material asset is never modified.
    [AddComponentMenu("Uto/Material Slot Override")]
    public sealed class MaterialSlotOverrideComponent : MaterialOverrideBase
    {
        // Slot of Renderer.sharedMaterials to modify. The inspector keeps TargetMaterial in sync with
        // the slot's material; the build and the preview always start from the slot's current material.
        public int SlotIndex;

        // Unity only draws the enable checkbox for a component that defines one of its event methods.
        // A disabled component is ignored by the build and the preview.
        private void Start()
        {
        }
    }
}
