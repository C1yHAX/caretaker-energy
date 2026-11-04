"""Replace only Energy's two string-table chunks in a known-good Zen container.

Never round-trip gameplay assets through an incomplete legacy extraction. The
baseline container header, dependency graph, exports and bulk data are retained.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess

TABLES = {
    '../../../Voyage/Content/LocalizationStringTables/ST_Mod_FuelGenerators.uasset',
    '../../../Voyage/Content/LocalizationStringTables/ST_Mod_StationaryPetrolRefinery.uasset',
}


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def patch(retoc, baseline, localized, output):
    output.mkdir(parents=True, exist_ok=False)
    def run(*args):
        subprocess.run([str(retoc), *map(str, args)], check=True)
    original, edited = output/'baseline-raw', output/'localized-raw'
    run('unpack-raw', baseline, original)
    run('unpack-raw', localized, edited)
    manifest = json.loads((original/'manifest.json').read_text())
    other = json.loads((edited/'manifest.json').read_text())
    allowed = {key for key, path in manifest['chunk_paths'].items() if path in TABLES}
    if len(allowed) != 2 or any(other['chunk_paths'].get(k) != manifest['chunk_paths'][k] for k in allowed):
        raise ValueError('String-table package IDs or paths changed')
    for chunk in (original/'chunks').iterdir():
        data = chunk.read_bytes()
        if any(s.encode(enc) in data for s in ('/Engine/UnknownPackage', 'UnknownExport') for enc in ('utf-8', 'utf-16-le')):
            raise ValueError('Baseline contains unresolved imports; use a verified working container')
    merged = output/'merged-raw'
    shutil.copytree(original, merged)
    for key in allowed:
        data = (edited/'chunks'/key).read_bytes()
        if b'UnknownPackage' in data or b'UnknownExport' in data:
            raise ValueError('Unresolved string-table import')
        shutil.copy2(edited/'chunks'/key, merged/'chunks'/key)
    package = output/'package'
    package.mkdir()
    container = package/baseline.name
    run('pack-raw', merged, container)
    run('verify', container)
    verified = output/'verified-raw'
    run('unpack-raw', container, verified)
    if json.loads((verified/'manifest.json').read_text()) != manifest:
        raise ValueError('Container paths or version changed')
    before = {p.name: sha(p) for p in (original/'chunks').iterdir()}
    after = {p.name: sha(p) for p in (verified/'chunks').iterdir()}
    if before.keys() != after.keys():
        raise ValueError('Container chunks added or removed')
    changes = {k for k in before if before[k] != after[k]}
    if not changes <= allowed:
        raise ValueError('Non-localization chunk changed')
    for k in allowed:
        if after[k] != sha(edited/'chunks'/k):
            raise ValueError('String-table roundtrip failed')
    shutil.copy2(baseline.with_suffix('.pak'), container.with_suffix('.pak'))
    report = dict(baselineSha256=sha(baseline), localizedSha256=sha(localized),
                  changedChunks=sorted(changes), preservedChunks=len(before)-len(changes),
                  containerHeaderAndDependenciesPreserved=True, chunks=after)
    (output/'verification.json').write_text(json.dumps(report, indent=2)+'\n')
    print(json.dumps({k:v for k,v in report.items() if k != 'chunks'}, indent=2))
    return package


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('retoc', 'baseline', 'localized', 'output'):
        parser.add_argument('--'+name, type=Path, required=True)
    a = parser.parse_args()
    patch(a.retoc, a.baseline, a.localized, a.output)
