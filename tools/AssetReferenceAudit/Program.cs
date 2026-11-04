using UAssetAPI;
using UAssetAPI.UnrealTypes;
using UAssetAPI.Unversioned;

if (args.Length != 3) throw new ArgumentException("mapping asset-or-directory output");
var root = Path.GetFullPath(args[1]);
var mapping = new Usmap(args[0]);
var files = Directory.Exists(root) ? Directory.GetFiles(root, "*.uasset", SearchOption.AllDirectories) : new[] { root };
foreach (var path in files) {
var asset = new UAsset(EngineVersion.VER_UE5_6, mapping) {
    FilePath = path, PayloadEngineVersionOverride = EngineVersion.VER_UE5_8
};
using (var reader = asset.PathToReader(path)) asset.Read(reader);
if (!asset.VerifyBinaryEquality()) throw new Exception("Non-lossless asset read");
var output = Directory.Exists(root) ? Path.Combine(args[2], Path.GetRelativePath(root, path)+".json") : args[2];
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
File.WriteAllText(output, asset.SerializeJson(true));
Console.WriteLine($"{Path.GetFileName(path)}: {asset.Imports.Count} imports, {asset.Exports.Count} exports");
}
