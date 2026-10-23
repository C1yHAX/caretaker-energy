using System.Buffers.Binary;
using System.Security.Cryptography;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;
using static AssetTools;

// Fixed-build BC1 virtual textures. Preserve tile borders, all mip levels,
// alpha selectors and bulk offsets; change only saturated color endpoints.
static class TextureRecolor
{
 public static void Build(string stock,string source,string id,string output)
 {
  var path=Path.Combine(stock,"Voyage","Content",source[6..]+".uasset");var a=Load(path);
  Require(a.Exports.Count==1&&a.ContainsNameReference(new FString("PF_DXT1")),"Expected a single BC1 texture");
  var e=(NormalExport)a.Exports[0];var extra=e.Extras;var bulk=File.ReadAllBytes(Path.ChangeExtension(path,"ubulk"));
  Require(Prop<BoolPropertyData>(e.Data,"VirtualTextureStreaming").Value,"Expected virtual texture");
  Require(extra.Length==804&&U32(extra,644)==4&&bulk.Length==12688272,"Unexpected fixed-build VT layout");
  Require(U32(extra,96)==128&&U32(extra,100)==4&&U32(extra,112)==13&&U32(extra,116)==4096&&U32(extra,120)==4096,"Unexpected tiling/mips");
  int offset=0,changed=0;
  for(int i=0;i<4;i++){
   int meta=648+i*37,size=checked((int)U32(extra,meta+20));
   Require(U32(extra,meta+24)==4&&extra[meta+28]==4&&U32(extra,meta+29)==4&&U32(extra,meta+33)==i,"Expected raw GPU chunk with indexed bulk");
   Require(offset+size<=bulk.Length&&(size-4)%9248==0&&U32(bulk,offset)==0,"Unexpected VT chunk payload");
   Require(SHA1.HashData(bulk.AsSpan(offset,size)).AsSpan().SequenceEqual(extra.AsSpan(meta,20)),"Source VT chunk hash mismatch");
   for(int p=offset+4;p<offset+size;p+=8){
    ushort c0=U16(bulk,p),c1=U16(bulk,p+2),n0=Red(c0),n1=Red(c1);uint indices=U32(bulk,p+4);
    bool four=c0>c1,swap=four?n0<n1:n0>n1;
    if(swap){(n0,n1)=(n1,n0);uint mapped=0;
     for(int pixel=0;pixel<16;pixel++){uint v=(indices>>(pixel*2))&3;v=four?v^1:(v<2?v^1:v);mapped|=v<<(pixel*2);}indices=mapped;
    }
    if(four&&n0==n1){if(n0<ushort.MaxValue)n0++;else n1--;}
    Require((n0>n1)==four,"BC1 alpha mode changed");
    if(c0!=n0||c1!=n1)changed++;
    BinaryPrimitives.WriteUInt16LittleEndian(bulk.AsSpan(p),n0);BinaryPrimitives.WriteUInt16LittleEndian(bulk.AsSpan(p+2),n1);BinaryPrimitives.WriteUInt32LittleEndian(bulk.AsSpan(p+4),indices);
   }
   SHA1.HashData(bulk.AsSpan(offset,size)).CopyTo(extra,meta);offset+=size;
  }
  Require(offset==bulk.Length&&changed>100000,"Incomplete texture recolor");
  // Coarse fallback color follows the same palette while tiles stream in.
  var fallback=Tint(BitConverter.ToSingle(extra,628),BitConverter.ToSingle(extra,632),BitConverter.ToSingle(extra,636));
  for(int k=0;k<3;k++)BitConverter.GetBytes(fallback[k]).CopyTo(extra,628+k*4);
  string target="/Game/CaretakerEnergy/Textures/"+id;
  a.SetNameReference(a.SearchNameReference(new FString(source)),new FString(target));a.Exports[0].ObjectName=new FName(a,id);a.FolderName=new FString(target);
  Prop<GuidPropertyData>(Prop<StructPropertyData>(e.Data,"LightingGuid").Value,"LightingGuid").Value=new Guid(SHA256.HashData(bulk).AsSpan(0,16));
  var dest=Path.Combine(output,"Voyage","Content",target[6..]+".uasset");Directory.CreateDirectory(Path.GetDirectoryName(dest));a.Write(dest);
  File.WriteAllBytes(Path.ChangeExtension(dest,"ubulk"),bulk);Load(dest);
  Console.WriteLine($"Recolored {id}: {changed} BC1 blocks; 13 mips, unchanged streaming layout");
 }
 static ushort Red(ushort c){
  var v=Tint(((c>>11)&31)/31f,((c>>5)&63)/63f,(c&31)/31f);
  return (ushort)(((int)MathF.Round(v[0]*31)<<11)|((int)MathF.Round(v[1]*63)<<5)|(int)MathF.Round(v[2]*31));
 }
 static float[] Tint(float r,float g,float b){
  float hi=Math.Max(r,Math.Max(g,b)),lo=Math.Min(r,Math.Min(g,b));
  if(hi-lo<0.05f||hi<=0||(hi-lo)/hi<0.18f)return new[]{r,g,b};
  return new[]{hi,lo+(hi-lo)*0.03f,lo};
 }
 static uint U32(byte[] b,int p)=>BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(p));
 static ushort U16(byte[] b,int p)=>BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(p));
}
