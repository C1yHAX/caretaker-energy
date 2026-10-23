using System.Reflection;
using Newtonsoft.Json;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.Kismet.Bytecode;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;
using UAssetAPI.Unversioned;

// Shared, fail-closed editing helpers for the Voyage addon builders.
// Assets are read with the UE5.6 legacy header layout written by retoc and the
// UE5.8 payload of the game; every write is re-read and must be lossless.
static class AssetTools
{
    public static string MapPath, OutputRoot, ReportDir;
    public static readonly List<object> Report = new();

    public static UAsset Load(string path)
    {
        var asset = new UAsset(EngineVersion.VER_UE5_6, new Usmap(MapPath)) { FilePath = path, PayloadEngineVersionOverride = EngineVersion.VER_UE5_8 };
        using (var reader = asset.PathToReader(path)) asset.Read(reader);
        Require(!asset.Exports.Any(e => e is RawExport), "RawExport in " + path);
        Require(asset.VerifyBinaryEquality(), "Roundtrip is not lossless: " + path);
        return asset;
    }

    public static void Save(UAsset a, string relative, List<string> log)
    {
        // The summary keeps the saved package name as a plain string outside the name map.
        var packageName = "/Game/" + relative[..^".uasset".Length];
        Require(a.FolderName?.Value != null && a.FolderName.Value.StartsWith("/Game/") && a.FolderName.Value != packageName, "Unexpected source package name " + a.FolderName);
        log.Add($"FolderName {a.FolderName.Value} -> {packageName}");
        a.FolderName = new FString(packageName);
        var output = Path.Combine(OutputRoot, "Voyage", "Content", relative);
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        a.Write(output);
        var reread = Load(output);
        File.WriteAllText(Path.Combine(ReportDir, Path.GetFileNameWithoutExtension(output) + ".json"), reread.SerializeJson(true));
        Report.Add(new { output, exports = reread.Exports.Count, rawExports = 0, binaryEquality = true, unresolvedDependencies = reread.OtherAssetsFailedToAccess.Select(x => x.ToString()).ToArray(), changes = log });
        Console.WriteLine($"Wrote {relative}: {log.Count} changes, exports={reread.Exports.Count}");
    }

    public static void WriteReport() =>
        File.WriteAllText(Path.Combine(ReportDir, "build-report.json"), JsonConvert.SerializeObject(Report, Formatting.Indented));

    public static void Rename(UAsset a, string from, string to, List<string> log)
    {
        Require(a.ContainsNameReference(new FString(from)), "Name not found: " + from);
        Require(!a.ContainsNameReference(new FString(to)), "Target name already present: " + to);
        a.SetNameReference(a.SearchNameReference(new FString(from)), new FString(to));
        log.Add($"name {from} -> {to}");
    }

    // Renames every name-map entry containing `from`; the expected count guards against surprises.
    public static void RenameContaining(UAsset a, string from, string to, int expected, List<string> log)
    {
        var hits = a.GetNameMapIndexList().Select(n => n.Value).Where(n => n.Contains(from, StringComparison.Ordinal)).ToList();
        Require(hits.Count == expected, $"Expected {expected} names containing {from}, found {hits.Count}: {string.Join(", ", hits)}");
        foreach (var name in hits) Rename(a, name, name.Replace(from, to, StringComparison.Ordinal), log);
    }

    // A reparented Blueprint is written against its new parent's class name. The parent clone
    // keeps the stock property layout, so the stock parent's loaded schema is registered under
    // the new name as well (lookups are by name, optionally prefixed with the module path).
    public static void AliasBlueprintSchemas(UAsset a, string from, string to, List<string> log)
    {
        var loaded = a.Mappings.Schemas.Where(kv => kv.Key.Contains(from, StringComparison.Ordinal)).ToList();
        Require(loaded.Count > 0, "No schema loaded for parent " + from);
        foreach (var (key, schema) in loaded)
        {
            var alias = key.Replace(from, to, StringComparison.Ordinal);
            Require(!a.Mappings.Schemas.ContainsKey(alias), "Schema alias already present: " + alias);
            a.Mappings.Schemas[alias] = schema;
            log.Add($"schema alias {alias} = {key}");
        }
    }

    public static NormalExport ExportNamed(UAsset a, string name) => (NormalExport)a.Exports.Single(e => e.ObjectName.ToString() == name);

    public static T Prop<T>(IEnumerable<PropertyData> data, string name) where T : PropertyData => (T)data.Single(p => p.Name.ToString() == name);

    public static void SetFloat(List<PropertyData> data, string name, float before, float after, List<string> log)
    {
        var p = Prop<FloatPropertyData>(data, name);
        Require(p.Value == before, $"Unexpected {name}={p.Value}");
        p.Value = after;
        log.Add($"{name} {before} -> {after}");
    }

    public static void SetInt(List<PropertyData> data, string name, int before, int after, List<string> log)
    {
        var p = Prop<IntPropertyData>(data, name);
        Require(p.Value == before, $"Unexpected {name}={p.Value}");
        p.Value = after;
        log.Add($"{name} {before} -> {after}");
    }

    // Unversioned struct enums are serialized as numbers resolved through the mappings,
    // so the value is bound as a dummy name exactly like the reader does.
    public static void SetEnum(List<PropertyData> data, string name, string before, string after, List<string> log, string label)
    {
        var p = Prop<EnumPropertyData>(data, name);
        Require(p.Value?.ToString() == before, $"Unexpected {label}.{name}={p.Value}");
        p.Value = FName.DefineDummy(p.Value.Asset, after);
        log.Add($"{label}.{name} {before} -> {after}");
    }

    // Inserts a new unversioned enum member after `anchor`, sharing the anchor's struct ancestry.
    public static void InsertEnumAfter(UAsset a, List<PropertyData> data, string anchor, string name, string value, List<string> log, string label)
    {
        Require(!data.Any(p => p.Name.ToString() == name), $"{label}.{name} is already set");
        var after = data.Single(p => p.Name.ToString() == anchor);
        var added = new EnumPropertyData(FName.DefineDummy(a, name)) { Value = FName.DefineDummy(a, value), Ancestry = after.Ancestry };
        data.Insert(data.IndexOf(after) + 1, added);
        log.Add($"{label}.{name} (default) -> {value}");
    }

    public static void SetTextKey(TextPropertyData text, string table, string oldKey, string newKey, List<string> log)
    {
        Require(text.TableId?.ToString() == table && text.Value?.Value == oldKey, $"Unexpected text {text.Name}: {text.TableId}:{text.Value}");
        text.Value = new FString(newKey);
        log.Add($"{text.Name} -> {table}:{newKey}");
    }

    public static IEnumerable<KismetExpression> Walk(object node)
    {
        if (node is KismetExpression expression)
        {
            yield return expression;
            foreach (var member in expression.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance).Select(f => f.GetValue(expression))
                .Concat(expression.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0 && p.Name == "Value").Select(p => p.GetValue(expression))))
                foreach (var child in Walk(member)) yield return child;
        }
        else if (node is KismetExpression[] list)
            foreach (var entry in list) foreach (var child in Walk(entry)) yield return child;
    }

    public static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
