"""Package a local modpack staging tree with runtime files only (never publishes)."""
from pathlib import Path
import argparse, hashlib, json, zipfile
from package_release import runtime_path


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--source',type=Path,required=True)
    p.add_argument('--output',type=Path,required=True)
    args=p.parse_args()
    root=args.source.resolve()
    assert (root/'Voyage').is_dir(), 'Expected a staged Voyage tree'
    files={};excluded=[]
    for source in sorted((root/'Voyage').rglob('*')):
        if not source.is_file():continue
        assert source.resolve().is_relative_to(root), 'Source escapes staging'
        name=source.relative_to(root).as_posix()
        # Personal Warden state is preserved in this local-only complete modpack.
        local_state=name in {
            'Voyage/Binaries/Win64/ue4ss/Mods/Warden/destinations.tsv',
            'Voyage/Binaries/Win64/ue4ss/Mods/Warden/hotspots.tsv'}
        if runtime_path(name) or local_state:
            files[name]=source.read_bytes()
        elif source.suffix.lower()=='.md' or 'license' in source.name.lower():
            excluded.append(name)
        else:
            raise ValueError('Unclassified file; review before packaging: '+name)
    assert files
    args.output.parent.mkdir(parents=True,exist_ok=True)
    with zipfile.ZipFile(args.output,'w',zipfile.ZIP_DEFLATED,compresslevel=9) as z:
        for name,data in sorted(files.items()):
            info=zipfile.ZipInfo(name,(2026,10,24,0,0,0))
            info.compress_type=zipfile.ZIP_DEFLATED;info.external_attr=0o100644<<16
            z.writestr(info,data,compresslevel=9)
    with zipfile.ZipFile(args.output) as z:
        assert z.testzip() is None and set(z.namelist())==set(files)
        for name,data in files.items():assert z.read(name)==data
    print(json.dumps(dict(file=args.output.name,bytes=args.output.stat().st_size,
                          sha256=hashlib.sha256(args.output.read_bytes()).hexdigest(),
                          runtimeFiles=len(files),excludedDocumentation=excluded),indent=2))


if __name__=='__main__':main()
