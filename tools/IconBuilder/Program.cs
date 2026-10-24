using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;
using static AssetTools;

if(args.Length!=5)throw new ArgumentException("IconBuilder mapping stock icons-json energy-source reports");
MapPath=args[0];string stock=args[1],specPath=args[2],source=args[3],reports=args[4];
Directory.CreateDirectory(reports);
var spec=JArray.Parse(File.ReadAllText(specPath));var records=new List<object>();
foreach(var row in spec){
 string original=(string)row["original"],id=(string)row["id"],target=((string)row["packageRoot"]??"/Game/CaretakerEnergy/Icons")+"/"+id;
 string imagePath=Path.Combine(Path.GetDirectoryName(specPath),(string)row["image"]);
 var a=Load(Path.Combine(stock,"Voyage","Content",original[6..]+".uasset"));
 Require(a.Exports.Count==1,"Expected one texture export");var e=(NormalExport)a.Exports[0];var extra=e.Extras;
 uint U32(int n)=>BinaryPrimitives.ReadUInt32LittleEndian(extra.AsSpan(n));
 Require(extra.Length==262252&&U32(44)==256&&U32(48)==256&&U32(76)==1,"Unexpected UI texture layout");
 Require(System.Text.Encoding.ASCII.GetString(extra,60,12)=="PF_B8G8R8A8\0","Unexpected UI pixel format");
 Require(U32(262228)==256&&U32(262232)==256&&U32(262236)==1,"Unexpected UI mip footer");
 using var input=new Bitmap(imagePath);
 Require(input.Width==input.Height,"Icon source must be square");
 using var scaled=new Bitmap(256,256,PixelFormat.Format32bppArgb);
 using(var g=Graphics.FromImage(scaled)){
  g.CompositingMode=CompositingMode.SourceCopy;g.CompositingQuality=CompositingQuality.HighQuality;
  g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
  g.Clear(Color.Transparent);g.DrawImage(input,new Rectangle(0,0,256,256));
 }
 var bits=scaled.LockBits(new Rectangle(0,0,256,256),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
 var pixels=new byte[256*256*4];
 try{Require(bits.Stride==1024,"Unexpected icon stride");Marshal.Copy(bits.Scan0,pixels,0,pixels.Length);}finally{scaled.UnlockBits(bits);}
 int transparent=0,opaque=0;for(int i=3;i<pixels.Length;i+=4){if(pixels[i]==0)transparent++;if(pixels[i]>240)opaque++;}
 Require(transparent>1000&&opaque>2000,"Missing transparency or subject");
 pixels.CopyTo(extra,84);
 Rename(a,original,target,new List<string>());e.ObjectName=new FName(a,id);a.FolderName=new FString(target);
 Prop<GuidPropertyData>(Prop<StructPropertyData>(e.Data,"LightingGuid").Value,"LightingGuid").Value=new Guid(SHA256.HashData(pixels).AsSpan(0,16));
 var dest=Path.Combine(source,"Voyage","Content",target[6..]+".uasset");Directory.CreateDirectory(Path.GetDirectoryName(dest));
 a.Write(dest);var check=Load(dest);Require(((NormalExport)check.Exports[0]).Extras.AsSpan(84,262144).SequenceEqual(pixels),"Icon payload changed");
 scaled.Save(Path.Combine(reports,id+".png"),ImageFormat.Png);
 foreach(string leaf in ((JArray)row["bindings"]).Select(x=>(string)x)){
  var path=Directory.EnumerateFiles(source,leaf+".uasset",SearchOption.AllDirectories).Single();var asset=Load(path);
  var icon=Prop<ObjectPropertyData>(((NormalExport)asset.Exports[0]).Data,"Icon");
  Require(icon.Value.Index<0,"Expected hard texture import");
  // Keep import indices and dependencies; retarget the texture package/object names.
  var names=asset.GetNameMapIndexList().Select(x=>x.Value).Where(x=>x==original||x==original.Split('/').Last()).ToArray();
  Require(names.Length==2,"Unexpected original item/skill icon names: "+leaf);
  Rename(asset,original,target,new List<string>());Rename(asset,original.Split('/').Last(),id,new List<string>());
  asset.Write(path);Load(path);
 }
 records.Add(new{id,sourceImageSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(imagePath))).ToLowerInvariant(),pixelSha256=Convert.ToHexString(SHA256.HashData(pixels)).ToLowerInvariant(),width=256,height=256,transparentPixels=transparent,opaquePixels=opaque,bindings=row["bindings"]});
 Console.WriteLine("Built icon "+id+" and "+((JArray)row["bindings"]).Count+" references");
}
File.WriteAllText(Path.Combine(reports,"icons-verification.json"),Newtonsoft.Json.JsonConvert.SerializeObject(records,Newtonsoft.Json.Formatting.Indented));
