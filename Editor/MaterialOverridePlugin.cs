using System;
using System.Collections.Generic;
using nadena.dev.ndmf;
using nadena.dev.ndmf.localization;
using Shinano.MaterialOverride;
using UnityEngine;
using Object = UnityEngine.Object;

[assembly: ExportsPlugin(typeof(MaterialOverridePlugin))]

namespace Shinano.MaterialOverride
{
    // Runs in Generating, before the Transforming phase where Modular Avatar (Material Setter / Swap,
    // toggles that generate animations) reads the renderer's materials. From its point of view the slot
    // then simply holds the final material, as if it had been assigned by hand.
    internal class MaterialOverridePlugin : Plugin<MaterialOverridePlugin>
    {
        public override string QualifiedName => "shinano.material-override";
        public override string DisplayName => "Material Override";

        private static readonly Localizer L = new Localizer("en-US",
            () => new List<(string, Func<string, string>)> { ("en-US", MaterialOverrideLang.TryGet) });

        protected override void Configure()
        {
            InPhase(BuildPhase.Generating)
                .Run("Apply Material Override", Apply)
                .PreviewingWith(new MaterialOverridePreview());
        }

        private static void Apply(BuildContext ctx)
        {
            var all = ctx.AvatarRootTransform.GetComponentsInChildren<MaterialOverrideComponent>(true);
            foreach (var c in all)
            {
                if (!c.enabled) continue;
                ErrorReport.WithContextObject(c, () => ApplyOne(ctx, c));
            }

            // Destroyed only after all are processed: the conflict check looks at the other components
            foreach (var c in all)
                Object.DestroyImmediate(c);
        }

        private static void ApplyOne(BuildContext ctx, MaterialOverrideComponent c)
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
        }
    }
}
