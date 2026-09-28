#!/usr/bin/env bash
set -euo pipefail
repo_dir="$(cd "$(dirname "$0")/.." && pwd)"
: "${UNITY_EDITOR:?Set UNITY_EDITOR to the Unity executable (6000.6.2f1 verified)}"
suite="${1:-all}"
case "$suite" in
  core) project="Tests~/CoreOnly" ;;
  all) project="Examples~/PointCloudMinimal" ;;
  rokid) project="Examples~/RokidMinimal" ;;
  *) echo "Usage: UNITY_EDITOR=/path/to/Unity $0 [core|all|rokid]" >&2; exit 2 ;;
esac
mkdir -p "$repo_dir/artifacts"
result="$repo_dir/artifacts/$suite-editmode.xml"
# Remove old evidence so a failed Unity invocation cannot appear to pass.
rm -f "$result"
"$UNITY_EDITOR" -batchmode -nographics -projectPath "$repo_dir/$project" \
  -runTests -testPlatform EditMode -testResults "$result" \
  -logFile "$repo_dir/artifacts/$suite-editmode.log"
python3 - "$result" <<'PY'
import sys
import xml.etree.ElementTree as ET
root = ET.parse(sys.argv[1]).getroot()
assert root.attrib.get("result") == "Passed", root.attrib
assert int(root.attrib.get("total", "0")) > 0, "No tests executed"
print(root.attrib)
PY
