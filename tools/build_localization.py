"""Localize an extracted cooked mod without rebuilding its gameplay or visuals.

The source must contain the original .uasset/.uexp/.ubulk files and scriptobjects.bin.
Requires the documented game mapping, compatible UAssetAPI DLLs and retoc.
"""
from pathlib import Path
import argparse
import json
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[1]


def generate_runtime(catalog, target):
    rows = json.loads(catalog.read_text(encoding='utf8'))
    quote = lambda value: json.dumps(value, ensure_ascii=False)
    text = '-- Generated from localization.json. English native text; German translation.\nreturn {\n'
    text += ''.join('    {' + ', '.join(k + '=' + quote(r[k]) for k in ('namespace', 'key', 'en', 'de')) + '},\n' for r in rows)
    target.mkdir(parents=True, exist_ok=True)
    (target / 'translations.lua').write_text(text + '}\n', encoding='utf8')
    shutil.copy2(ROOT / 'tools/localization_runtime.lua', target / 'localization.lua')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('source', 'mapping', 'donor', 'retoc', 'output'):
        parser.add_argument('--' + name, type=Path, required=True)
    parser.add_argument('--catalog', type=Path, default=ROOT / 'localization.json')
    parser.add_argument('--container', required=True)
    args = parser.parse_args()
    if args.output.exists() and any(args.output.iterdir()):
        raise SystemExit('Use a new, empty output directory.')
    project = ROOT / 'tools/LocalizationBuilder/LocalizationBuilder.csproj'
    subprocess.run(['dotnet', 'build', str(project), '-c', 'Release', '--nologo'], check=True)
    patched = args.output / 'patched'
    subprocess.run(['dotnet', str(project.parent / 'bin/Release/net10.0/LocalizationBuilder.dll'),
                    str(args.mapping), str(args.source), str(patched), str(args.donor), str(args.catalog)], check=True)
    source = args.output / 'source'
    for item in args.source.rglob('*'):
        if not item.is_file() or (item.suffix not in ('.uasset', '.uexp', '.ubulk') and item.name != 'scriptobjects.bin'):
            continue
        relative = item.relative_to(args.source)
        edited = patched / relative
        dest = source / relative
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(edited if edited.exists() else item, dest)
    package = args.output / 'package'
    package.mkdir(parents=True)
    subprocess.run([str(args.retoc), 'to-zen', str(source), str(package / (args.container + '.utoc')), '--version', 'UE5_7'], check=True)
    subprocess.run([str(args.retoc), 'verify', str(package / (args.container + '.utoc'))], check=True)
    print('Localized container:', package)


if __name__ == '__main__':
    main()
