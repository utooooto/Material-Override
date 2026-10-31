using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using nadena.dev.ndmf.preview;
using UnityEditor;
using UnityEngine;

namespace Uto.MaterialOverride
{
    // Shows the result of the Material Override components on the scene's renderers without building.
    // The layers and how they apply (MaterialOverrideChain, MaterialOverrideUtil.Apply) are shared with the
    // build, so the preview matches it.
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
            var renderers = new HashSet<Renderer>();

            foreach (var c in context.GetComponentsByType<MaterialSlotOverrideComponent>())
            {
                var r = context.GetComponent<Renderer>(c.gameObject);
                if (IsSupported(r)) renderers.Add(r);
            }

            foreach (var g in context.GetComponentsByType<MaterialOverrideComponent>())
            {
                foreach (var r in context.GetComponentsInChildren<Renderer>(g.gameObject, true))
                {
                    if (IsSupported(r)) renderers.Add(r);
                }
            }

            return renderers.Select(RenderGroup.For).ToImmutableList();
        }

        // NDMF rejects every group of a filter once one of them holds another kind of renderer
        // (a particle system's, for example), so the preview leaves those out. The build still covers them.
        private static bool IsSupported(Renderer r)
        {
            return r is MeshRenderer || r is SkinnedMeshRenderer;
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

            // Every material created for the layers, including the ones that are only an input of a later layer
            private readonly List<Material> _created = new List<Material>();

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

                // Re-runs when the renderer is moved under another parent
                var path = context.ObservePath(_target.transform).ToList();
                var ancestors = MaterialOverrideChain.GetAncestors(path,
                    go => context.GetComponents<MaterialOverrideComponent>(go));
                foreach (var g in ancestors) ObserveRecorded(context, g);

                // Same validity rules as the build
                var own = new Dictionary<int, MaterialSlotOverrideComponent>();
                foreach (var c in context.GetComponents<MaterialSlotOverrideComponent>(_target.gameObject))
                {
                    ObserveRecorded(context, c);
                    if (!c.enabled) continue;
                    if (c.SlotIndex < 0 || c.SlotIndex >= current.Length) continue;
                    if (c.TargetMaterial == null || current[c.SlotIndex] == null) continue;
                    if (MaterialOverrideUtil.IsSlotClaimedByOther(c, c.SlotIndex)) continue;
                    own[c.SlotIndex] = c;
                }

                var resolver = new MaterialOverrideResolver(CreateLayerMaterial);

                for (int slot = 0; slot < current.Length; slot++)
                {
                    var original = current[slot];
                    if (original == null) continue;
                    context.Observe(original);

                    var layers = MaterialOverrideChain.Filter(ancestors, original);
                    if (own.TryGetValue(slot, out var slotComponent)) layers.Add(slotComponent);
                    if (layers.Count == 0) continue;

                    _materials.Add((slot, resolver.Resolve(original, layers)));
                }

                return Task.FromResult<IRenderFilterNode>(this);
            }

            // Any recorded change re-runs Refresh (EditorJsonUtility also covers object references)
            private static void ObserveRecorded(ComputeContext context, MaterialOverrideBase c)
            {
                context.Observe(c, x => EditorJsonUtility.ToJson(x));
                context.Observe(c, x => x.enabled);
            }

            private Material CreateLayerMaterial(Material input, MaterialOverrideBase layer)
            {
                var preview = new Material(input)
                {
                    name = input.name,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                MaterialOverrideUtil.Apply(preview, layer);
                _created.Add(preview);
                return preview;
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
                foreach (var material in _created)
                {
                    if (material != null) Object.DestroyImmediate(material);
                }
                _created.Clear();
                _materials.Clear();
            }
        }
    }
}
