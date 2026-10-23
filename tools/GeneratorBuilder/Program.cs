using Newtonsoft.Json.Linq;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;
using static AssetTools;

// Clones the stock petrol electric generators into diesel and methane variants.
// The stock large generator inherits from the portable one; every large clone is
// reparented onto the portable clone of the same fuel so it inherits that fuel
// socket. Every edit is checked against the expected stock value.
if (args.Length != 5)
{
    Console.Error.WriteLine("GeneratorBuilder <mapping.usmap> <spec.json> <stock-legacy-root> <output-root> <report-dir>");
    return 2;
}
try { return Builder.Run(args[0], args[1], args[2], args[3], args[4]); }
catch (Exception ex) { Console.Error.WriteLine("BUILD FAILED: " + ex); return 1; }

record Template(string Blueprint, string Item, string Skill, string ModuleName, string NameKey, string ItemDescKey, string SkillDescKey,
    float Weight, Dictionary<string, int> Recipe, int Tier, int Cost, float Electricity, float CarbonDioxide, float Speed, int OwnNames);

static class Builder
{
    const string BlueprintDir = "/Game/Blueprints/Modules/Generators/";
    const string ItemDir = "/Game/Data/Assets/Modules/";
    const string SkillDir = "/Game/Data/Assets/Skill/Modules/";
    const string TableDir = "/Game/LocalizationStringTables/";
    const string SocketDir = "/Game/Data/Assets/ModuleSockets/";
    const string StockTable = "ST_Modules_Generator";
    const string StockParent = "BP_Module_SmallGenerator";

    static readonly Dictionary<string, Template> Templates = new()
    {
        ["Portable"] = new(StockParent, "DA_Item_Module_PortableElectricGenerator", "DA_Skill_Engineering_Portable_Electric_Generator",
            "Small Electric Generator", "Module_Name_PortableElectricGenerator", "Module_Desc_ElectricGenerator", "Module_Desc_PortableElectricGenerator",
            8.5f, new() { ["DA_Part_ConnectorPorts"] = 2, ["DA_Part_AlloyFrame"] = 1, ["DA_Part_CoolingRadiator"] = 1, ["DA_Part_CompressionPump"] = 1 },
            5, 2, 10f, 20f, 1000f, 5),
        ["Large"] = new("BP_Module_Generator_New", "DA_Item_Module_ElectricGenerator", "DA_Skill_Engineering_Electric_Generator",
            "Large Electric Generator", "Module_Name_ElectricGenerator", "Module_Desc_ElectricGenerator", "Module_Desc_ElectricGenerator",
            42.9f, new() { ["DA_Part_ConnectorPorts"] = 4, ["DA_Part_AlloyFrame"] = 4, ["DA_Part_CoolingRadiator"] = 1, ["DA_Part_CompressionPump"] = 1, ["DA_Part_AdvancedAlloyMesh"] = 2, ["DA_Part_PowerCore"] = 1 },
            15, 3, 11f, 22f, 3000f, 4),
    };

    static string sourceRoot;
    static JObject spec;
    static string Table => (string)spec["names"]["stringTable"];
    static string TableId => TableDir + Table + "." + Table;

    public static int Run(string map, string specPath, string source, string output, string reports)
    {
        MapPath = map; sourceRoot = source; OutputRoot = output; ReportDir = reports;
        spec = JObject.Parse(File.ReadAllText(specPath));
        Require(!Directory.Exists(OutputRoot) || !Directory.EnumerateFileSystemEntries(OutputRoot).Any(), "Output root must be empty: " + OutputRoot);
        Directory.CreateDirectory(OutputRoot);
        Directory.CreateDirectory(ReportDir);

        BuildStringTable();
        foreach (var v in spec["variants"].Cast<JObject>())
        {
            Require(Templates.ContainsKey((string)v["size"]) && spec["fuels"][(string)v["fuel"]] != null, "Unknown variant " + v);
            BuildBlueprint(v);
            BuildItem(v);
            BuildSkill(v);
        }
        WriteReport();
        Console.WriteLine("Built " + Report.Count + " assets into " + OutputRoot);
        return 0;
    }

    static void BuildStringTable()
    {
        var log = new List<string>();
        var a = Load(Content("LocalizationStringTables/" + StockTable + ".uasset"));
        Rename(a, TableDir + StockTable, TableDir + Table, log);
        Rename(a, StockTable, Table, log);
        var st = (StringTableExport)a.Exports.Single();
        Require(st.Table.TableNamespace.Value == "ST_Modules_Generators", "Unexpected string table namespace");
        st.Table.TableNamespace = new FString((string)spec["strings"]["namespace"]);
        st.Table.Clear();
        foreach (var entry in ((JObject)spec["strings"]["entries"]).Properties())
        {
            st.Table.Add(new FString(entry.Name), new FString((string)entry.Value));
            log.Add($"entry {entry.Name} = {entry.Value}");
        }
        Save(a, "LocalizationStringTables/" + Table + ".uasset", log);
    }

    static void BuildBlueprint(JObject v)
    {
        var log = new List<string>();
        var t = Templates[(string)v["size"]];
        var fuel = spec["fuels"][(string)v["fuel"]];
        var a = Load(Content("Blueprints/Modules/Generators/" + t.Blueprint + ".uasset"));
        RenameContaining(a, t.Blueprint, (string)v["blueprint"], t.OwnNames, log);
        Rename(a, ItemDir + t.Item, ItemDir + (string)v["item"], log);
        Rename(a, t.Item, (string)v["item"], log);
        Rename(a, t.ModuleName, (string)v["moduleName"], log);

        var socket = ExportNamed(a, "VoyageModuleSocketView1_GEN_VARIABLE").Data;
        var port = Prop<StructPropertyData>(socket, "Port").Value;
        if ((string)v["size"] == "Portable")
        {
            // Only the fuel socket uses the petrol socket visuals.
            Rename(a, SocketDir + "DA_Socket_LiquidData_Petrol", SocketDir + (string)fuel["socketData"], log);
            Rename(a, "DA_Socket_LiquidData_Petrol", (string)fuel["socketData"], log);
            Require(Prop<SoftObjectPropertyData>(socket, "DataAsset").Value.AssetPath.AssetName.ToString() == (string)fuel["socketData"], "Fuel socket visuals were not redirected");
            Require(!port.Any(p => p.Name.ToString() == "Type"), "Stock fuel socket is expected to keep the default direction");
            SetEnum(port, "ResourceType", "Petrol", (string)fuel["resourceType"], log, "FuelSocket.Port");
            if (fuel["portableCategory"].Type != JTokenType.Null)
                InsertEnumAfter(a, port, "ResourceType", "ResourceCategory", (string)fuel["portableCategory"], log, "FuelSocket.Port");
            if (fuel["fuelSocketBandwidth"].Type != JTokenType.Null)
            {
                // Schema order is Type, ResourceType, Bandwidth, ..., ResourceCategory. The struct is cloned from
                // this Blueprint's own CO2 output socket, which shares the Port struct context.
                Require(!port.Any(p => p.Name.ToString() == "Bandwidth"), "Stock fuel socket already sets a bandwidth");
                var co2Port = Prop<StructPropertyData>(ExportNamed(a, "CO2Output_GEN_VARIABLE").Data, "Port").Value;
                var bandwidth = (StructPropertyData)Prop<StructPropertyData>(co2Port, "Bandwidth").Clone();
                foreach (var side in new[] { "Input", "Output" })
                    SetFloat(bandwidth.Value, side, 60000f, (float)fuel["fuelSocketBandwidth"], log);
                port.Insert(port.IndexOf(Prop<EnumPropertyData>(port, "ResourceType")) + 1, bandwidth);
                log.Add("FuelSocket.Port.Bandwidth (default) -> cloned from CO2Output");
            }
            var visualization = Prop<StructPropertyData>(ExportNamed(a, "ModuleComponent").Data, "WorkloadVisualizationConfig").Value;
            SetEnum(visualization, "ResourceDeltaType", "Petrol", (string)fuel["resourceType"], log, "ModuleComponent.WorkloadVisualizationConfig");
        }
        else
        {
            // Reparent onto the portable clone of the same fuel; it provides the fuel socket and visualization.
            var parent = (string)spec["variants"].Single(x => (string)x["fuel"] == (string)v["fuel"] && (string)x["size"] == "Portable")["blueprint"];
            RenameContaining(a, StockParent, parent, 3, log);
            AliasBlueprintSchemas(a, StockParent, parent, log);
            Require(port.Count == 1, "Large fuel socket is expected to override only its category");
            var category = (string)fuel["largeCategory"];
            if (category == "Fuel") Require(Prop<EnumPropertyData>(port, "ResourceCategory").Value.ToString() == "Fuel", "Unexpected large fuel category");
            else SetEnum(port, "ResourceCategory", "Fuel", category, log, "FuelSocket.Port");
        }
        Require(Prop<ObjectPropertyData>(ExportNamed(a, "ModuleComponent").Data, "ItemAsset").Value.ToImport(a).ObjectName.ToString() == (string)v["item"], "ModuleComponent.ItemAsset was not redirected");
        Save(a, "Blueprints/Modules/Generators/" + (string)v["blueprint"] + ".uasset", log);
    }

    static void BuildItem(JObject v)
    {
        var log = new List<string>();
        var t = Templates[(string)v["size"]];
        var fuel = spec["fuels"][(string)v["fuel"]];
        string item = (string)v["item"], blueprint = (string)v["blueprint"];
        var a = Load(Content("Data/Assets/Modules/" + t.Item + ".uasset"));
        Rename(a, ItemDir + t.Item, ItemDir + item, log);
        Rename(a, t.Item, item, log);
        Rename(a, BlueprintDir + t.Blueprint, BlueprintDir + blueprint, log);
        Rename(a, t.Blueprint + "_C", blueprint + "_C", log);
        RenameTable(a, log);
        // Conversion map keys are serialized as names: renaming the key name switches the fuel.
        Rename(a, "EModuleResourceType::Petrol", "EModuleResourceType::" + (string)fuel["resourceType"], log);

        var root = ExportNamed(a, item).Data;
        SetFloat(root, "Weight", t.Weight, (float)v["weight"], log);
        var desired = ((JObject)v["components"]).Properties().ToDictionary(p => p.Name, p => (int)p.Value);
        var components = Prop<MapPropertyData>(root, "Components").Value;
        Require(components.Count == t.Recipe.Count && desired.Count == t.Recipe.Count, "Recipe must keep the stock part types (no new imports)");
        foreach (var entry in components)
        {
            var part = ((ObjectPropertyData)entry.Key).Value.ToImport(a).ObjectName.ToString();
            var count = (IntPropertyData)entry.Value;
            Require(t.Recipe.TryGetValue(part, out var before) && count.Value == before && desired.ContainsKey(part), "Unexpected recipe entry " + part);
            if (count.Value != desired[part]) log.Add($"Components[{part}] {count.Value} -> {desired[part]}");
            count.Value = desired[part];
        }
        SetTextKey(Prop<TextPropertyData>(root, "Name"), TableId, t.NameKey, (string)v["nameKey"], log);
        SetTextKey(Prop<TextPropertyData>(ExportNamed(a, "VoyagePropertyDescription_0").Data, "ShortDescription"), TableId, t.ItemDescKey, (string)v["descKey"], log);

        var conversion = Prop<StructPropertyData>(ExportNamed(a, "VoyagePropertyModuleResourceConversion_0").Data, "Conversion").Value;
        Require(Prop<FloatPropertyData>(conversion, "Speed").Value == t.Speed && Prop<BoolPropertyData>(conversion, "bRequired").Value, "Unexpected conversion speed/requirement");
        SetFloatMap(Prop<MapPropertyData>(conversion, "Inputs"), new() { ["EModuleResourceType::" + (string)fuel["resourceType"]] = 2f }, (JObject)v["conversion"]["inputs"], log, "Inputs");
        SetFloatMap(Prop<MapPropertyData>(conversion, "Outputs"), new() { ["EModuleResourceType::Electricity"] = t.Electricity, ["EModuleResourceType::CarbonDioxide"] = t.CarbonDioxide }, (JObject)v["conversion"]["outputs"], log, "Outputs");
        Save(a, "Data/Assets/Modules/" + item + ".uasset", log);
    }

    static void BuildSkill(JObject v)
    {
        var log = new List<string>();
        var t = Templates[(string)v["size"]];
        string skill = (string)v["skill"], item = (string)v["item"];
        var a = Load(Content("Data/Assets/Skill/Modules/" + t.Skill + ".uasset"));
        Rename(a, SkillDir + t.Skill, SkillDir + skill, log);
        Rename(a, t.Skill, skill, log);
        Rename(a, ItemDir + t.Item, ItemDir + item, log);
        Rename(a, t.Item, item, log);
        RenameTable(a, log);

        var root = ExportNamed(a, skill).Data;
        var unlock = Prop<StructPropertyData>(root, "Unlock").Value;
        Require(Prop<EnumPropertyData>(unlock, "UnlockMethod").Value.ToString() == "Tier", "Skill is not tier-unlocked");
        SetInt(unlock, "Requirement", t.Tier, (int)v["tier"], log);
        SetInt(unlock, "Cost", t.Cost, (int)v["cost"], log);
        var items = Prop<ArrayPropertyData>(root, "Items").Value;
        Require(items.Length == 1 && ((ObjectPropertyData)items[0]).Value.ToImport(a).ObjectName.ToString() == item, "Skill does not unlock the new item");
        SetTextKey(Prop<TextPropertyData>(root, "Name"), TableId, t.NameKey, (string)v["nameKey"], log);
        SetTextKey(Prop<TextPropertyData>(root, "Description"), TableId, t.SkillDescKey, (string)v["descKey"], log);
        Save(a, "Data/Assets/Skill/Modules/" + skill + ".uasset", log);
    }

    // The stock table is a CreateBeforeSerialization import wherever its texts are used.
    static void RenameTable(UAsset a, List<string> log)
    {
        Rename(a, TableDir + StockTable, TableDir + Table, log);
        Rename(a, TableDir + StockTable + "." + StockTable, TableId, log);
        Rename(a, StockTable, Table, log);
    }

    static void SetFloatMap(MapPropertyData map, Dictionary<string, float> expected, JObject desired, List<string> log, string label)
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
