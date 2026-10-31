using System;
using System.Collections.Generic;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using nadena.dev.ndmf.localization;
using Uto.MaterialOverride;
using UnityEngine;
using Object = UnityEngine.Object;

[assembly: ExportsPlugin(typeof(MaterialOverridePlugin))]

namespace Uto.MaterialOverride
{
    // Runs in Generating, before the Transforming phase where Modular Avatar (Material Setter / Swap,
    // toggles that generate animations) reads the renderer's materials. From its point of view the slot
    // then simply holds the final material, as if it had been assigned by hand.
    internal class MaterialOverridePlugin : Plugin<MaterialOverridePlugin>
    {
        public override string QualifiedName => "uto.material-override";
        public override string DisplayName => "Material Override";

        private static readonly Localizer L = new Localizer("en-US",
            () => new List<(string, Func<string, string>)> { ("en-US", MaterialOverrideLang.TryGet) });

        protected override void Configure()
        {
            InPhase(BuildPhase.Generating)
                .Run("Apply Material Override", Apply)
                .PreviewingWith(new MaterialOverridePreview());

            // Modular Avatar merges its animators (Merge Animator) and generates its reactive animations in
            // Transforming, so animation references can only be fixed up after it.
            InPhase(BuildPhase.Transforming)
                .AfterPlugin("nadena.dev.modular-avatar")
                .WithRequiredExtension(typeof(AnimatorServicesContext), seq => seq
                    .Run("Rewrite animation material references", RewriteAnimations));
        }

        private static void Apply(BuildContext ctx)
        {
            var state = ctx.GetState<OverrideState>();
            var root = ctx.AvatarRootTransform;
            state.Components = root.GetComponentsInChildren<MaterialOverrideBase>(true);

            foreach (var g in root.GetComponentsInChildren<MaterialOverrideComponent>(true))
            {
                if (!g.enabled || g.TargetMaterial != null) continue;
                ErrorReport.WithContextObject(g, () =>
                    ErrorReport.ReportError(L, ErrorSeverity.NonFatal, "err_no_target"));
            }

            // Per-slot components that are valid; they are the last layer of their slot
            var slotComponents = new Dictionary<(Renderer, int), MaterialSlotOverrideComponent>();
            foreach (var c in root.GetComponentsInChildren<MaterialSlotOverrideComponent>(true))
            {
                if (!c.enabled) continue;
                var component = c;
                ErrorReport.WithContextObject(component, () =>
                {
                    var renderer = ValidateSlot(component);
                    if (renderer != null) slotComponents[(renderer, component.SlotIndex)] = component;
                });
            }

            state.Resolver = new MaterialOverrideResolver((input, layer) => CreateLayerMaterial(ctx, input, layer));

            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var ancestors = MaterialOverrideChain.GetAncestors(renderer.transform);
                if (ancestors.Count > 0) state.Ancestors[renderer] = ancestors;

                var mats = renderer.sharedMaterials;
                bool changed = false;
                for (int slot = 0; slot < mats.Length; slot++)
                {
                    var original = mats[slot];
                    if (original == null) continue;

                    var layers = MaterialOverrideChain.Filter(ancestors, original);
                    if (slotComponents.TryGetValue((renderer, slot), out var own)) layers.Add(own);
                    if (layers.Count == 0) continue;

                    var result = state.Resolver.Resolve(original, layers);
                    mats[slot] = result;
                    changed = true;
                    state.Slots[(renderer, slot)] = (original, result);
                }
                if (changed) renderer.sharedMaterials = mats;
            }
        }

        // Returns the renderer when the component can take effect; reports why not otherwise
        private static Renderer ValidateSlot(MaterialSlotOverrideComponent c)
        {
            var renderer = c.GetComponent<Renderer>();
            if (renderer == null)
            {
                ErrorReport.ReportError(L, ErrorSeverity.NonFatal, "err_no_renderer", c);
                return null;
            }

            var mats = renderer.sharedMaterials;
            if (c.SlotIndex < 0 || c.SlotIndex >= mats.Length)
            {
                ErrorReport.ReportError(L, ErrorSeverity.NonFatal, "err_slot_out_of_range",
                    c.SlotIndex.ToString(), mats.Length.ToString());
                return null;
            }

            if (c.TargetMaterial == null || mats[c.SlotIndex] == null)
            {
                ErrorReport.ReportError(L, ErrorSeverity.NonFatal, "err_slot_empty", c.SlotIndex.ToString());
                return null;
            }

            // An earlier component on this object already modifies the slot; this one does not take effect
            if (MaterialOverrideUtil.IsSlotClaimedByOther(c, c.SlotIndex))
            {
                ErrorReport.ReportError(L, ErrorSeverity.Error, "err_slot_conflict", c.SlotIndex.ToString());
                return null;
            }

            return renderer;
        }

        // The overrides are values, so they follow whatever material the layer receives
        private static Material CreateLayerMaterial(BuildContext ctx, Material input, MaterialOverrideBase layer)
        {
            var clone = Object.Instantiate(input);
            clone.name = input.name;
            MaterialOverrideUtil.Apply(clone, layer);

            var missing = MaterialOverrideUtil.GetMissingProperties(clone, layer);
            if (missing.Count > 0)
            {
                ErrorReport.WithContextObject(layer, () =>
                    ErrorReport.ReportError(L, ErrorSeverity.NonFatal, "err_missing_props",
                        input.name, missing.Count.ToString(), MaterialOverrideUtil.JoinNames(missing)));
            }

            ObjectRegistry.RegisterReplacedObject(input, clone);
            ctx.AssetSaver.SaveAsset(clone);
            return clone;
        }

        // A keyframe that sets a slot to a material means "the material in this slot", so it is pointed at the
        // result of the layers, as if that material had been assigned by hand. That covers the slot's own
        // material and, for the Material Override layers, any other material they target (for example the material a
        // Material Swap switches to). Only the renderers under a Material Override component are affected; other
        // users of the original material keep it.
        private static void RewriteAnimations(BuildContext ctx)
        {
            var state = ctx.GetState<OverrideState>();
            try
            {
                if (state.Resolver == null || (state.Slots.Count == 0 && state.Ancestors.Count == 0)) return;

                var asc = ctx.Extension<AnimatorServicesContext>();

                // Animation paths are the ones the animator context knows, which may differ from the current
                // hierarchy if something moved the renderer.
                var renderers = new HashSet<Renderer>(state.Ancestors.Keys);
                foreach (var key in state.Slots.Keys) renderers.Add(key.Item1);

                var byPath = new Dictionary<string, Renderer>();
                foreach (var r in renderers)
                {
                    if (r == null) continue;
                    byPath[asc.ObjectPathRemapper.GetVirtualPathForObject(r.gameObject)] = r;
                }

                asc.AnimationIndex.RewriteObjectCurves((binding, obj) =>
                {
                    if (!binding.isPPtrCurve || !(obj is Material material)) return obj;
                    if (!TryGetSlot(binding.propertyName, out int slot)) return obj;
                    if (!byPath.TryGetValue(binding.path, out var renderer)) return obj;

                    if (state.Slots.TryGetValue((renderer, slot), out var planned) && planned.Original == material)
                        return planned.Result;

                    if (!state.Ancestors.TryGetValue(renderer, out var ancestors)) return obj;
                    var layers = MaterialOverrideChain.Filter(ancestors, material);
                    return layers.Count == 0 ? obj : state.Resolver.Resolve(material, layers);
                });
            }
            finally
            {
                // Kept until here because the layers read the components' recorded values
                foreach (var c in state.Components)
                {
                    if (c != null) Object.DestroyImmediate(c);
                }
            }
        }

        private static bool TryGetSlot(string property, out int slot)
        {
            const string prefix = "m_Materials.Array.data[";
            slot = -1;
            if (!property.StartsWith(prefix) || !property.EndsWith("]")) return false;
            return int.TryParse(property.Substring(prefix.Length, property.Length - prefix.Length - 1), out slot);
        }

        // What the Generating pass decided, for the animation pass
        private sealed class OverrideState
        {
            public MaterialOverrideBase[] Components = Array.Empty<MaterialOverrideBase>();
            public MaterialOverrideResolver Resolver;

            // Slots whose material was replaced, with the material before and after
            public readonly Dictionary<(Renderer, int), (Material Original, Material Result)> Slots =
                new Dictionary<(Renderer, int), (Material Original, Material Result)>();

            // The Material Override components above each renderer, captured here because the hierarchy can change later
            public readonly Dictionary<Renderer, List<MaterialOverrideComponent>> Ancestors =
                new Dictionary<Renderer, List<MaterialOverrideComponent>>();
        }
    }
}
