// Material Override - language support
// UI strings for MaterialOverrideWindow, the component inspector and NDMF build messages.
//
// The strings live in Localization/<language code>.po next to this file (gettext format, imported by Unity
// as a LocalizationAsset, so editing a file needs no recompile). To add a language, add a <code>.po file
// with the same keys. A key missing from a language falls back to en-US.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;

public static class MaterialOverrideLang
{
    private const string PrefKey = "MaterialOverride.LanguageCode";
    private const string FallbackCode = "en-US";

    private static string _folder;
    private static string[] _codes;
    private static string[] _names;
    private static readonly Dictionary<string, UnityEngine.LocalizationAsset> Assets = new Dictionary<string, UnityEngine.LocalizationAsset>();

    // Display names of the available languages, in the same order as the popup index of Current
    public static string[] Names
    {
        get
        {
            Scan();
            return _names;
        }
    }

    // Index into Names
    public static int Current
    {
        get
        {
            Scan();
            int i = System.Array.IndexOf(_codes, EditorPrefs.GetString(PrefKey, FallbackCode));
            return i >= 0 ? i : 0;
        }
        set
        {
            Scan();
            EditorPrefs.SetString(PrefKey, _codes[UnityEngine.Mathf.Clamp(value, 0, _codes.Length - 1)]);
        }
    }

    public static string T(string key)
    {
        return TryGet(key) ?? key;
    }

    public static string F(string key, params object[] args)
    {
        return string.Format(T(key), args);
    }

    // Returns null when the key is missing (the NDMF Localizer expects that).
    public static string TryGet(string key)
    {
        Scan();
        string code = _codes[Current];
        string v = Lookup(code, key);
        if (v == null && code != FallbackCode) v = Lookup(FallbackCode, key);
        return v;
    }

    private static string Lookup(string code, string key)
    {
        var asset = Load(code);
        if (asset == null) return null;
        string v = asset.GetLocalizedString(key);
        return string.IsNullOrEmpty(v) || v == key ? null : v;
    }

    // A destroyed asset (the file was re-imported) is loaded again
    private static UnityEngine.LocalizationAsset Load(string code)
    {
        if (Assets.TryGetValue(code, out var asset) && asset != null) return asset;
        asset = AssetDatabase.LoadAssetAtPath<UnityEngine.LocalizationAsset>(_folder + "/" + code + ".po");
        Assets[code] = asset;
        return asset;
    }

    // Finds the Localization folder next to this script and lists the languages in it
    private static void Scan()
    {
        if (_codes != null && Directory.Exists(_folder)) return;

        _folder = null;
        foreach (var guid in AssetDatabase.FindAssets("MaterialOverrideLang t:MonoScript"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileName(path) != "MaterialOverrideLang.cs") continue;
            _folder = Path.GetDirectoryName(path).Replace('\\', '/') + "/Localization";
            break;
        }

        var codes = new List<string>();
        if (_folder != null && Directory.Exists(_folder))
        {
            codes = Directory.GetFiles(_folder, "*.po")
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(c => c == FallbackCode ? 0 : 1).ThenBy(c => c)
                .ToList();
        }
        if (codes.Count == 0) codes.Add(FallbackCode);

        _codes = codes.ToArray();
        _names = _codes.Select(DisplayName).ToArray();
        Assets.Clear();
    }

    private static string DisplayName(string code)
    {
        if (code == "zh-Hant") return "繁體中文";
        if (code == "zh-Hans") return "简体中文";
        try { return new CultureInfo(code).NativeName; }
        catch (CultureNotFoundException) { return code; }
    }
}
