using UnityEditor;
using UnityEngine;

namespace Uto.MaterialOverride
{
    // The target is the material in one slot of the renderer on the same GameObject
    [CustomEditor(typeof(MaterialSlotOverrideComponent))]
    internal class MaterialSlotOverrideComponentEditor : MaterialOverrideBaseEditor
    {
        private MaterialSlotOverrideComponent _slotComponent;

        protected override string NoTargetKey => "insp_no_target";

        protected override bool IsInactive =>
            _slotComponent.TargetMaterial != null
            && MaterialOverrideUtil.IsSlotClaimedByOther(_slotComponent, _slotComponent.SlotIndex);

        protected override void OnEnable()
        {
            _slotComponent = (MaterialSlotOverrideComponent)target;
            base.OnEnable();
        }

        protected override bool DrawTargetPicker()
        {
            var renderer = _slotComponent.GetComponent<Renderer>();
            if (renderer == null)
            {
                EditorGUILayout.HelpBox(T("insp_no_renderer"), MessageType.Warning);
                return false;
            }

            var mats = renderer.sharedMaterials;
            if (mats.Length == 0)
            {
                EditorGUILayout.HelpBox(T("insp_no_target"), MessageType.Warning);
                return false;
            }

            DrawSlot(mats);
            return true;
        }

        // The recorded baseline, read-only like in the transfer window; it is changed through the slot
        protected override void DrawBaseline()
        {
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField(T("target"), _t.TargetMaterial, typeof(Material), false);
        }

        private void DrawSlot(Material[] mats)
        {
            var c = _slotComponent;
            int slot = Mathf.Clamp(c.SlotIndex, 0, mats.Length - 1);
            var options = new string[mats.Length];
            for (int i = 0; i < mats.Length; i++)
            {
                options[i] = MaterialOverrideLang.F("insp_slot_option", i, mats[i] != null ? mats[i].name : T("none"));
                if (MaterialOverrideUtil.IsSlotClaimedByOther(c, i)) options[i] += T("insp_slot_in_use");
            }

            EditorGUI.BeginChangeCheck();
            int picked = EditorGUILayout.Popup(T("insp_slot"), slot, options);
            bool changed = EditorGUI.EndChangeCheck();

            // A slot already modified by another Material Slot Override on this object cannot be chosen
            if (changed && MaterialOverrideUtil.IsSlotClaimedByOther(c, picked)) changed = false;

            // First time: record the first slot that has a material and is not claimed yet
            if (c.TargetMaterial == null && !changed)
            {
                int fallback = -1;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    if (fallback < 0) fallback = i;
                    if (MaterialOverrideUtil.IsSlotClaimedByOther(c, i)) continue;
                    picked = i;
                    changed = true;
                    break;
                }

                // No free slot: the popup already shows a slot as selected, so record it and let the
                // conflict message below report it, as if the user had chosen that slot.
                if (!changed && fallback >= 0)
                {
                    picked = fallback;
                    changed = true;
                }
            }

            if (changed)
            {
                Undo.RecordObject(c, "Material Slot Override");
                c.SlotIndex = picked;
                Record(mats[picked]);
                return;
            }

            if (c.TargetMaterial != null && MaterialOverrideUtil.IsSlotClaimedByOther(c, c.SlotIndex))
                EditorGUILayout.HelpBox(T("insp_slot_conflict"), MessageType.Error);

            // The slot holds another material now: follow it. The overrides are values, so they are kept.
            if (c.TargetMaterial != null && mats[slot] != null && mats[slot] != c.TargetMaterial)
            {
                Undo.RecordObject(c, "Material Slot Override");
                Retarget(mats[slot]);
            }
        }

        // Makes the material in the slot the recorded baseline, keeping the overrides
        private void Retarget(Material target)
        {
            _t.TargetMaterial = target;
            EditorUtility.SetDirty(_t);
            PrefabUtility.RecordPrefabInstancePropertyModifications(_t);
            RebuildRecording();
        }

        // Starts over with a new baseline (a different slot)
        private void Record(Material target)
        {
            _t.TargetMaterial = target;
            ClearOverrides();
            RebuildRecording();
        }
    }
}
