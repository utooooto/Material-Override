# Material Override

**English** | [繁體中文](README.zh-Hant.md)

Change materials on your VRChat avatar without touching the material assets. You record only what differs
from the original, and the changes are applied when the avatar is built (NDMF).

- **Non-destructive**: no material asset is created or modified. The inspector shows only what you changed, and
  removing the component removes the change. You can still bake the result into a new material asset when you
  want one.
- **Two components**: **Material Override** changes every slot below a GameObject that uses a given material
  (for example one material shared by the body, the clothes and the accessories). **Material Slot Override**
  changes one slot of one renderer.
- **Stackable**: components on parent objects apply first, then those on children, then the slot's own. Each
  layer builds on the previous one, so a child can override its parent but never the other way round.
- **Works with animations and Modular Avatar**: toggles, animations that point at the original material, and
  Modular Avatar's Material Swap all see the final material.
- **Preview before you build**: NDMF's preview shows the result in the scene (mesh and skinned mesh renderers).
- **Any shader**: not tied to lilToon. You can also copy selected properties from another material.
- **Requires** Unity 2022.3 and NDMF 1.13.1 or newer. Compatibility with TexTransTool (TTT) has not been
  verified.

To install, add the VPM repository described under [Installation](#installation) to VCC or ALCOM.

## Quick start

1. Add **Material Slot Override** to a GameObject with a renderer and pick the **Slot**, or add **Material
   Override** to any GameObject and pick the **Target Material**. Both are in the **Uto** section of the Add
   Component menu.
2. Change what you want in the material panel the inspector shows. Only your changes are recorded.
3. Build or upload as usual. To check the result first, look at NDMF's preview in the scene.

## Requirements

- Unity 2022.3
- [NDMF](https://ndmf.nadena.dev/) 1.13.1 or newer
- lilToon is optional. The transfer window works with any shader. When lilToon is installed, it groups the
  properties the way lilToon's material inspector does; otherwise every property is listed under **Other**.

## Installation

Add this VPM repository to VCC (2.1.0 or newer) or ALCOM, then add **Material Override** to your project:

```
https://utooooto.github.io/vpm/index.json
```

In VCC: **Settings** > **Packages** > **Add Repository**, paste the URL, and confirm.

## Components

Both components are in the **Uto** section of the Add Component menu.

### Material Override

Add it to any GameObject and choose a **Target Material**. It applies to every slot, of every renderer on that
GameObject or below it, that uses the target. Edit the result in the material panel the inspector shows; only
what you change is recorded.

Use it for changes that should reach many renderers, for example changing one property of a material used by
the body, the clothes and the accessories.

### Material Slot Override

Add it to a GameObject that has a renderer and choose a **Slot**. It modifies that one slot. Only one Material
Slot Override can modify a given slot; a later one on the same GameObject has no effect and the build reports
an error.

If the slot's material is replaced later, the component follows it and keeps the recorded differences.

## Layers

A slot can be affected by several components. They are applied from the widest scope to the narrowest:

1. Material Override components on the renderer's GameObject and its parents, parents first. Components on
   the same GameObject apply in their component order.
2. The slot's own Material Slot Override.

Each layer starts from the result of the layer before it, so a later layer overrides an earlier one for the
same property. A parent's component affects its children; a child's never affects its parents.

Material Override components match the slot's **original** material, not the output of an earlier layer, so an
earlier layer never changes which components apply.

The inspector shows **Applied before this component**: the layers before this one and what they change. The
material panel starts from that result, and your changes are recorded relative to it.

Slots that reach a layer with the same input share one copy of the resulting material. A slot with an extra
layer, or different layers above it, gets its own copy.

## What can be overridden

Shader, render queue, shader properties (texture with offset and scale, color, vector, float, range, integer)
and shader keywords. Material tags such as `RenderType` are not recorded.

If a recorded property does not exist in the slot's shader, it is skipped. The inspector shows a warning and the
build reports a non-fatal message.

## Animations

When a material is replaced, keyframes that point at the original material would put it back. After Modular
Avatar has run, the build rewrites them:

- A keyframe that sets a slot to that slot's original material is pointed at the slot's final material.
- For Material Override, a keyframe that sets a slot below the component to **another** material that the
  component targets is also pointed at the overridden copy. This covers the material a Modular Avatar Material
  Swap switches to. Material Slot Override layers are not applied to these.

Only renderers under the component are touched; other users of the material keep it.

## Preview

NDMF's preview shows the result in the scene without building. It can be turned off in the preview panel
(**Material Override**). NDMF's preview supports only mesh and skinned mesh renderers, so renderers of other
kinds, such as particle systems, are not shown in the preview. The build still modifies them.

## Advanced

The **Advanced** section of the inspector has:

- **Transfer from Another Material...**: choose a source material and copy selected properties from it into
  the recorded differences. Useful for presets that bring their own textures.
- **Bake as New Material...**: save the result (the layers before this one plus this one) as a new material
  asset. The component stays.
- **Bake into Original Material**: write the differences into the original material asset and remove the
  component. This changes every object that uses that material. It is disabled for materials that cannot be
  edited (inside a model file or a package) and when other layers apply before this one.

## Languages

The interface is available in English and Traditional Chinese (the language is selected at the top of the
inspector). To add a language, add a `<language code>.po` file with the same keys to `Editor/Localization`.
A missing key falls back to English.

## Known limitations

- Compatibility with TTT has not been verified. If both change the same property, the result depends on which
  runs last.
- A texture is compared by reference, so two different assets with identical content count as a difference.
- A property whose type differs between shaders is not checked; setting it uses the type that was recorded.

## Feedback

Report problems and suggestions in [GitHub Issues](https://github.com/utooooto/Material-Override/issues).
Please include your Unity and NDMF versions and, for a build problem, the message from the console.

## License

[MIT](LICENSE).
