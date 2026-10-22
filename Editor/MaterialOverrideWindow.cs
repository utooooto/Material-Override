// Material Override
// Selectively transfers properties from a source material to the target slot of a
// MaterialOverrideComponent component (for example, a lilToon preset material bought on BOOTH).
//
// Opened from the component's inspector ("Open Transfer Window"). The target column shows the
// component's current result (original target + recorded overrides). Applying copies the selected
// properties into a temporary material and writes the resulting differences back to the component;
// no material asset is modified. The window then closes.
//
// Layout:
// - Properties are grouped like the lilToon Inspector; groups can be folded.
//   Each row shows the source on the left and the target on the right.
// - Both sides share one scroll area and one set of fold states, so scrolling
//   and expanding/collapsing are always in sync.
// - Rows come from the source shader. The General group holds "Shader" and "Render Queue" rows.
//   If the shaders differ, properties both shaders have transfer as usual; properties the target
//   shader lacks can only be transferred together with the Shader row (the result then uses the
//   source shader).
// - Property remapping (different property names) is not supported yet.

#if UNITY_EDITOR
using System.Collections.Generic;
using Shinano.MaterialOverride;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class MaterialOverrideWindow : EditorWindow
{
    // Shader and RenderQueue are not shader properties; they get their own rows in the General group.
    private enum Kind { Texture, Color, Vector, Number, Shader, RenderQueue }

    private const string OtherGroup = "Other";
    private const string GeneralGroup = "General";

    private class Row
    {
        public string Name;
        public string Label;
        public Kind Kind;
        public bool IsInt;
        public bool Hidden;
        public bool Selected;
        public int Order;
        // False when the target's shader has no property with this name and type. Such a row can
        // only be transferred together with the Shader row (the result then uses the source shader).
        public bool InTarget = true;

        // Recomputed on every OnGUI
        public bool Diff;
        public bool Visible;
    }

    private class Section
    {
        public string Title; // null = the group's own properties (no sub-block)
        public readonly List<Row> Rows = new List<Row>();
    }

    private class Group
    {
        public string Name;
        public readonly List<Section> Sections = new List<Section>();
    }

    [SerializeField] private MaterialOverrideComponent _component;

    private Material _source;
    // Temporary copy of the component's target with its recorded overrides applied; edited by Apply.
    private Material _target;
    private Material _builtSource;
    private Material _builtTarget;

    private readonly List<Row> _rows = new List<Row>();
    private readonly List<Group> _groups = new List<Group>();
    private readonly Dictionary<string, bool> _fold = new Dictionary<string, bool>();

    // Warnings are stored as a language key so they follow the language setting.
    private string _warnKey;
    private bool _shaderDiffers;

    private bool _onlyDiff = true;
    private bool _showHidden;
    private bool _onlyTextures;
    private string _search = "";
    private Vector2 _scroll;

    public static void Open(MaterialOverrideComponent component)
    {
        var window = GetWindow<MaterialOverrideWindow>("Material Override");
        window._component = component;
        window.ResetTarget();
        window.Show();
    }

    private void OnEnable()
    {
        // The temporary material does not survive a domain reload
        ResetTarget();
    }

    private void OnDisable()
    {
        DestroyTarget();
    }

    private void DestroyTarget()
    {
        if (_target != null) DestroyImmediate(_target);
        _target = null;
    }

    private void ResetTarget()
    {
        DestroyTarget();
        _builtSource = null;
        _builtTarget = null;
        if (_component == null || _component.TargetMaterial == null) return;
        _target = MaterialOverrideUtil.CreateRecordingMaterial(_component);
    }

    private static string T(string key)
    {
        return MaterialOverrideLang.T(key);
    }

    private static string F(string key, params object[] args)
    {
        return MaterialOverrideLang.F(key, args);
    }

    private static string KindLabel(Kind kind)
    {
        switch (kind)
        {
            case Kind.Texture: return T("kind_texture");
            case Kind.Color: return T("kind_color");
            case Kind.Vector: return T("kind_vector");
            case Kind.Shader: return T("kind_shader");
            case Kind.RenderQueue: return T("kind_queue");
            default: return T("kind_number");
        }
    }

    // Material.GetXxx logs an error for a property the shader does not have, so every read goes through this.
    private static Texture GetTexture(Material m, string name)
    {
        return m.HasProperty(name) ? m.GetTexture(name) : null;
    }

    private bool ShaderSelected()
    {
        foreach (var r in _rows)
            if (r.Kind == Kind.Shader && r.Selected) return true;
        return false;
    }

    // A row without a counterpart in the target shader only counts when the Shader row is selected too.
    private bool IsEffective(Row r, bool shaderSelected)
    {
        return r.Selected && (r.InTarget || shaderSelected);
    }

    // ---------------------------------------------------------------- build

    private void Rebuild()
    {
        _builtSource = _source;
        _builtTarget = _target;
        _rows.Clear();
        _groups.Clear();
        _fold.Clear();
        _warnKey = null;
        _shaderDiffers = false;

        if (_source == null || _target == null) return;

        // _target is a temporary copy, so compare with the component's target asset
        if (_source == _component.TargetMaterial)
        {
            _warnKey = "same_material";
            return;
        }

        // Rows come from the source shader. Where the target shader lacks a property, the row is
        // marked InTarget = false instead of stopping.
        var shader = _source.shader;
        var targetShader = _target.shader;
        _shaderDiffers = shader != targetShader;

        // Labels of these two rows are localized at draw time (see RowLabel)
        _rows.Add(new Row { Name = "Shader", Kind = Kind.Shader, Order = 0 });
        _rows.Add(new Row { Name = "RenderQueue", Kind = Kind.RenderQueue, Order = 1 });

        int count = shader.GetPropertyCount();
        for (int i = 0; i < count; i++)
        {
            var type = shader.GetPropertyType(i);
            var flags = shader.GetPropertyFlags(i);

            Kind kind;
            bool isInt = false;
            switch (type)
            {
                case ShaderPropertyType.Texture: kind = Kind.Texture; break;
                case ShaderPropertyType.Color: kind = Kind.Color; break;
                case ShaderPropertyType.Vector: kind = Kind.Vector; break;
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range: kind = Kind.Number; break;
                case ShaderPropertyType.Int: kind = Kind.Number; isInt = true; break;
                default: continue;
            }

            if ((flags & ShaderPropertyFlags.NonModifiableTextureData) != 0) continue;

            string propName = shader.GetPropertyName(i);
            var row = new Row
            {
                Name = propName,
                // Property names are shown as-is (not localized)
                Label = propName,
                Kind = kind,
                IsInt = isInt,
                Hidden = (flags & ShaderPropertyFlags.HideInInspector) != 0,
                Order = 100000 + i, // properties missing from the table go last, in shader order
            };

            int targetIndex = _shaderDiffers ? targetShader.FindPropertyIndex(propName) : i;
            row.InTarget = targetIndex >= 0 && targetShader.GetPropertyType(targetIndex) == type;

            // Default selection: texture properties that are set in the source and empty in the
            // target (so textures shipped with the preset are not overwritten). Properties the
            // target shader lacks are never selected by default.
            row.Selected = !row.Hidden && kind == Kind.Texture && row.InTarget
                           && _source.GetTexture(propName) != null
                           && _target.GetTexture(propName) == null;

            _rows.Add(row);
        }

        BuildGroups();

        // Shader / Render Queue go into their own group at the top; opened when the shaders differ
        var general = new Group { Name = GeneralGroup };
        var generalSection = new Section { Title = null };
        foreach (var r in _rows)
            if (r.Kind == Kind.Shader || r.Kind == Kind.RenderQueue) generalSection.Rows.Add(r);
        general.Sections.Add(generalSection);
        _groups.Insert(0, general);
        if (_shaderDiffers) _fold[GeneralGroup] = true;

        // Expand groups that contain selected properties by default
        foreach (var g in _groups)
        {
            foreach (var s in g.Sections)
            {
                foreach (var r in s.Rows)
                {
                    if (!r.Selected) continue;
                    _fold[g.Name] = true;
                }
            }
        }
    }

    private void BuildGroups()
    {
        var byName = new Dictionary<string, Group>();
        var sectionByKey = new Dictionary<string, Section>();

        Group MakeGroup(string name)
        {
            var g = new Group { Name = name };
            g.Sections.Add(new Section { Title = null });
            sectionByKey[name + "/"] = g.Sections[0];
            foreach (var child in LilToonPropertyGroups.ChildrenOf(name))
            {
                var s = new Section { Title = child };
                g.Sections.Add(s);
                sectionByKey[name + "/" + child] = s;
            }
            byName[name] = g;
            _groups.Add(g);
            return g;
        }

        foreach (var name in LilToonPropertyGroups.GroupOrder)
            MakeGroup(name);
        MakeGroup(OtherGroup);

        foreach (var r in _rows)
        {
            if (r.Kind == Kind.Shader || r.Kind == Kind.RenderQueue) continue; // shown in General

            string group = OtherGroup;
            string sub = null;
            if (LilToonPropertyGroups.TryGet(r.Name, out var g, out var s, out var order)
                && byName.ContainsKey(g))
            {
                group = g;
                sub = s;
                r.Order = order;
            }

            if (!sectionByKey.TryGetValue(group + "/" + (sub ?? ""), out var section))
                section = sectionByKey[group + "/"];
            section.Rows.Add(r);
        }

        foreach (var g in _groups)
            foreach (var s in g.Sections)
                s.Rows.Sort((a, b) => a.Order.CompareTo(b.Order));
    }

    // ---------------------------------------------------------------- compare / apply

    private bool IsDifferent(Row r)
    {
        if (r.Kind == Kind.Shader) return _source.shader != _target.shader;
        if (r.Kind == Kind.RenderQueue) return _source.renderQueue != _target.renderQueue;

        // The target has nothing to compare with, so the row counts as a difference
        if (!_target.HasProperty(r.Name)) return true;

        switch (r.Kind)
        {
            case Kind.Texture:
                return _source.GetTexture(r.Name) != _target.GetTexture(r.Name)
                       || _source.GetTextureScale(r.Name) != _target.GetTextureScale(r.Name)
                       || _source.GetTextureOffset(r.Name) != _target.GetTextureOffset(r.Name);
            case Kind.Color:
                return _source.GetColor(r.Name) != _target.GetColor(r.Name);
            case Kind.Vector:
                return _source.GetVector(r.Name) != _target.GetVector(r.Name);
            default:
                return r.IsInt
                    ? _source.GetInteger(r.Name) != _target.GetInteger(r.Name)
                    : !Mathf.Approximately(_source.GetFloat(r.Name), _target.GetFloat(r.Name));
        }
    }

    private void CopyValue(Row r, Material src, Material dst)
    {
        switch (r.Kind)
        {
            case Kind.Shader:
                dst.shader = src.shader;
                break;
            case Kind.RenderQueue:
                dst.renderQueue = src.renderQueue;
                break;
            case Kind.Texture:
                dst.SetTexture(r.Name, src.GetTexture(r.Name));
                dst.SetTextureScale(r.Name, src.GetTextureScale(r.Name));
                dst.SetTextureOffset(r.Name, src.GetTextureOffset(r.Name));
                break;
            case Kind.Color:
                dst.SetColor(r.Name, src.GetColor(r.Name));
                break;
            case Kind.Vector:
                dst.SetVector(r.Name, src.GetVector(r.Name));
                break;
            default:
                if (r.IsInt) dst.SetInteger(r.Name, src.GetInteger(r.Name));
                else dst.SetFloat(r.Name, src.GetFloat(r.Name));
                break;
        }
    }

    private void Apply()
    {
        // Order matters: the shader first (so its properties exist), the render queue last
        // (changing the shader resets it)
        bool shaderSelected = ShaderSelected();
        int done = 0;
        foreach (var pass in new[] { Kind.Shader, Kind.Texture, Kind.RenderQueue })
        {
            foreach (var r in _rows)
            {
                bool inPass = pass == Kind.Texture
                    ? r.Kind != Kind.Shader && r.Kind != Kind.RenderQueue
                    : r.Kind == pass;
                if (!inPass || !IsEffective(r, shaderSelected)) continue;
                CopyValue(r, _source, _target);
                done++;
            }
        }

        // Record original -> result as the component's overrides (nothing is written to any asset)
        MaterialOverrideUtil.WriteDiff(_component, _component.TargetMaterial, _target);

        Debug.Log(F("log_done", done, _component.TargetMaterial.name), _component);
        Close();
    }

    // ---------------------------------------------------------------- GUI

    private void OnGUI()
    {
        EditorGUI.BeginChangeCheck();
        int lang = EditorGUILayout.Popup(T("language"), MaterialOverrideLang.Current, MaterialOverrideLang.Names);
        if (EditorGUI.EndChangeCheck()) MaterialOverrideLang.Current = lang;

        if (_component == null)
        {
            EditorGUILayout.HelpBox(T("win_no_component"), MessageType.Info);
            return;
        }

        if (_target == null && _component.TargetMaterial != null) ResetTarget();

        // The source is chosen here and remembered on the component; the target is the one recorded there
        EditorGUI.BeginChangeCheck();
        var picked = (Material)EditorGUILayout.ObjectField(T("source"), _component.SourceMaterial, typeof(Material), false);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_component, "Material Override");
            _component.SourceMaterial = picked;
            EditorUtility.SetDirty(_component);
            PrefabUtility.RecordPrefabInstancePropertyModifications(_component);
        }
        _source = _component.SourceMaterial;

        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.ObjectField(T("target"), _component.TargetMaterial, typeof(Material), false);

        if (_builtSource != _source || _builtTarget != _target)
            Rebuild();

        if (LilToonPropertyGroups.UsingFallback)
            EditorGUILayout.HelpBox(T("using_fallback"), MessageType.Info);

        if (_source == null || _target == null)
        {
            EditorGUILayout.HelpBox(T(_source == null ? "win_pick_source" : "win_pick_target"), MessageType.Info);
            return;
        }

        if (_warnKey != null)
        {
            EditorGUILayout.HelpBox(T(_warnKey), MessageType.Warning);
            return;
        }

        if (_shaderDiffers)
        {
            EditorGUILayout.HelpBox(F("shader_differs",
                _source.shader != null ? _source.shader.name : T("none"),
                _target.shader != null ? _target.shader.name : T("none")), MessageType.Info);
        }

        RefreshRowState();

        DrawFilters();
        DrawBulkButtons();
        DrawColumnHeader();

        // Source and target share a row, so this single scroll view keeps them in sync
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        int shownGroups = 0;
        foreach (var g in _groups)
        {
            if (!HasVisible(g)) continue;

            DrawGroup(g);
            shownGroups++;
        }
        EditorGUILayout.EndScrollView();

        if (shownGroups == 0)
            EditorGUILayout.HelpBox(T("no_rows"), MessageType.None);

        DrawFooter();
    }

    private void RefreshRowState()
    {
        string s = string.IsNullOrEmpty(_search) ? null : _search.ToLowerInvariant();
        foreach (var r in _rows)
        {
            r.Diff = IsDifferent(r);
            r.Visible = (!_onlyTextures || r.Kind == Kind.Texture)
                        && (!r.Hidden || _showHidden)
                        && (!_onlyDiff || r.Diff)
                        && (s == null || r.Name.ToLowerInvariant().Contains(s));
        }
    }

    private static bool HasVisible(Group g)
    {
        foreach (var s in g.Sections)
            foreach (var r in s.Rows)
                if (r.Visible) return true;
        return false;
    }

    private bool GetFold(string key)
    {
        return _fold.TryGetValue(key, out bool v) && v;
    }

    private void DrawFilters()
    {
        EditorGUILayout.BeginHorizontal();
        _onlyTextures = GUILayout.Toggle(_onlyTextures, T("only_textures"), EditorStyles.miniButton, GUILayout.MinWidth(80));
        _onlyDiff = GUILayout.Toggle(_onlyDiff, T("only_diff"), EditorStyles.miniButton, GUILayout.MinWidth(90));
        _showHidden = GUILayout.Toggle(_showHidden, T("show_hidden"), EditorStyles.miniButton, GUILayout.MinWidth(90));
        EditorGUILayout.EndHorizontal();
        _search = EditorGUILayout.TextField(T("search"), _search);
    }

    private void DrawBulkButtons()
    {
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button(T("select_all_visible"), EditorStyles.miniButton)) SetVisible(true);
        if (GUILayout.Button(T("select_none_visible"), EditorStyles.miniButton)) SetVisible(false);
        if (GUILayout.Button(T("select_empty_tex"), EditorStyles.miniButton)) SelectEmptyTextureTargets();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button(T("expand_all"), EditorStyles.miniButton)) SetAllFolds(true);
        if (GUILayout.Button(T("collapse_all"), EditorStyles.miniButton)) SetAllFolds(false);
        EditorGUILayout.EndHorizontal();
    }

    // A property the target shader lacks can only be selected together with the Shader row (which comes first)
    private bool CanSelect(Row r)
    {
        return r.InTarget || ShaderSelected();
    }

    private void SetVisible(bool value)
    {
        foreach (var r in _rows)
            if (r.Visible && (!value || CanSelect(r))) r.Selected = value;
    }

    private void SelectEmptyTextureTargets()
    {
        foreach (var r in _rows)
        {
            r.Selected = !r.Hidden && r.Kind == Kind.Texture && r.InTarget
                         && GetTexture(_source, r.Name) != null
                         && GetTexture(_target, r.Name) == null;
        }
    }

    private void SetAllFolds(bool open)
    {
        foreach (var g in _groups)
            _fold[g.Name] = open;
    }

    private void DrawColumnHeader()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Space(22);
        GUILayout.Label(T("col_property"), EditorStyles.miniLabel, GUILayout.Width(LabelWidth()));
        GUILayout.Label(T("col_source"), EditorStyles.miniLabel);
        GUILayout.Space(20);
        GUILayout.Label(T("col_target"), EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
    }

    private float LabelWidth()
    {
        return Mathf.Clamp(position.width * 0.28f, 120f, 260f);
    }

    private void DrawGroup(Group g)
    {
        int visible = 0, diff = 0, selected = 0;
        bool shaderSelected = ShaderSelected();
        foreach (var s in g.Sections)
        {
            foreach (var r in s.Rows)
            {
                if (!r.Visible) continue;
                visible++;
                if (r.Diff) diff++;
                if (IsEffective(r, shaderSelected)) selected++;
            }
        }

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        EditorGUILayout.BeginHorizontal();
        bool open = EditorGUILayout.Foldout(GetFold(g.Name),
            F("group_header", g.Name, diff, selected, visible), true);
        _fold[g.Name] = open;
        if (GUILayout.Button(T("group_all"), EditorStyles.miniButton, GUILayout.MinWidth(40))) SetGroup(g, true);
        if (GUILayout.Button(T("group_none"), EditorStyles.miniButton, GUILayout.MinWidth(48))) SetGroup(g, false);
        EditorGUILayout.EndHorizontal();

        if (open)
        {
            foreach (var s in g.Sections)
            {
                bool any = false;
                foreach (var r in s.Rows)
                    if (r.Visible) { any = true; break; }
                if (!any) continue;

                // Sub-blocks are not foldable; they are just separated by a heading
                if (s.Title != null)
                {
                    GUILayout.Space(4);
                    GUILayout.Label(s.Title, EditorStyles.boldLabel);
                }

                foreach (var r in s.Rows)
                    if (r.Visible) DrawRow(r);
            }
        }

        EditorGUILayout.EndVertical();
    }

    private void SetGroup(Group g, bool value)
    {
        foreach (var s in g.Sections)
            foreach (var r in s.Rows)
                if (r.Visible && (!value || CanSelect(r))) r.Selected = value;
    }

    // Property names are shown as-is; only the two special rows are localized
    private static string RowLabel(Row r)
    {
        switch (r.Kind)
        {
            case Kind.Shader: return T("row_shader");
            case Kind.RenderQueue: return T("row_queue");
            default: return r.Label;
        }
    }

    private void DrawRow(Row r)
    {
        // A property the target shader lacks can only be transferred together with the Shader row
        bool unavailable = !r.InTarget && !ShaderSelected();

        EditorGUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(unavailable))
        {
            // Shown unchecked while it cannot be applied; the remembered choice is kept
            bool on = EditorGUILayout.Toggle(r.Selected && !unavailable, GUILayout.Width(18));
            if (!unavailable) r.Selected = on;
        }

        string tip = r.Name + "  [" + KindLabel(r.Kind) + "]";
        if (!r.InTarget) tip += "\n" + T("tip_not_in_target");
        var labelStyle = r.Diff ? EditorStyles.boldLabel : EditorStyles.label;
        GUILayout.Label(new GUIContent(RowLabel(r), tip), labelStyle, GUILayout.Width(LabelWidth()));

        EditorGUI.BeginDisabledGroup(true);
        DrawValue(r, _source);
        EditorGUI.EndDisabledGroup();

        GUILayout.Label("→", GUILayout.Width(16));

        EditorGUI.BeginDisabledGroup(true);
        DrawValue(r, _target);
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawValue(Row r, Material m)
    {
        if (r.Kind == Kind.Shader)
        {
            EditorGUILayout.ObjectField(GUIContent.none, m.shader, typeof(Shader), false);
            return;
        }
        if (r.Kind == Kind.RenderQueue)
        {
            EditorGUILayout.IntField(GUIContent.none, m.renderQueue);
            return;
        }
        if (!m.HasProperty(r.Name))
        {
            GUILayout.Label("—", GUILayout.ExpandWidth(true));
            return;
        }

        switch (r.Kind)
        {
            case Kind.Texture:
                EditorGUILayout.ObjectField(GUIContent.none, m.GetTexture(r.Name), typeof(Texture), false);
                break;
            case Kind.Color:
                EditorGUILayout.ColorField(GUIContent.none, m.GetColor(r.Name), false, true, true);
                break;
            case Kind.Vector:
                EditorGUILayout.Vector4Field(GUIContent.none, m.GetVector(r.Name));
                break;
            default:
                if (r.IsInt) EditorGUILayout.IntField(GUIContent.none, m.GetInteger(r.Name));
                else EditorGUILayout.FloatField(GUIContent.none, m.GetFloat(r.Name));
                break;
        }
    }

    private void DrawFooter()
    {
        // Only rows that will really be written are counted
        bool shaderSelected = ShaderSelected();
        int selected = 0;
        foreach (var r in _rows) if (IsEffective(r, shaderSelected)) selected++;

        EditorGUILayout.Space(4);

        EditorGUI.BeginDisabledGroup(selected == 0);
        if (GUILayout.Button(F("apply", selected), GUILayout.Height(28)))
            Apply();
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.Space(4);
    }
}
#endif
