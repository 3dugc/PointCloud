#!/usr/bin/env python3
"""Prepare the pinned Rokid 4.0.1 SDK for Unity 6000.6 in a host project.

Requires Python 3.9+. Close Unity before running. Only the embedded package is
created; manifest.json and Library/PackageCache are never changed. Vendor files
retain their license and must remain excluded from source control.
"""
import argparse
import hashlib
import json
from pathlib import Path, PurePosixPath
import shutil
import sys
import tarfile
import tempfile
import urllib.request


class PreparationError(Exception):
    """The pinned SDK cannot be prepared without changing unexpected content."""


def file_hash(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def tree_hash(root):
    """SHA256 of sorted 'file-sha256  relative-posix-path\n' records."""
    digest = hashlib.sha256()
    count = 0
    for path in sorted(root.rglob("*"), key=lambda p: p.relative_to(root).as_posix()):
        if path.is_symlink():
            raise PreparationError(f"Symlink in embedded package: {path}")
        if path.is_dir():
            continue
        if not path.is_file():
            raise PreparationError(f"Unsupported file in embedded package: {path}")
        relative = path.relative_to(root).as_posix()
        digest.update(f"{file_hash(path)}  {relative}\n".encode("utf-8"))
        count += 1
    return digest.hexdigest(), count


def safe_extract(archive_path, output, maximum_bytes):
    """Extract regular package/ members only, without trusting tar paths or links."""
    seen = set()
    total = 0
    with tarfile.open(archive_path, "r:gz") as archive:
        for member in archive:
            name = member.name.rstrip("/")
            parts = name.split("/")
            if (not parts or parts[0] != "package" or
                    any(part in ("", ".", "..") for part in parts) or
                    "\\" in name or ":" in name or
                    any(ord(char) < 32 for char in name)):
                raise PreparationError(f"Unsafe archive path: {member.name!r}")
            if not member.isdir() and not member.isfile():
                raise PreparationError(f"Archive links and special files are forbidden: {name}")
            if len(parts) == 1:
                if not member.isdir():
                    raise PreparationError("The package root must be a directory.")
                continue
            relative = PurePosixPath(*parts[1:])
            # Also prevent collisions on case-insensitive host filesystems.
            key = relative.as_posix().casefold()
            if key in seen:
                raise PreparationError(f"Duplicate archive path: {name}")
            seen.add(key)
            destination = output.joinpath(*relative.parts)
            if member.isdir():
                destination.mkdir(parents=True, exist_ok=True)
                continue
            total += member.size
            if member.size < 0 or total > maximum_bytes:
                raise PreparationError("Archive exceeds its pinned unpacked byte limit.")
            destination.parent.mkdir(parents=True, exist_ok=True)
            with archive.extractfile(member) as source, destination.open("xb") as target:
                shutil.copyfileobj(source, target)
            if destination.stat().st_size != member.size:
                raise PreparationError(f"Truncated archive member: {name}")
    return total


def apply_patches(output, patches):
    for patch in patches:
        relative = PurePosixPath(patch["path"])
        if relative.is_absolute() or ".." in relative.parts:
            raise PreparationError(f"Unsafe configured patch path: {relative}")
        path = output.joinpath(*relative.parts)
        data = path.read_bytes()
        if hashlib.sha256(data).hexdigest() != patch["source_sha256"]:
            raise PreparationError(f"Vendor source changed; refusing patch: {relative}")
        for replacement in patch["replacements"]:
            before = replacement["before"].encode("utf-8")
            after = replacement["after"].encode("utf-8")
            if not before or data.count(before) != replacement["count"]:
                raise PreparationError(f"Patch context changed: {relative}")
            data = data.replace(before, after)
        if hashlib.sha256(data).hexdigest() != patch["target_sha256"]:
            raise PreparationError(f"Patched source hash mismatch: {relative}")
        path.write_bytes(data)


def download(source, path):
    request = urllib.request.Request(source["url"], headers={"User-Agent": "PointCloud-Rokid-Setup/1"})
    total = 0
    with urllib.request.urlopen(request, timeout=60) as response, path.open("xb") as output:
        if not response.geturl().startswith("https://"):
            raise PreparationError("The vendor download redirected to an insecure URL.")
        while True:
            block = response.read(1024 * 1024)
            if not block:
                break
            total += len(block)
            if total > source["archive_bytes"]:
                raise PreparationError("Download exceeds the pinned archive size.")
            output.write(block)


def prepare(project, archive_path, config):
    if config["schema_version"] != 1 or config["package"] != "com.rokid.xr.unity":
        raise PreparationError("Unsupported Rokid compatibility manifest.")
    project = project.expanduser().resolve()
    packages = project / "Packages"
    if packages.is_symlink() or not (packages / "manifest.json").is_file():
        raise PreparationError("--project must contain a real Packages directory and manifest.json.")
    if "PackageCache" in project.parts or "Library" in project.parts:
        raise PreparationError("Refusing to prepare an SDK inside a Unity Library or PackageCache.")
    target = packages / config["package"]
    if target.is_symlink():
        raise PreparationError(f"Refusing embedded-package symlink: {target}")
    expected = (config["patched_tree_sha256"], config["source"]["file_count"])
    if target.exists():
        if target.is_dir() and tree_hash(target) == expected:
            print(f"Already prepared and verified: {target}")
            return
        raise PreparationError(
            f"Existing embedded package differs; nothing was changed: {target}\n"
            "Preserve or move it aside yourself before retrying. There is no force-overwrite option.")

    # Stage beside the destination so the final rename does not copy partial output.
    with tempfile.TemporaryDirectory(prefix=".pointcloud-rokid-", dir=packages) as temporary:
        staging = Path(temporary)
        archive = archive_path.expanduser().resolve() if archive_path else staging / "sdk.tgz"
        source = config["source"]
        if archive_path is None:
            print(f"Downloading pinned Rokid {config['version']} from {source['url']}")
            download(source, archive)
        if (not archive.is_file() or archive.stat().st_size != source["archive_bytes"] or
                file_hash(archive) != source["sha256"]):
            raise PreparationError("Archive size or SHA-256 differs from the official pinned release.")
        output = staging / "package"
        output.mkdir()
        total = safe_extract(archive, output, source["unpacked_bytes"])
        if total != source["unpacked_bytes"]:
            raise PreparationError("Unpacked byte count differs from the pinned release.")
        vendor = json.loads((output / "package.json").read_text(encoding="utf-8"))
        if vendor["name"] != config["package"] or vendor["version"] != config["version"]:
            raise PreparationError("Vendor package identity differs from the pinned release.")
        apply_patches(output, config["patches"])
        if tree_hash(output) != expected:
            raise PreparationError("Prepared SDK tree differs from the reviewed compatibility patch.")
        if target.exists() or target.is_symlink():
            raise PreparationError("Embedded destination appeared during preparation; nothing was replaced.")
        output.rename(target)
    print(f"Prepared Rokid {config['version']} ({config['patch_set']}): {target}")
    print(f"Verified {expected[1]} files; {len(config['patches'])} compatibility patches.")
    print("Keep this embedded vendor package out of Git. Its original license still applies.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", type=Path, required=True, help="Unity host project directory")
    parser.add_argument("--archive", type=Path, help="Use a local official 4.0.1 .tgz instead of downloading")
    args = parser.parse_args()
    try:
        config = json.loads(Path(__file__).with_name("rokid-sdk.json").read_text(encoding="utf-8"))
        prepare(args.project, args.archive, config)
    except (PreparationError, OSError, ValueError, KeyError, tarfile.TarError) as error:
        print(f"Rokid preparation failed: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
