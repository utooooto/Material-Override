using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Uto.MaterialOverride
{
    // The inspector shared by every override component: the material the differences are recorded against,
    // what the layers before this one did, the resulting material (edited with the normal material
    // inspector), the list of recorded differences, and the Advanced section. What picks the target
    // (a slot, or a material) is up to the subclass.
    internal abstract class MaterialOverrideBaseEditor : Editor
    {
        protected MaterialOverrideBase _t;

        // Throwaway material = base + recorded overrides, drawn with the normal material inspector.
        // Edits made there are written back to the component as differences from the base, which is the
        // target with the layers before this component applied.
        private Material _recording;
        private RecordingMaterialEditor _materialEditor;
        private int _signature;
        private bool _showUpstream = true;
        private bool _showOverrides;
        private bool _showAdvanced;

        // Shown when no target is recorded yet
        protected abstract string NoTargetKey { get; }

        // Draws what picks the target; returns false when the rest cannot be shown
        protected abstract bool DrawTargetPicker();

        // Whether another component already takes this one's place, which makes it inactive
        protected virtual bool IsInactive => false;

        protected virtual void DrawBaseline()
        {
        }

        protected virtual void OnEnable()
        {
            _t = (MaterialOverrideBase)target;
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

        protected void RebuildRecording()
        {
            DestroyRecording();
            _signature = Signature();
            if (_t.TargetMaterial == null) return;

            _recording = MaterialOverrideUtil.CreateRecordingMaterial(_t);
            _materialEditor = (RecordingMaterialEditor)CreateEditor(_recording, typeof(RecordingMaterialEditor));
            _materialEditor.ShaderChanged += OnShaderChanged;
        }

        // Changes the component itself or a layer before it make the recording stale
        private int Signature()
        {
            int h = EditorUtility.GetDirtyCount(_t);
            foreach (var u in MaterialOverrideChain.GetUpstream(_t))
                h = h * 31 + EditorUtility.GetDirtyCount(u);
            return h;
        }

        // The shader dropdown in the material header does not raise a change in OnInspectorGUI
        private void OnShaderChanged()
        {
            WriteRecordingToComponent();
        }

        private void WriteRecordingToComponent()
        {
            MaterialOverrideUtil.WriteDiff(_t, _recording);
            // Our own write must not trigger a rebuild of the editor that is being used
            _signature = Signature();
        }

        protected static string T(string key)
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

        // The target, what came before, the resulting material (the part that is edited most), then the
        // overrides. Transferring from another material is a secondary way to fill the overrides, so it is
        // just a button at the end; the source is chosen in the window it opens.
        private void DrawMain()
        {
            if (!DrawTargetPicker()) return;

            if (_t.TargetMaterial == null)
            {
                EditorGUILayout.HelpBox(T(NoTargetKey), MessageType.Info);
                return;
            }

            DrawBaseline();

            // Something else changed the component (transfer window, prefab revert, a layer before it, ...)
            if (_recording == null || Signature() != _signature)
                RebuildRecording();

            DrawUpstream();
            DrawMissingOverrides();
            DrawRecording();
            DrawOverrides();
            DrawAdvanced();
        }

        // What the layers before this one do to the material; the result below starts from there
        private void DrawUpstream()
        {
            var upstream = MaterialOverrideChain.GetUpstream(_t);
            if (upstream.Count == 0) return;

            EditorGUILayout.Space();
            _showUpstream = EditorGUILayout.Foldout(_showUpstream,
                MaterialOverrideLang.F("insp_upstream", upstream.Count), true);
            if (!_showUpstream) return;

            EditorGUI.indentLevel++;
            foreach (var u in upstream)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(u.gameObject.name,
                        MaterialOverrideUtil.JoinNames(MaterialOverrideUtil.GetOverrideNames(u), 4));
                    if (GUILayout.Button(new GUIContent("→", T("insp_upstream_select_tip")), GUILayout.Width(22)))
                        Selection.activeGameObject = u.gameObject;
                }
            }
            EditorGUI.indentLevel--;
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

            bool inactive = IsInactive;
            bool canBake = !inactive && MaterialOverrideUtil.CountOverrides(_t) > 0;
            bool editable = MaterialOverrideUtil.IsEditableAsset(_t.TargetMaterial);

            // Writing into the original would leave out the layers before this one
            bool hasUpstream = MaterialOverrideChain.GetUpstream(_t).Count > 0;

            using (new EditorGUI.DisabledScope(inactive))
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
            else if (hasUpstream)
                EditorGUILayout.HelpBox(T("insp_bake_upstream"), MessageType.Info);

            using (new EditorGUI.DisabledScope(!canBake || !editable || hasUpstream))
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

        protected void ClearOverrides()
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

        // One line per recorded difference; the button removes it, which puts the base's value back
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
