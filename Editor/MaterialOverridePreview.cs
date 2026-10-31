using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using nadena.dev.ndmf.preview;
using UnityEditor;
using UnityEngine;

namespace Uto.MaterialOverride
{
    // Shows the result of MaterialSlotOverrideComponent components on the scene's renderers without building.
    // The build-time logic (MaterialOverrideUtil.Apply) is shared, so the preview matches the build.
    internal class MaterialOverridePreview : IRenderFilter
    {
        private static readonly TogglablePreviewNode EnableNode = TogglablePreviewNode.Create(
            () => "Material Override",
            qualifiedName: "uto.material-override/Preview",
            true
        );

        public IEnumerable<TogglablePreviewNode> GetPreviewControlNodes()
        {
            yield return EnableNode;
        }

        public bool IsEnabled(ComputeContext context)
        {
            return context.Observe(EnableNode.IsEnabled);
        }

        public ImmutableList<RenderGroup> GetTargetGroups(ComputeContext context)
        {
            return context.GetComponentsByType<MaterialSlotOverrideComponent>()
                .Select(c => context.GetComponent<Renderer>(c.gameObject))
                .Where(r => r != null)
                .Distinct()
                .Select(RenderGroup.For)
                .ToImmutableList();
        }

        public Task<IRenderFilterNode> Instantiate(RenderGroup group, IEnumerable<(Renderer, Renderer)> proxyPairs,
            ComputeContext context)
        {
            var node = new Node(proxyPairs.First().Item1);
            return node.Refresh(null, context, 0);
        }

        private class Node : IRenderFilterNode
        {
            private readonly Renderer _target;
            private readonly List<(int slot, Material material)> _materials = new List<(int, Material)>();

            public RenderAspects WhatChanged { get; private set; } = RenderAspects.Material;

            public Node(Renderer target)
            {
                _target = target;
            }

            // Refresh is only called after something observed here changed, so the materials are simply rebuilt.
            public Task<IRenderFilterNode> Refresh(IEnumerable<(Renderer, Renderer)> proxyPairs,
                ComputeContext context, RenderAspects updatedAspects)
            {
                DestroyMaterials();
                WhatChanged = RenderAspects.Material;

                if (_target == null) return Task.FromResult<IRenderFilterNode>(this);

                var current = context.Observe(_target, r => r.sharedMaterials,
                    (a, b) => a.SequenceEqual(b));

                foreach (var c in context.GetComponents<MaterialSlotOverrideComponent>(_target.gameObject))
                {
                    // Any recorded change re-runs this method (EditorJsonUtility also covers object references)
                    context.Observe(c, x => EditorJsonUtility.ToJson(x));
                    if (!context.Observe(c, x => x.enabled)) continue;

                    // Same validity rules as the build
                    if (c.SlotIndex < 0 || c.SlotIndex >= current.Length) continue;
                    var baseMaterial = current[c.SlotIndex];
                    if (c.TargetMaterial == null || baseMaterial == null) continue;
                    if (MaterialOverrideUtil.IsSlotClaimedByOther(c, c.SlotIndex)) continue;
                    context.Observe(baseMaterial);

                    var preview = new Material(baseMaterial)
                    {
                        name = baseMaterial.name,
                        hideFlags = HideFlags.HideAndDontSave,
                    };
                    MaterialOverrideUtil.Apply(preview, c);
                    _materials.Add((c.SlotIndex, preview));
                }

                return Task.FromResult<IRenderFilterNode>(this);
            }

            public void OnFrame(Renderer original, Renderer proxy)
            {
                if (original == null || proxy == null) return;

                var mats = proxy.sharedMaterials;
                foreach (var (slot, material) in _materials)
                {
                    if (slot < mats.Length) mats[slot] = material;
                }
                proxy.sharedMaterials = mats;
            }

            public void Dispose()
            {
                DestroyMaterials();
            }

            private void DestroyMaterials()
            {
                foreach (var (_, material) in _materials)
                {
                    if (material != null) Object.DestroyImmediate(material);
                }
                _materials.Clear();
            }
        }
    }
}
