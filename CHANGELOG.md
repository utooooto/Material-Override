# Changelog

## [0.1.0] - 2026-10-31

First release.

### Added
- Material Slot Override: records the differences of one material slot of one renderer and applies them at build time.
- Material Override: records differences for a target material and applies them to every slot below the GameObject that uses it.
- Layering: Material Override components on parents, then on children, then the slot's own Material Slot Override, each built on the previous result.
- Animation support: keyframes that point at the original material follow the final material, including Modular Avatar Material Swap targets for Material Override.
- NDMF preview for mesh and skinned mesh renderers.
- Transfer window, bake as new material and bake into original material.
- English and Traditional Chinese interface.
