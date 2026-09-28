#!/usr/bin/env python3
"""Validate standalone package boundaries and the extracted source snapshot."""
import hashlib
import json
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]


def validate():
    snapshot = json.loads((ROOT / "Documentation~/release-snapshot.json").read_text())
    actual = {}
    guids = {}
    for name, expected_version in (("com.bujiaban.pointcloud", "3.0.0"),
                                   ("com.bujiaban.pointcloud.immersal", "2.0.0"),
                                   ("com.bujiaban.pointcloud.immersal.rokid", "2.0.0")):
        package = ROOT / "Packages" / name
        manifest = json.loads((package / "package.json").read_text())
        assert manifest["name"] == name and manifest["version"] == expected_version
        assert manifest["unity"] == "6000.6" and manifest["unityRelease"] == "2f1"
        if name == "com.bujiaban.pointcloud":
            assert manifest["dependencies"] == {}, "Core acquired external dependencies"
        for path in sorted(package.rglob("*")):
            assert not path.is_symlink(), f"Symlink in package: {path}"
            if not path.is_file():
                continue
            relative = path.relative_to(ROOT).as_posix()
            actual[relative] = hashlib.sha256(path.read_bytes()).hexdigest()
            if path.suffix == ".meta":
                match = re.search(r"^guid: ([a-f0-9]{32})$", path.read_text(), re.M)
                assert match, f"Missing GUID: {relative}"
                guid = match.group(1)
                assert guid not in guids, f"Duplicate GUID: {relative} and {guids.get(guid)}"
                guids[guid] = relative
                assert path.with_suffix("").exists(), f"Orphan meta: {relative}"
            elif not any(part.endswith("~") for part in path.relative_to(package).parts):
                assert Path(str(path) + ".meta").is_file(), f"Missing meta: {relative}"
            if path.suffix == ".cs" and "/Runtime/" in relative:
                assert not re.search(r"\b(?:Tourism|GDGeek|_7DGame_com|MrPP)\b", path.read_text()), relative
        for assembly in package.rglob("*.asmdef"):
            data = json.loads(assembly.read_text())
            assert "Assembly-CSharp" not in data.get("references", []), str(assembly)
    assert actual == snapshot["files"], (
        "Package files differ from the release snapshot; review intentional changes "
        "and update the recorded snapshot/version before publishing.")
    for project in ("Tests~/CoreOnly", "Examples~/PointCloudMinimal", "Examples~/RokidMinimal"):
        manifest_path = ROOT / project / "Packages/manifest.json"
        manifest = json.loads(manifest_path.read_text())
        for name, version in manifest["dependencies"].items():
            if version.startswith("file:"):
                target = (manifest_path.parent / version[5:]).resolve()
                assert target.is_relative_to(ROOT), f"External local dependency: {target}"
                assert json.loads((target / "package.json").read_text())["name"] == name
    print(f"PASS: {len(actual)} verified package files; {len(guids)} unique GUIDs; independent local projects")


if __name__ == "__main__":
    validate()
