#if UNITY_EDITOR
// Property groups for lilToon, parsed from the installed lilMaterialProperties.cs.
// When lilToon is not installed or its source cannot be parsed there are no groups, and the
// transfer window lists every property in its "Other" group.
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;

public static class LilToonPropertyGroups
{
    private const int MinParsedProperties = 200;

    private static readonly Regex DeclRegex = new Regex(
        @"new\s+lilMaterialProperty\(\s*""(_\w+)""\s*(?:,\s*(?:true|false)\s*)?((?:,\s*PropertyBlock\.\w+\s*)*)\)",
        RegexOptions.Compiled);
    private static readonly Regex BlockRegex = new Regex(@"PropertyBlock\.(\w+)", RegexOptions.Compiled);

    private static bool _loaded;
    private static string[] _groupOrder = new string[0];
    private static Dictionary<string, string[]> _children = new Dictionary<string, string[]>();
    private static Dictionary<string, (string group, string sub, int order)> _map =
        new Dictionary<string, (string group, string sub, int order)>();

    public static string[] GroupOrder
    {
        get { Load(); return _groupOrder; }
    }

    public static string[] ChildrenOf(string group)
    {
        Load();
        return _children.TryGetValue(group, out var c) ? c : new string[0];
    }

    public static bool TryGet(string propertyName, out string group, out string sub, out int order)
    {
        Load();
        if (_map.TryGetValue(propertyName, out var e))
        {
            group = e.group; sub = e.sub; order = e.order;
            return true;
        }
        group = null; sub = null; order = -1;
        return false;
    }

    private static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        TryParse();
    }

    private static string FindSource()
    {
        foreach (var guid in AssetDatabase.FindAssets("lilMaterialProperties t:MonoScript"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith("/lilMaterialProperties.cs")) continue;
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            if (script != null) return script.text;
        }
        return null;
    }

    private static bool TryParse()
    {
        string text;
        try { text = FindSource(); }
        catch { return false; }
        if (string.IsNullOrEmpty(text)) return false;

        var decls = new List<(string name, List<string> blocks)>();
        foreach (Match m in DeclRegex.Matches(text))
        {
            var blocks = new List<string>();
            foreach (Match b in BlockRegex.Matches(m.Groups[2].Value)) blocks.Add(b.Groups[1].Value);
            decls.Add((m.Groups[1].Value, blocks));
        }
        if (decls.Count < MinParsedProperties) return false;

        // A block that is never the first tag of any property is a sub-block of the block before it.
        var firstTags = new HashSet<string>();
        foreach (var d in decls)
            if (d.blocks.Count > 0) firstTags.Add(d.blocks[0]);

        var order = new List<string>();
        var children = new Dictionary<string, List<string>>();
        var map = new Dictionary<string, (string, string, int)>(decls.Count);
        for (int i = 0; i < decls.Count; i++)
        {
            var blocks = decls[i].blocks;
            if (blocks.Count == 0) continue;
            string group = blocks[0];
            string sub = null;
            if (blocks.Count > 1 && !firstTags.Contains(blocks[1]))
            {
                sub = blocks[1];
                if (!children.TryGetValue(group, out var list)) children[group] = list = new List<string>();
                if (!list.Contains(sub)) list.Add(sub);
            }
            if (!order.Contains(group)) order.Add(group);
            map[decls[i].name] = (group, sub, i);
        }

        _groupOrder = order.ToArray();
        _children = new Dictionary<string, string[]>();
        foreach (var kv in children) _children[kv.Key] = kv.Value.ToArray();
        _map = map;
        return true;
    }
}
#endif
