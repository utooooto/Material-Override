using UnityEditor;
using UnityEngine;

namespace Uto.MaterialOverride
{
    // The target is a material chosen here; it applies to every slot below this GameObject that uses it
    [CustomEditor(typeof(MaterialOverrideComponent))]
    internal class MaterialOverrideComponentEditor : MaterialOverrideBaseEditor
    {
        protected override string NoTargetKey => "insp_pick_target";

        protected override bool DrawTargetPicker()
        {
            EditorGUI.BeginChangeCheck();
            var picked = (Material)EditorGUILayout.ObjectField(T("target"), _t.TargetMaterial, typeof(Material), false);
            if (EditorGUI.EndChangeCheck())
            {
                // The overrides are values, so they are kept when the target changes
                Undo.RecordObject(_t, "Material Override");
                _t.TargetMaterial = picked;
                EditorUtility.SetDirty(_t);
                PrefabUtility.RecordPrefabInstancePropertyModifications(_t);
                RebuildRecording();
            }

            if (_t.TargetMaterial != null)
                EditorGUILayout.HelpBox(MaterialOverrideLang.F("insp_target_scope", CountSlots()), MessageType.None);
            return true;
        }

        // How many slots below this GameObject currently use the target
        private int CountSlots()
        {
            int count = 0;
            foreach (var r in _t.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var m in r.sharedMaterials)
                {
                    if (m == _t.TargetMaterial) count++;
                }
            }
            return count;
        }
    }
}
