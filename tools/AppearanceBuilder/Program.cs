using Newtonsoft.Json.Linq;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;
using static AssetTools;

if(args.Length!=5)throw new ArgumentException("AppearanceBuilder mapping material-stock palette energy-source reports");
MapPath=args[0];var stock=args[1];var palette=JObject.Parse(File.ReadAllText(args[2]));
var source=args[3];var reports=args[4];OutputRoot=source;ReportDir=reports;Directory.CreateDirectory(reports);
var created=new Dictionary<string,string>();var log=new List<object>();
foreach(var row in (JArray)palette["textures"])TextureRecolor.Build(stock,(string)row["source"],(string)row["id"],source);
foreach(var row in (JArray)palette["materials"]){
 string original=(string)row["source"],id=(string)row["id"],target="/Game/CaretakerEnergy/Materials/"+id;
 var path=Path.Combine(stock,"Voyage","Content",original[6..]+".uasset");var a=Load(path);
 string leaf=original.Split('/').Last();Require(a.Exports.Count==1,"Expected one material instance export");
 Require(a.Exports[0].ObjectName.ToString()==leaf,"Material name mismatch");
 if(a.ContainsNameReference(new FString(original)))a.SetNameReference(a.SearchNameReference(new FString(original)),new FString(target));
 a.Exports[0].ObjectName=new FName(a,id);a.FolderName=new FString(target);
 var root=((NormalExport)a.Exports[0]).Data;
 var scalars=Prop<ArrayPropertyData>(root,"ScalarParameterValues");
 Require(!scalars.Value.Cast<StructPropertyData>().Any(x=>Prop<NamePropertyData>(Prop<StructPropertyData>(x.Value,"ParameterInfo").Value,"Name").Value.ToString()=="HueShift"),"Unexpected original hue override");
 var hue=(StructPropertyData)scalars.Value[0].Clone();
 Prop<NamePropertyData>(Prop<StructPropertyData>(hue.Value,"ParameterInfo").Value,"Name").Value=new FName(a,"HueShift");
 Prop<FloatPropertyData>(hue.Value,"ParameterValue").Value=(float)row["hueShift"];
 Prop<GuidPropertyData>(Prop<StructPropertyData>(hue.Value,"ExpressionGUID").Value,"ExpressionGUID").Value=Guid.Empty;
 scalars.Value=scalars.Value.Append(hue).ToArray();
 if(row["scalarOverrides"] is JObject scalarOverrides){
  foreach(var pair in scalarOverrides.Properties()){
   Require(pair.Name is "Desaturation" or "Brightness","Unexpected appearance scalar");
   var existing=scalars.Value.Cast<StructPropertyData>().SingleOrDefault(x=>Prop<NamePropertyData>(Prop<StructPropertyData>(x.Value,"ParameterInfo").Value,"Name").Value.ToString()==pair.Name);
   var parameter=existing??(StructPropertyData)hue.Clone();
   Prop<NamePropertyData>(Prop<StructPropertyData>(parameter.Value,"ParameterInfo").Value,"Name").Value=new FName(a,pair.Name);
   Prop<FloatPropertyData>(parameter.Value,"ParameterValue").Value=(float)pair.Value;
   Prop<GuidPropertyData>(Prop<StructPropertyData>(parameter.Value,"ExpressionGUID").Value,"ExpressionGUID").Value=Guid.Empty;
   if(existing==null)scalars.Value=scalars.Value.Append(parameter).ToArray();
  }
 }
 if(row["textureOverrides"] is JObject overrides){
  var textures=Prop<ArrayPropertyData>(root,"TextureParameterValues").Value.Cast<StructPropertyData>().ToArray();
  StructPropertyData Texture(string key)=>textures.Single(x=>Prop<NamePropertyData>(Prop<StructPropertyData>(x.Value,"ParameterInfo").Value,"Name").Value.ToString()==key);
  foreach(var pair in overrides.Properties()){
   string textureId=(string)pair.Value;
   var pkg=a.AddImport(new Import("/Script/CoreUObject","Package",new FPackageIndex(0),"/Game/CaretakerEnergy/Textures/"+textureId,false,a));
   var imp=a.AddImport(new Import("/Script/Engine","Texture2D",pkg,textureId,false,a));
   Prop<ObjectPropertyData>(Texture(pair.Name).Value,"ParameterValue").Value=imp;
   a.Exports[0].CreateBeforeSerializationDependencies.Add(imp);
  }
 }
 var dest=Path.Combine(source,"Voyage","Content",target[6..]+".uasset");WriteChecked(a,dest);
 created[id]=target;log.Add(new{material=id,stock=original,hueShift=(float)row["hueShift"],scalarOverrides=row["scalarOverrides"],textureOverrides=row["textureOverrides"],unchanged="normals, roughness, damage, animation, shader permutation"});
}
foreach(var row in (JArray)palette["bindings"]){
 string bp=(string)row["blueprint"],component=(string)row["component"];
 var path=Path.Combine(source,"Voyage","Content","Blueprints","Modules","Generators",bp+".uasset");var a=Load(path);
 var e=ExportNamed(a,component);var mats=e.Data.OfType<ArrayPropertyData>().SingleOrDefault(x=>x.Name.ToString()=="OverrideMaterials");
 if(mats==null){
  Require(bp=="BP_Module_StationaryPetrolRefinery"&&component=="StaticMesh_GEN_VARIABLE","Unexpected missing material overrides");
  mats=(ArrayPropertyData)Prop<ArrayPropertyData>(ExportNamed(a,"LiquidMesh_GEN_VARIABLE").Data,"OverrideMaterials").Clone();e.Data.Add(mats);
 }
 var values=new List<PropertyData>();
 foreach(string id in ((JArray)row["materials"]).Select(x=>(string)x)){
  string material=created[id];var pkg=a.AddImport(new Import("/Script/CoreUObject","Package",new FPackageIndex(0),material,false,a));
  var imp=a.AddImport(new Import("/Script/Engine","MaterialInstanceConstant",pkg,id,false,a));
  var value=(ObjectPropertyData)mats.Value[0].Clone();value.Value=imp;values.Add(value);
  e.CreateBeforeSerializationDependencies.Add(imp);
 }
 mats.Value=values.ToArray();WriteChecked(a,path);log.Add(new{blueprint=bp,component,materials=row["materials"]});
}
File.WriteAllText(Path.Combine(reports,"appearance-report.json"),Newtonsoft.Json.JsonConvert.SerializeObject(log,Newtonsoft.Json.Formatting.Indented));
Console.WriteLine("Appearance materials="+created.Count+"; bindings="+((JArray)palette["bindings"]).Count);
void WriteChecked(UAsset a,string path){
 Directory.CreateDirectory(Path.GetDirectoryName(path));a.Write(path);
 var b=Load(path);Require(b.VerifyBinaryEquality(),"Material roundtrip failed");
 File.WriteAllText(Path.Combine(reports,Path.GetFileNameWithoutExtension(path)+".json"),b.SerializeJson(true));
}
