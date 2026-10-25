using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Uto.MaterialOverride
{
    [CustomEditor(typeof(MaterialOverrideComponent))]
    internal class MaterialOverrideComponentEditor : Editor
    {
        private MaterialOverrideComponent _t;

        // Throwaway material = target + recorded overrides, drawn with the normal material inspector.
        // Edits made there are written back to the component as differences from the target.
        private Material _recording;
        private RecordingMaterialEditor _materialEditor;
        private int _dirtyCount;
        private bool _showOverrides;
        private bool _showAdvanced;

        private void OnEnable()
        {
            _t = (MaterialOverrideComponent)target;
            Undo.undoRedoPerformed += OnUndoRedo;
            RebuildRecording();
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            DestroyRecording();
        }

        private void OnUndoRedo()
        {
            RebuildRecording();
            Repaint();
        }

        private void DestroyRecording()
        {
            if (_materialEditor != null)
            {
                _materialEditor.ShaderChanged -= OnShaderChanged;
                DestroyImmediate(_materialEditor);
            }
            if (_recording != null) DestroyImmediate(_recording);
            _materialEditor = null;
            _recording = null;
        }

        private void RebuildRecording()
        {
            DestroyRecording();
            _dirtyCount = EditorUtility.GetDirtyCount(_t);
            if (_t.TargetMaterial == null) return;

            _recording = MaterialOverrideUtil.CreateRecordingMaterial(_t);
            _materialEditor = (RecordingMaterialEditor)CreateEditor(_recording, typeof(RecordingMaterialEditor));
            _materialEditor.ShaderChanged += OnShaderChanged;
        }

        // The shader dropdown in the material header does not raise a change in OnInspectorGUI
        private void OnShaderChanged()
        {
            WriteRecordingToComponent();
        }

        private void WriteRecordingToComponent()
        {
            MaterialOverrideUtil.WriteDiff(_t, _t.TargetMaterial, _recording);
            // Our own write must not trigger a rebuild of the editor that is being used
            _dirtyCount = EditorUtility.GetDirtyCount(_t);
        }

        private static string T(string key)
        {
            return MaterialOverrideLang.T(key);
        }

        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            int lang = EditorGUILayout.Popup(T("language"), MaterialOverrideLang.Current, MaterialOverrideLang.Names);
            if (EditorGUI.EndChangeCheck()) MaterialOverrideLang.Current = lang;

            DrawMain();
        }

        // Slot, the resulting material (the part that is edited most), then the overrides.
        // Transferring from another material is a secondary way to fill the overrides, so it is just a button
        // at the end; the source is chosen in the window it opens.
        private void DrawMain()
        {
            var renderer = _t.GetComponent<Renderer>();
            if (renderer == null)
            {
                EditorGUILayout.HelpBox(T("insp_no_renderer"), MessageType.Warning);
                return;
            }

            var mats = renderer.sharedMaterials;
            if (mats.Length == 0)
            {
                EditorGUILayout.HelpBox(T("insp_no_target"), MessageType.Warning);
                return;
            }

            DrawSlot(mats);

            if (_t.TargetMaterial == null)
            {
                EditorGUILayout.HelpBox(T("insp_no_target"), MessageType.Info);
                return;
            }

            // The recorded baseline, read-only like in the transfer window; it is changed through the slot
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField(T("target"), _t.TargetMaterial, typeof(Material), false);

            // Something else changed the component (transfer window, prefab revert, ...)
            if (_recording == null || EditorUtility.GetDirtyCount(_t) != _dirtyCount)
                RebuildRecording();

            DrawMissingOverrides();
            DrawRecording();
            DrawOverrides();
            DrawAdvanced();
        }

        // Overrides for properties the current shader lacks (e.g. after the slot got another material).
        // They are skipped at build time; the warning can be ignored.
        private void DrawMissingOverrides()
        {
            if (_recording == null) return;

            var missing = MaterialOverrideUtil.GetMissingProperties(_recording, _t);
            if (missing.Count == 0) return;

            EditorGUILayout.HelpBox(MaterialOverrideLang.F("insp_missing_props", missing.Count,
                MaterialOverrideUtil.JoinNames(missing)), MessageType.Warning);
        }

        // Transfer and bake buttons, safest first. The bake buttons need something to bake.
        private void DrawAdvanced()
        {
            EditorGUILayout.Space();
            _showAdvanced = EditorGUILayout.Foldout(_showAdvanced, T("insp_advanced"), true);
            if (!_showAdvanced) return;

            bool conflict = MaterialOverrideUtil.IsSlotClaimedByOther(_t, _t.SlotIndex);
            bool canBake = !conflict && MaterialOverrideUtil.CountOverrides(_t) > 0;
            bool editable = MaterialOverrideUtil.IsEditableAsset(_t.TargetMaterial);

            using (new EditorGUI.DisabledScope(conflict))
            {
                if (GUILayout.Button(T("insp_transfer"), GUILayout.Height(24)))
                    MaterialOverrideWindow.Open(_t);
            }

            using (new EditorGUI.DisabledScope(!canBake))
            {
                if (GUILayout.Button(new GUIContent(T("insp_bake_new"), T("insp_bake_new_tip")), GUILayout.Height(24)))
                {
                    var created = MaterialOverrideUtil.BakeAsNew(_t, T("insp_bake_new_title"));
                    if (created != null) EditorGUIUtility.PingObject(created);
                    GUIUtility.ExitGUI();
                }
            }

            if (!editable)
                EditorGUILayout.HelpBox(T("insp_bake_readonly"), MessageType.Info);

            using (new EditorGUI.DisabledScope(!canBake || !editable))
            {
                if (GUILayout.Button(new GUIContent(T("insp_bake_orig"), T("insp_bake_orig_tip")), GUILayout.Height(24)))
                    BakeIntoOriginal();
            }
        }

        private void BakeIntoOriginal()
        {
            // A component that exists in the prefab asset cannot be removed from an instance
            bool removable = PrefabUtility.GetCorrespondingObjectFromSource(_t) == null;
            string name = _t.TargetMaterial.name;
            string message = MaterialOverrideLang.F(removable ? "insp_bake_dlg_msg" : "insp_bake_dlg_msg_keep", name);
            if (!EditorUtility.DisplayDialog(T("insp_bake_dlg_title"), message, T("insp_bake_ok"), T("insp_bake_cancel")))
                return;

            MaterialOverrideUtil.BakeInto(_t.TargetMaterial, _t);
            if (removable)
            {
                Undo.DestroyObjectImmediate(_t);
                GUIUtility.ExitGUI();
            }
            RebuildRecording();
        }

        private void DrawSlot(Material[] mats)
        {
            int slot = Mathf.Clamp(_t.SlotIndex, 0, mats.Length - 1);
            var options = new string[mats.Length];
            for (int i = 0; i < mats.Length; i++)
            {
                options[i] = MaterialOverrideLang.F("insp_slot_option", i, mats[i] != null ? mats[i].name : T("none"));
                if (MaterialOverrideUtil.IsSlotClaimedByOther(_t, i)) options[i] += T("insp_slot_in_use");
            }

            EditorGUI.BeginChangeCheck();
            int picked = EditorGUILayout.Popup(T("insp_slot"), slot, options);
            bool changed = EditorGUI.EndChangeCheck();

            // A slot already modified by another Material Override on this object cannot be chosen
            if (changed && MaterialOverrideUtil.IsSlotClaimedByOther(_t, picked)) changed = false;

            // First time: record the first slot that has a material and is not claimed yet
            if (_t.TargetMaterial == null && !changed)
            {
                int fallback = -1;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    if (fallback < 0) fallback = i;
                    if (MaterialOverrideUtil.IsSlotClaimedByOther(_t, i)) continue;
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
                Undo.RecordObject(_t, "Material Override");
                _t.SlotIndex = picked;
                Record(mats[picked]);
                return;
            }

            if (_t.TargetMaterial != null && MaterialOverrideUtil.IsSlotClaimedByOther(_t, _t.SlotIndex))
                EditorGUILayout.HelpBox(T("insp_slot_conflict"), MessageType.Error);

            // The slot holds another material now: follow it. The overrides are values, so they are kept.
            if (_t.TargetMaterial != null && mats[slot] != null && mats[slot] != _t.TargetMaterial)
            {
                Undo.RecordObject(_t, "Material Override");
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

        private void ClearOverrides()
        {
            _t.IsOverrideShader = false;
            _t.OverrideShader = null;
            _t.IsOverrideRenderQueue = false;
            _t.OverrideProperties.Clear();
            _t.EnabledKeywords.Clear();
            _t.DisabledKeywords.Clear();
            EditorUtility.SetDirty(_t);
            PrefabUtility.RecordPrefabInstancePropertyModifications(_t);
        }

        private void DrawRecording()
        {
            if (_materialEditor == null) return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(T("insp_result"), EditorStyles.boldLabel);

            using (var change = new EditorGUI.ChangeCheckScope())
            {
                _materialEditor.DrawHeader();
                _materialEditor.OnInspectorGUI();
                if (change.changed) WriteRecordingToComponent();
            }
        }

        // One line per recorded difference; the button removes it, which puts the target's value back
        private void DrawOverrides()
        {
            EditorGUILayout.Space();
            _showOverrides = EditorGUILayout.Foldout(_showOverrides,
                MaterialOverrideLang.F("insp_overrides", MaterialOverrideUtil.CountOverrides(_t)), true);
            if (!_showOverrides) return;

            Action remove = null;
            EditorGUI.indentLevel++;

            if (_t.IsOverrideShader && OverrideRow(T("row_shader"), _t.OverrideShader != null ? _t.OverrideShader.name : T("none")))
                remove = () => { _t.IsOverrideShader = false; _t.OverrideShader = null; };

            if (_t.IsOverrideRenderQueue && OverrideRow(T("row_queue"), _t.OverrideRenderQueue.ToString()))
                remove = () => _t.IsOverrideRenderQueue = false;

            for (int i = 0; i < _t.OverrideProperties.Count; i++)
            {
                int index = i;
                var p = _t.OverrideProperties[i];
                if (OverrideRow(p.PropertyName, FormatValue(p)))
                    remove = () => _t.OverrideProperties.RemoveAt(index);
            }

            for (int i = 0; i < _t.EnabledKeywords.Count; i++)
            {
                int index = i;
                if (OverrideRow(_t.EnabledKeywords[i], T("insp_kw_on")))
                    remove = () => _t.EnabledKeywords.RemoveAt(index);
            }

            for (int i = 0; i < _t.DisabledKeywords.Count; i++)
            {
                int index = i;
                if (OverrideRow(_t.DisabledKeywords[i], T("insp_kw_off")))
                    remove = () => _t.DisabledKeywords.RemoveAt(index);
            }

            EditorGUI.indentLevel--;

            if (remove != null)
            {
                Undo.RecordObject(_t, "Material Override");
                remove();
                EditorUtility.SetDirty(_t);
                PrefabUtility.RecordPrefabInstancePropertyModifications(_t);
                RebuildRecording();
                GUIUtility.ExitGUI();
            }

            if (GUILayout.Button(T("insp_clear")))
            {
                Undo.RecordObject(_t, "Material Override");
                ClearOverrides();
                RebuildRecording();
            }
        }

        private static bool OverrideRow(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, value);
                return GUILayout.Button(new GUIContent("−", T("insp_revert_tip")), GUILayout.Width(22));
            }
        }

        private static string FormatValue(MaterialPropertyOverride p)
        {
            switch (p.PropertyType)
            {
                case ShaderPropertyType.Texture: return p.TextureValue != null ? p.TextureValue.name : T("none");
                case ShaderPropertyType.Color: return "#" + ColorUtility.ToHtmlStringRGBA(p.ColorValue);
                case ShaderPropertyType.Vector: return p.VectorValue.ToString();
                case ShaderPropertyType.Int: return p.IntValue.ToString();
                default: return p.FloatValue.ToString("0.###");
            }
        }
    }

    // MaterialEditor.OnShaderChanged is protected, so expose it as an event
    internal class RecordingMaterialEditor : MaterialEditor
    {
        public event Action ShaderChanged;

        protected override void OnShaderChanged()
        {
            base.OnShaderChanged();
            ShaderChanged?.Invoke();
        }
    }
}
