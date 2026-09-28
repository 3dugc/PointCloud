#!/usr/bin/env python3
"""Create deterministic UPM tarballs, then verify every archived file."""
import gzip
import hashlib
import io
import json
from pathlib import Path
import tarfile

from validate import ROOT, validate


def pack():
    validate()
    output = ROOT / "dist"
    output.mkdir(exist_ok=True)
    checksums = []
    for package in sorted((ROOT / "Packages").iterdir()):
        if not (package / "package.json").is_file():
            continue
        manifest = json.loads((package / "package.json").read_text())
        target = output / f'{manifest["name"]}-{manifest["version"]}.tgz'
        expected = {}
        with target.open("wb") as raw:
            with gzip.GzipFile(filename="", fileobj=raw, mode="wb", mtime=0) as compressed:
                with tarfile.open(fileobj=compressed, mode="w", format=tarfile.USTAR_FORMAT) as archive:
                    for path in sorted(package.rglob("*")):
                        if not path.is_file():
                            continue
                        name = "package/" + path.relative_to(package).as_posix()
                        data = path.read_bytes()
                        expected[name] = data
                        entry = tarfile.TarInfo(name)
                        entry.size = len(data)
                        entry.mode = 0o644
                        archive.addfile(entry, io.BytesIO(data))
        with tarfile.open(target) as archive:
            actual = {member.name: archive.extractfile(member).read()
                      for member in archive.getmembers() if member.isfile()}
        assert actual == expected, f"Archive content mismatch: {target}"
        digest = hashlib.sha256(target.read_bytes()).hexdigest()
        checksums.append(f"{digest}  {target.name}\n")
        print(f"PASS: {target.name}: {len(actual)} files, round-trip verified")
    (output / "SHA256SUMS").write_text("".join(checksums))


if __name__ == "__main__":
    pack()
