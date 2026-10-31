using UnityEngine;

namespace Uto.MaterialOverride
{
    // Records differences from TargetMaterial and applies them to every slot, of every Renderer on this
    // GameObject or below it, that uses TargetMaterial. Layers apply from the widest scope to the narrowest:
    // a parent's component before a child's, and these before a per-slot MaterialSlotOverrideComponent.
    // Each layer starts from the result of the layers before it.
    [AddComponentMenu("Uto/Material Override")]
    public sealed class MaterialOverrideComponent : MaterialOverrideBase
    {
        // Unity only draws the enable checkbox for a component that defines one of its event methods.
        // A disabled component is ignored by the build and the preview.
        private void Start()
        {
        }
    }
}
