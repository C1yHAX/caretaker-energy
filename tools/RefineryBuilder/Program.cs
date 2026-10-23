using Newtonsoft.Json.Linq;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.Kismet.Bytecode;
using UAssetAPI.Kismet.Bytecode.Expressions;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;
using UAssetAPI.Unversioned;
using static AssetTools;

// Clones the stock Diesel Refinery into a separate stationary petrol refinery.
// Every edit is explicit and checked against the expected source value; any
// unexpected source shape aborts the build instead of guessing.
if (args.Length != 5)
{
    Console.Error.WriteLine("RefineryBuilder <mapping.usmap> <spec.json> <stock-legacy-root> <output-root> <report-dir>");
    return 2;
}
try { return Builder.Run(args[0], args[1], args[2], args[3], args[4]); }
catch (Exception ex) { Console.Error.WriteLine("BUILD FAILED: " + ex); return 1; }

static class Builder
{
    const string OldItem = "DA_Item_Module_DieselGenerator";
    const string OldBlueprint = "BP_Module_DieselGenerator";
    const string OldSkill = "DA_Skill_Engineering_Oil_Generator";
    const string OldTable = "ST_Modules_Generator";
    const string ItemDir = "/Game/Data/Assets/Modules/";
    const string BlueprintDir = "/Game/Blueprints/Modules/Generators/";
    const string SkillDir = "/Game/Data/Assets/Skill/Modules/";
    const string TableDir = "/Game/LocalizationStringTables/";
    const string SocketDir = "/Game/Data/Assets/ModuleSockets/";

    static string sourceRoot;
    static JObject spec;

    static string N(string key) => (string)spec["names"][key];
    static string S(string group, string key) => (string)spec["strings"][group][key];

    public static int Run(string map, string specPath, string source, string output, string reports)
    {
        MapPath = map; sourceRoot = source; OutputRoot = output; ReportDir = reports;
        spec = JObject.Parse(File.ReadAllText(specPath));
        Require(!Directory.Exists(OutputRoot) || !Directory.EnumerateFileSystemEntries(OutputRoot).Any(), "Output root must be empty: " + OutputRoot);
        Directory.CreateDirectory(OutputRoot);
        Directory.CreateDirectory(ReportDir);

        BuildStringTable();
        BuildItem();
        BuildBlueprint();
        BuildSkill();

        WriteReport();
        Console.WriteLine("Built " + Report.Count + " assets into " + OutputRoot);
        return 0;
    }

    // ---------------------------------------------------------------- assets

    static void BuildStringTable()
    {
        var log = new List<string>();
        var a = Load(Content("LocalizationStringTables/" + OldTable + ".uasset"));
        Rename(a, TableDir + OldTable, TableDir + N("stringTable"), log);
        Rename(a, OldTable, N("stringTable"), log);

        var st = (StringTableExport)a.Exports.Single();
        Require(st.Table.TableNamespace.Value == "ST_Modules_Generators", "Unexpected string table namespace");
        Require(st.Table.ContainsKey(new FString("Module_Name_DieselGenerator")), "Source table lacks the Diesel Refinery name");
        st.Table.TableNamespace = new FString((string)spec["strings"]["namespace"]);
        st.Table.Clear();
        foreach (var group in new[] { "name", "description", "type" })
        {
            st.Table.Add(new FString(S(group, "key")), new FString(S(group, "text")));
            log.Add($"entry {S(group, "key")} = {S(group, "text")}");
        }
        Save(a, "LocalizationStringTables/" + N("stringTable") + ".uasset", log);
    }

    static void BuildItem()
    {
        var log = new List<string>();
        var a = Load(Content("Data/Assets/Modules/" + OldItem + ".uasset"));
        RenameShared(a, log, item: true, blueprint: true, table: true);
        Rename(a, "EModuleResourceType::Diesel", "EModuleResourceType::Petrol", log);

        var root = ExportNamed(a, N("item")).Data;
        SetFloat(root, "Weight", 51.8f, (float)spec["item"]["weight"], log);
        SetFloat(root, "CraftTime", 20f, (float)spec["item"]["craftTime"], log);

        var expectedParts = new Dictionary<string, int> { ["DA_Part_ConnectorPorts"] = 2, ["DA_Part_AlloyFrame"] = 2, ["DA_Part_CompressionPump"] = 1, ["DA_Part_FilteringSystem"] = 1 };
        var desiredParts = ((JObject)spec["item"]["components"]).Properties().ToDictionary(p => p.Name, p => (int)p.Value);
        var components = Prop<MapPropertyData>(root, "Components").Value;
        Require(components.Count == expectedParts.Count && desiredParts.Count == expectedParts.Count, "Recipe must keep the four stock part types");
        foreach (var entry in components)
        {
            var part = ((ObjectPropertyData)entry.Key).Value.ToImport(a).ObjectName.ToString();
            var count = (IntPropertyData)entry.Value;
            Require(expectedParts.TryGetValue(part, out var before) && count.Value == before, "Unexpected recipe entry " + part);
            Require(desiredParts.ContainsKey(part), "Spec lacks recipe part " + part);
            log.Add($"Components[{part}] {count.Value} -> {desiredParts[part]}");
            count.Value = desiredParts[part];
        }

        SetTextKey(Prop<TextPropertyData>(root, "Name"), TableDir + N("stringTable") + "." + N("stringTable"), "Module_Name_DieselGenerator", S("name", "key"), log);

        var description = ExportNamed(a, "VoyagePropertyDescription_0").Data;
        var type = Prop<TextPropertyData>(description, "Type");
        Require(type.TableId.ToString() == TableDir + "ST_DescriptionTemplates.ST_DescriptionTemplates" && type.Value.Value == "ModuleTypeDieselProducer", "Unexpected module type text");
        // The renamed table is already a CreateBeforeSerialization import of this item.
        type.TableId = new FName(a, TableDir + N("stringTable") + "." + N("stringTable"));
        type.Value = new FString(S("type", "key"));
        log.Add($"Description.Type -> {type.TableId}:{type.Value.Value}");
        // Stock stores Tier only for larger variants, directly after Type (e.g. DA_Item_Module_ElectricGenerator).
        Require(!description.Any(p => p.Name.ToString() == "Tier"), "Stock item already stores a description Tier");
        // Unversioned property names are never serialized; the reader also binds them as dummies,
        // so the name map and NamesReferencedFromExportDataCount stay exactly as in stock.
        var tier = new IntPropertyData(FName.DefineDummy(a, "Tier")) { Value = (int)spec["item"]["descriptionTier"] };
        description.Insert(description.IndexOf(type) + 1, tier);
        log.Add($"Description.Tier (default 1) -> {tier.Value}");
        SetTextKey(Prop<TextPropertyData>(description, "ShortDescription"), TableDir + N("stringTable") + "." + N("stringTable"), "Module_Desc_DieselGenerator", S("description", "key"), log);

        var conversion = Prop<StructPropertyData>(ExportNamed(a, "VoyagePropertyModuleResourceConversion_0").Data, "Conversion").Value;
        SetResourceMap(Prop<MapPropertyData>(conversion, "Inputs"), new() { ["EModuleResourceType::Electricity"] = 2000f, ["EModuleResourceType::Custom1"] = 5000f }, (JObject)spec["conversion"]["inputs"], log, "Inputs");
        // The single Diesel output key was renamed to Petrol in the name map above.
        SetResourceMap(Prop<MapPropertyData>(conversion, "Outputs"), new() { ["EModuleResourceType::Petrol"] = 20000f }, (JObject)spec["conversion"]["outputs"], log, "Outputs");
        Require(Prop<FloatPropertyData>(conversion, "Speed").Value == 1f && Prop<BoolPropertyData>(conversion, "bRequired").Value, "Unexpected conversion speed/requirement");

        Save(a, "Data/Assets/Modules/" + N("item") + ".uasset", log);
    }

    static void BuildBlueprint()
    {
        var log = new List<string>();
        var a = Load(Content("Blueprints/Modules/Generators/" + OldBlueprint + ".uasset"));
        RenameShared(a, log, item: true, blueprint: true, table: true);
        Rename(a, "Default__" + OldBlueprint + "_C", "Default__" + N("blueprint") + "_C", log);
        Rename(a, "ExecuteUbergraph_" + OldBlueprint, "ExecuteUbergraph_" + N("blueprint"), log);
        Rename(a, SocketDir + "DA_Socket_LiquidData_Diesel", SocketDir + "DA_Socket_LiquidData_Petrol", log);
        Rename(a, "DA_Socket_LiquidData_Diesel", "DA_Socket_LiquidData_Petrol", log);
        Rename(a, "Diesel Generator", N("moduleName"), log);

        var socket = ExportNamed(a, "VoyageModuleSocketView3_GEN_VARIABLE").Data;
        var asset = Prop<SoftObjectPropertyData>(socket, "DataAsset").Value.AssetPath;
        Require(asset.PackageName.ToString() == SocketDir + "DA_Socket_LiquidData_Petrol" && asset.AssetName.ToString() == "DA_Socket_LiquidData_Petrol", "Socket data asset was not redirected");
        var port = Prop<StructPropertyData>(socket, "Port").Value;
        Require(Prop<EnumPropertyData>(port, "Type").Value.ToString() == "ST_Output", "Liquid socket is not an output");
        var resource = Prop<EnumPropertyData>(port, "ResourceType");
        Require(resource.Value.ToString() == "Diesel", "Unexpected liquid socket resource");
        Require(a.ContainsNameReference(new FString("Petrol")), "Blueprint name map lacks Petrol");
        resource.Value = new FName(a, "Petrol");
        log.Add("VoyageModuleSocketView3.Port.ResourceType Diesel -> Petrol");

        var inventory = ExportNamed(a, "VoyageInventoryWeightLimited_GEN_VARIABLE").Data;
        SetTextKey(Prop<TextPropertyData>(inventory, "Name"), TableDir + N("stringTable") + "." + N("stringTable"), "Module_Name_DieselGenerator", S("name", "key"), log);

        var module = ExportNamed(a, "ModuleComponent").Data;
        var itemAsset = Prop<ObjectPropertyData>(module, "ItemAsset").Value.ToImport(a);
        Require(itemAsset.ObjectName.ToString() == N("item"), "ModuleComponent.ItemAsset was not redirected");

        RecolorLiquid(a, log);
        var organicInventory = ExportNamed(a, "VoyageInventoryWeightLimited_GEN_VARIABLE");
        var allow = Prop<BoolPropertyData>(organicInventory.Data, "bAllowBeyondWeightLimit");
        Require(!allow.Value, "Expected capped organic inventory in stock refinery");
        allow.Value = true;
        log.Add("Organic intake bAllowBeyondWeightLimit false -> true");
        Save(a, "Blueprints/Modules/Generators/" + N("blueprint") + ".uasset", log);
    }

    static void BuildSkill()
    {
        var log = new List<string>();
        var a = Load(Content("Data/Assets/Skill/Modules/" + OldSkill + ".uasset"));
        Rename(a, SkillDir + OldSkill, SkillDir + N("skill"), log);
        Rename(a, OldSkill, N("skill"), log);
        RenameShared(a, log, item: true, blueprint: false, table: true);

        var root = ExportNamed(a, N("skill")).Data;
        var unlock = Prop<StructPropertyData>(root, "Unlock").Value;
        Require(Prop<EnumPropertyData>(unlock, "UnlockMethod").Value.ToString() == "Tier", "Skill is not tier-unlocked");
        SetInt(unlock, "Requirement", 4, (int)spec["skill"]["tier"], log);
        SetInt(unlock, "Cost", 2, (int)spec["skill"]["cost"], log);
        var items = Prop<ArrayPropertyData>(root, "Items").Value;
        Require(items.Length == 1 && ((ObjectPropertyData)items[0]).Value.ToImport(a).ObjectName.ToString() == N("item"), "Skill does not unlock the new item");
        var table = TableDir + N("stringTable") + "." + N("stringTable");
        SetTextKey(Prop<TextPropertyData>(root, "Name"), table, "Module_Name_DieselGenerator", S("name", "key"), log);
        SetTextKey(Prop<TextPropertyData>(root, "Description"), table, "Module_Desc_DieselGenerator", S("description", "key"), log);
        Save(a, "Data/Assets/Skill/Modules/" + N("skill") + ".uasset", log);
    }

    // --------------------------------------------------------------- helpers

    static void RenameShared(UAsset a, List<string> log, bool item, bool blueprint, bool table)
    {
        if (item)
        {
            Rename(a, ItemDir + OldItem, ItemDir + N("item"), log);
            Rename(a, OldItem, N("item"), log);
        }
        if (blueprint)
        {
            Rename(a, BlueprintDir + OldBlueprint, BlueprintDir + N("blueprint"), log);
            Rename(a, OldBlueprint + "_C", N("blueprint") + "_C", log);
        }
        if (table)
        {
            Rename(a, TableDir + OldTable, TableDir + N("stringTable"), log);
            Rename(a, TableDir + OldTable + "." + OldTable, TableDir + N("stringTable") + "." + N("stringTable"), log);
            Rename(a, OldTable, N("stringTable"), log);
        }
    }

    static void RecolorLiquid(UAsset a, List<string> log)
    {
        var dieselEnd = new[] { 0.010417000390589237f, 0.0030380000825971365f, 0.00043399998685345054f, 1f };
        var function = (FunctionExport)a.Exports.Single(e => e.ObjectName.ToString() == "UpdateLiquid");
        var code = function.ScriptBytecode;
        var done = new HashSet<string>();
        for (int i = 0; i + 1 < code.Length; i++)
        {
            var lerp = Walk(code[i]).OfType<EX_CallMath>().SingleOrDefault(c => c.StackNode.IsImport() && c.StackNode.ToImport(a).ObjectName.ToString() == "LinearColorLerp");
            if (lerp == null) continue;
            var parameter = Walk(code[i + 1]).OfType<EX_NameConst>().Select(n => n.Value.ToString()).Single(n => n == "Liquid_Color" || n == "Foam_Color");
            var end = (EX_StructConst)lerp.Parameters[1];
            var floats = end.Value.Cast<EX_FloatConst>().ToArray();
            Require(floats.Select(f => f.Value).SequenceEqual(dieselEnd), "Unexpected stock liquid color for " + parameter);
            var target = ((JArray)spec["liquidColors"][parameter]).Select(v => (float)v).ToArray();
            for (int c = 0; c < 4; c++) floats[c].Value = target[c];
            Require(done.Add(parameter), "Duplicate liquid color target " + parameter);
            log.Add($"UpdateLiquid {parameter} end color -> [{string.Join(", ", target.Select(f => f.ToString("R")))}]");
        }
        Require(done.SetEquals(new[] { "Liquid_Color", "Foam_Color" }), "Liquid colors not found");
    }

    static void SetResourceMap(MapPropertyData map, Dictionary<string, float> expected, JObject desired, List<string> log, string label)
    {
        var want = desired.Properties().ToDictionary(p => p.Name, p => (float)p.Value);
        Require(map.Value.Count == expected.Count && want.Count == expected.Count && want.Keys.All(expected.ContainsKey), label + " keys must match the stock layout");
        foreach (var entry in map.Value)
        {
            var key = ((EnumPropertyData)entry.Key).Value.ToString();
            var value = (FloatPropertyData)entry.Value;
            Require(expected.TryGetValue(key, out var before) && value.Value == before, $"Unexpected {label} entry {key}={value.Value}");
            log.Add($"{label}[{key}] {value.Value} -> {want[key]}");
            value.Value = want[key];
        }
    }

    static string Content(string relative) => Path.Combine(sourceRoot, "Voyage", "Content", relative);
}
