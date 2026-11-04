"""Localize an extracted cooked mod without rebuilding its gameplay or visuals.

The source must contain the original .uasset/.uexp/.ubulk files and scriptobjects.bin.
Requires the documented game mapping, compatible UAssetAPI DLLs and retoc.
"""
from pathlib import Path
import argparse
import json
import shutil
import subprocess
from patch_energy_strings import patch

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
    parser.add_argument('--baseline-utoc', type=Path,
                        help='Required for Energy: verified, working pre-localization container')
    args = parser.parse_args()
    energy = args.container == 'CaretakerEnergy_P'
    if energy and not args.baseline_utoc:
        parser.error('Energy requires --baseline-utoc; never repack its gameplay assets for translation.')
    if not (args.source/'scriptobjects.bin').is_file():
        raise SystemExit('Missing scriptobjects.bin: extract with the complete game containers available.')
    for item in args.source.rglob('*.uasset'):
        data = item.read_bytes()
        for marker in ('/Engine/UnknownPackage', 'UnknownExport'):
            if marker.encode() in data or marker.encode('utf-16-le') in data:
                raise SystemExit('Unresolved imports in '+str(item)+'. Re-extract against the complete game containers.')
    if args.output.exists() and any(args.output.iterdir()):
        raise SystemExit('Use a new, empty output directory.')
    project = ROOT / 'tools/LocalizationBuilder/LocalizationBuilder.csproj'
    subprocess.run(['dotnet', 'build', str(project), '-c', 'Release', '--nologo'], check=True)
    patched = args.output / 'patched'
    subprocess.run(['dotnet', str(project.parent / 'bin/Release/net10.0/LocalizationBuilder.dll'),
                    str(args.mapping), str(args.source), str(patched), str(args.donor), str(args.catalog)], check=True)
    source = args.output / 'source'
    # Energy needs only its two edited string tables. All gameplay chunks are
    # copied verbatim from the verified baseline after converting these tables.
    input_tree = patched if energy else args.source
    for item in input_tree.rglob('*'):
        if not item.is_file() or (item.suffix not in ('.uasset', '.uexp', '.ubulk') and item.name != 'scriptobjects.bin'):
            continue
        relative = item.relative_to(input_tree)
        edited = patched / relative
        dest = source / relative
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(edited if edited.exists() else item, dest)
    shutil.copy2(args.source/'scriptobjects.bin', source/'scriptobjects.bin')
    package = args.output / 'package'
    package.mkdir(parents=True)
    subprocess.run([str(args.retoc), 'to-zen', str(source), str(package / (args.container + '.utoc')), '--version', 'UE5_6' if energy else 'UE5_7'], check=True)
    subprocess.run([str(args.retoc), 'verify', str(package / (args.container + '.utoc'))], check=True)
    if energy:
        package = patch(args.retoc, args.baseline_utoc, package/(args.container+'.utoc'), args.output/'preserved')
        shutil.copy2(ROOT/'addon-manifest.json', package/'CaretakerEnergy_P.tlcaddon.json')
    print('Localized container:', package)


if __name__ == '__main__':
    main()
