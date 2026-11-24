#!/usr/bin/env bash
# Render docs/cheatsheet.md as a two-page PDF (commands, then typical paths).
# Usage: scripts/cheatsheet/render.sh [out.pdf]   (default: docs/cheatsheet.pdf)
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$here/../.." && pwd)"
out="${1:-$root/docs/cheatsheet.pdf}"
html="$(mktemp --suffix=.html)"
trap 'rm -f "$html"' EXIT
python3 "$here/render.py" "$root/docs/cheatsheet.md" "$html"
node "$here/pdf.js" "$html" "$out"
echo "cheatsheet: wrote $out"
