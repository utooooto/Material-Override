using System;
using System.Collections.Generic;
using UnityEngine;

namespace Uto.MaterialOverride
{
    // Which components modify a material, and in what order. Shared by the build, the preview and the
    // inspector so that all three agree on the layers.
    //
    // The layers of one slot are: the Material Override components on the renderer's GameObject and its parents that
    // target the slot's original material (parents first), then the slot's own MaterialSlotOverrideComponent.
    // Those are matched against the original material, so an earlier layer never changes
    // which components apply.
    internal static class MaterialOverrideChain
    {
        // The path from a transform up to the root, leaf first (same order as NDMF's ObservePath)
        public static List<Transform> PathOf(Transform t)
        {
            var path = new List<Transform>();
            for (; t != null; t = t.parent) path.Add(t);
            return path;
        }

        // Every Material Override component along the path, widest scope first. Components on one GameObject keep
        // their order. Nothing is filtered, so callers can tell where a component sits in the order.
        public static List<MaterialOverrideComponent> GetAncestors(IEnumerable<Transform> pathLeafFirst,
            Func<GameObject, MaterialOverrideComponent[]> getComponents)
        {
            var levels = new List<MaterialOverrideComponent[]>();
            foreach (var t in pathLeafFirst) levels.Add(getComponents(t.gameObject));
            levels.Reverse();

            var result = new List<MaterialOverrideComponent>();
            foreach (var level in levels) result.AddRange(level);
            return result;
        }

        public static List<MaterialOverrideComponent> GetAncestors(Transform t)
        {
            return GetAncestors(PathOf(t), go => go.GetComponents<MaterialOverrideComponent>());
        }

        // The ones that actually modify this material
        public static List<MaterialOverrideBase> Filter(IEnumerable<MaterialOverrideComponent> ancestors,
            Material original)
        {
            var result = new List<MaterialOverrideBase>();
            if (original == null) return result;
            foreach (var g in ancestors)
            {
                if (g.enabled && g.TargetMaterial == original) result.Add(g);
            }
            return result;
        }

        // The layers that come before c, as seen by the inspector. A disabled component still has an
        // upstream, so it is cut by position rather than by looking for c in the filtered list.
        public static List<MaterialOverrideBase> GetUpstream(MaterialOverrideBase c)
        {
            var result = new List<MaterialOverrideBase>();
            if (c.TargetMaterial == null) return result;

            foreach (var g in GetAncestors(c.transform))
            {
                if (g == c) break;
                if (g.enabled && g.TargetMaterial == c.TargetMaterial) result.Add(g);
            }
            return result;
        }
    }

    // Applies a list of layers to a material, creating one result per (input, layer) pair. Slots that reach
    // a layer with the same input share its result, and a slot with an extra layer or different upstream
    // gets a separate material.
    internal sealed class MaterialOverrideResolver
    {
        private readonly Dictionary<(Material, MaterialOverrideBase), Material> _cache =
            new Dictionary<(Material, MaterialOverrideBase), Material>();

        private readonly Func<Material, MaterialOverrideBase, Material> _create;

        // create(input, layer) returns the material that results from applying the layer to the input
        public MaterialOverrideResolver(Func<Material, MaterialOverrideBase, Material> create)
        {
            _create = create;
        }

        public Material Resolve(Material input, IEnumerable<MaterialOverrideBase> layers)
        {
            var current = input;
            foreach (var layer in layers)
            {
                if (!_cache.TryGetValue((current, layer), out var next))
                {
                    next = _create(current, layer);
                    _cache[(current, layer)] = next;
                }
                current = next;
            }
            return current;
        }
    }
}
