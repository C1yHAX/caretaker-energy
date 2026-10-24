"""Build the combined additive Energy mod from locally extracted stock assets."""
import argparse,hashlib,json,shutil,subprocess
from pathlib import Path

ROOT=Path(__file__).resolve().parent
def run(*args):subprocess.run([str(x) for x in args],check=True)
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 p=argparse.ArgumentParser()
 for name in ('mapping','refinery-stock','generator-stock','appearance-stock','icon-stock','retoc','output'):p.add_argument('--'+name,type=Path,required=True)
 a=p.parse_args();out=a.output.resolve()
 if out.exists() and any(out.iterdir()):raise SystemExit('Output must be empty; use a new build directory.')
 if sha(a.mapping)!='24ed679c5be228e6635939494db2f7682281250035d1980821b2a4a3e3d1e33d':raise SystemExit('Unexpected game mapping')
 out.mkdir(parents=True,exist_ok=True)
 for name,stock,spec in [('RefineryBuilder',a.refinery_stock,'refinery-spec.json'),('GeneratorBuilder',a.generator_stock,'generators-spec.json')]:
  project=ROOT/'tools'/name
  run('dotnet','build',project/(name+'.csproj'),'-c','Release','--nologo')
  run('dotnet',project/'bin/Release/net10.0'/(name+'.dll'),a.mapping,ROOT/spec,stock,out/name,out/(name+'-reports'))
  shutil.copytree(out/name,out/'source',dirs_exist_ok=True)
 shutil.copy2(a.refinery_stock/'scriptobjects.bin',out/'source/scriptobjects.bin')
 assert len(list((out/'source').rglob('*.uasset')))==17
 project=ROOT/'tools/AppearanceBuilder'
 run('dotnet','build',project/'AppearanceBuilder.csproj','-c','Release','--nologo')
 run('dotnet',project/'bin/Release/net10.0/AppearanceBuilder.dll',a.mapping,a.appearance_stock,ROOT/'appearance.json',out/'source',out/'appearance-reports')
 assert len(list((out/'source').rglob('*.uasset')))==25
 project=ROOT/'tools/IconBuilder'
 run('dotnet','build',project/'IconBuilder.csproj','-c','Release','--nologo')
 run('dotnet',project/'bin/Release/net10.0-windows/IconBuilder.dll',a.mapping,a.icon_stock,ROOT/'icons.json',out/'source',out/'icon-reports')
 assert len(list((out/'source').rglob('*.uasset')))==30
 package=out/'package';package.mkdir()
 run(a.retoc,'to-zen',out/'source',package/'CaretakerEnergy_P.utoc','--version','UE5_6')
 run(a.retoc,'verify',package/'CaretakerEnergy_P.utoc')
 shutil.copy2(ROOT/'addon-manifest.json',package/'CaretakerEnergy_P.tlcaddon.json')
 entries=json.loads((ROOT/'addon-manifest.json').read_text(encoding='utf-8'))['assets']
 assert len(entries)==10 and len({x['id'] for x in entries})==10
 (out/'SHA256SUMS.txt').write_text('\n'.join(f'{sha(f)}  {f.name}' for f in sorted(package.iterdir()))+'\n',encoding='utf-8')
 print('Built:',package)
if __name__=='__main__':main()
