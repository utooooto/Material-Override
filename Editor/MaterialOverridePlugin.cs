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
            var all = ctx.AvatarRootTransform.GetComponentsInChildren<MaterialOverrideComponent>(true);
            var replaced = ctx.GetState<List<Replacement>>();
            foreach (var c in all)
            {
                if (!c.enabled) continue;
                ErrorReport.WithContextObject(c, () => ApplyOne(ctx, c, replaced));
            }

            // Destroyed only after all are processed: the conflict check looks at the other components
            foreach (var c in all)
                Object.DestroyImmediate(c);
        }

        private static void ApplyOne(BuildContext ctx, MaterialOverrideComponent c, List<Replacement> replaced)
        {
            var renderer = c.GetComponent<Renderer>();
            if (renderer == null)
            {
                ErrorReport.ReportError(L, ErrorSeverity.NonFatal, "err_no_renderer", c);
                return;
            }

            var mats = renderer.sharedMaterials;
            if (c.SlotIndex < 0 || c.SlotIndex >= mats.Length)
            {
                ErrorReport.ReportError(L, ErrorSeverity.NonFatal, "err_slot_out_of_range",
                    c.SlotIndex.ToString(), mats.Length.ToString());
                return;
            }

            var current = mats[c.SlotIndex];
            if (c.TargetMaterial == null || current == null)
            {
                ErrorReport.ReportError(L, ErrorSeverity.NonFatal, "err_slot_empty", c.SlotIndex.ToString());
                return;
            }

            // An earlier component on this object already modifies the slot; this one does not take effect
            if (MaterialOverrideUtil.IsSlotClaimedByOther(c, c.SlotIndex))
            {
                ErrorReport.ReportError(L, ErrorSeverity.Error, "err_slot_conflict", c.SlotIndex.ToString());
                return;
            }

            // The overrides are values, so they follow whatever material the slot holds
            var clone = Object.Instantiate(current);
            clone.name = current.name;
            MaterialOverrideUtil.Apply(clone, c);

            var missing = MaterialOverrideUtil.GetMissingProperties(clone, c);
            if (missing.Count > 0)
            {
                ErrorReport.ReportError(L, ErrorSeverity.NonFatal, "err_missing_props",
                    c.SlotIndex.ToString(), missing.Count.ToString(), MaterialOverrideUtil.JoinNames(missing));
            }

            ObjectRegistry.RegisterReplacedObject(current, clone);
            ctx.AssetSaver.SaveAsset(clone);

            mats[c.SlotIndex] = clone;
            renderer.sharedMaterials = mats;

            replaced.Add(new Replacement(renderer, c.SlotIndex, current, clone));
        }

        // A keyframe that sets this renderer's slot to the original material means "the material in this
        // slot", so it is pointed at the transferred one, as if that material had been assigned by hand.
        // Only that renderer and slot are touched; other users of the original material keep it.
        private static void RewriteAnimations(BuildContext ctx)
        {
            var replaced = ctx.GetState<List<Replacement>>();
            if (replaced.Count == 0) return;

            var asc = ctx.Extension<AnimatorServicesContext>();

            // Animation paths are the ones the animator context knows, which may differ from the current
            // hierarchy if something moved the renderer.
            var targets = new List<(string path, string property, Object original, Object clone)>();
            foreach (var r in replaced)
            {
                if (r.Renderer == null) continue;
                targets.Add((asc.ObjectPathRemapper.GetVirtualPathForObject(r.Renderer.gameObject),
                    $"m_Materials.Array.data[{r.Slot}]", r.Original, r.Clone));
            }

            asc.AnimationIndex.RewriteObjectCurves((binding, obj) =>
            {
                if (!binding.isPPtrCurve) return obj;
                foreach (var t in targets)
                {
                    if (t.original == obj && t.path == binding.path && t.property == binding.propertyName)
                        return t.clone;
                }
                return obj;
            });
        }

        private readonly struct Replacement
        {
            public readonly Renderer Renderer;
            public readonly int Slot;
            public readonly Object Original;
            public readonly Object Clone;

            public Replacement(Renderer renderer, int slot, Object original, Object clone)
            {
                Renderer = renderer;
                Slot = slot;
                Original = original;
                Clone = clone;
            }
        }
    }
}
