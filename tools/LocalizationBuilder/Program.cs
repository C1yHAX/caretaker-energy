using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.UnrealTypes;
using UAssetAPI.Unversioned;
using Newtonsoft.Json.Linq;

// Dump current cooked assets with the game's schema. The dump is an audit input,
// never a replacement for loading the current asset or verifying its roundtrip.
if (args.Length is not (4 or 5)) throw new ArgumentException("mapping source output donor [catalog.json]");
var catalog=args.Length==5?JArray.Parse(File.ReadAllText(args[4])):null;
var mappings = new Usmap(args[0]);
UAsset Read(string path) {
    path=Path.GetFullPath(path);
    var a = new UAsset(EngineVersion.VER_UE5_6, mappings) {
        FilePath=path, PayloadEngineVersionOverride=EngineVersion.VER_UE5_8 };
    using(var r=a.PathToReader(path)) a.Read(r);
    var raw=a.Exports.OfType<RawExport>().ToList();
    if(raw.Count>0 && !(Path.GetFileName(path).StartsWith("DA_Module_WallSocket_") && raw.Count==1 && raw[0].ObjectName.ToString()=="BP_CraftingLocationGenerator_WallSocket_C_0")) {
        Directory.CreateDirectory(args[2]);
        File.WriteAllText(Path.Combine(args[2],Path.GetFileName(path)+".skipped.json"),a.SerializeJson(true));
        throw new Exception("Unparsed asset: "+path);
    }
    if(!a.VerifyBinaryEquality()) throw new Exception("Non-lossless read: "+path);
    foreach(var e in a.Exports.OfType<StructExport>()) {
        var s=Usmap.GetSchemaFromStructExport(e,true);
        mappings.Schemas[e.ObjectName.ToString()]=s;
        if(!string.IsNullOrEmpty(s.ModulePath)) mappings.Schemas[s.ModulePath+"."+e.ObjectName]=s;
    }
    return a;
}
Read(args[3]);
var stockRoot=Path.GetDirectoryName(args[3])!;
var socket=Path.Combine(stockRoot,"BP_WirelessResource_Socket_Base.uasset");
if(File.Exists(socket)) Read(socket);
var fabrication=Path.GetFullPath(Path.Combine(stockRoot,"../../../Fabrication/BP_CraftingLocationGenerator_WallSocket.uasset"));
if(File.Exists(fabrication)) Read(fabrication);
var files=Directory.GetFiles(args[1],"*.uasset",SearchOption.AllDirectories)
    .Where(p=>Path.GetFileName(p).StartsWith("BP_") || Path.GetFileName(p).StartsWith("DA_") || Path.GetFileName(p).StartsWith("ST_"))
    .OrderBy(p=>Path.GetFileName(p).StartsWith("BP_")?0:1).ThenBy(p=>p).ToList();
if(catalog!=null && catalog.All(t=>((string)t["namespace"]!).StartsWith("ST_Mod_"))) files=files.Where(p=>Path.GetFileName(p).StartsWith("ST_")).ToList();
var changed=new List<object>();
foreach(var path in files) {
    UAsset a;
    try { a=Read(path); }
    catch(Exception e) { if(catalog!=null) throw; Console.WriteLine("SKIPPED "+e.Message); continue; }
    var target=Path.Combine(args[2],Path.GetRelativePath(args[1],path)+".json");
    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    File.WriteAllText(target,a.SerializeJson(true));
    if(catalog!=null) {
        var before=JObject.Parse(a.SerializeJson(true));
        var json=(JObject)before.DeepClone();
        int count=0;
        foreach(var text in json.Descendants().OfType<JObject>().Where(t=>((string?)t["$type"]??"").Contains("TextPropertyData"))) {
            var source=(string?)text["CultureInvariantString"];
            var entry=catalog.FirstOrDefault(t=>(string?)t["source"]==source && source!=null);
            if(entry==null) continue;
            text["Flags"]="0";text["HistoryType"]="Base";
            text["Namespace"]=entry["namespace"]!.DeepClone();text["Value"]=entry["key"]!.DeepClone();
            text["CultureInvariantString"]=entry["en"]!.DeepClone();count++;
        }
        var edited=UAsset.DeserializeJson(json.ToString());
        edited.Mappings=a.Mappings;edited.PayloadEngineVersionOverride=EngineVersion.VER_UE5_8;
        foreach(var table in edited.Exports.OfType<StringTableExport>()) {
            foreach(var row in catalog.Where(t=>(string?)t["namespace"]==table.Table.TableNamespace.Value)) {
                var key=new FString((string)row["key"]!);
                table.Table[key]=new FString((string)row["en"]!);count++;
            }
        }
        if(count==0)continue;
        typeof(UAsset).GetField("NamesReferencedFromExportDataCount",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!
            .SetValue(edited,edited.GetNameMapIndexList().Count);
        var destination=Path.Combine(args[2],Path.GetRelativePath(args[1],path));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        edited.Write(destination);
        var check=Read(destination);
        var expected=JObject.Parse(edited.SerializeJson(true));var actual=JObject.Parse(check.SerializeJson(true));
        foreach(var root in new[]{expected,actual}) {
            foreach(var e in (JArray)root["Exports"]!){((JObject)e).Remove("SerialOffset");((JObject)e).Remove("SerialSize");}
            // UAssetAPI reads null FPackageIndex references as null but its JSON
            // constructor materializes index 0. Both serialize to the same int32.
            foreach(var p in root.Descendants().OfType<JProperty>().Where(p=>p.Name is "Next" or "ArrayInnerProp" or "StringTableAsset"))
                if(p.Value.Type==JTokenType.Integer && (int)p.Value==0)p.Value=JValue.CreateNull();
        }
        if(!JToken.DeepEquals(expected["Exports"],actual["Exports"]) || !JToken.DeepEquals(before["Imports"],actual["Imports"])) {
            File.WriteAllText(destination+".expected.json",expected.ToString());File.WriteAllText(destination+".actual.json",actual.ToString());
            throw new Exception("Semantic mismatch: "+path);
        }
        changed.Add(new { asset=Path.GetRelativePath(args[1],path),textFields=count,nonTextExportsUnchanged=true });
    }
    Console.WriteLine(Path.GetFileName(path));
}
if(catalog!=null)File.WriteAllText(Path.Combine(args[2],"localization-verification.json"),Newtonsoft.Json.JsonConvert.SerializeObject(changed,Newtonsoft.Json.Formatting.Indented));
