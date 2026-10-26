#!/usr/bin/env bash
# Regenerates assets/icon.ico (16, 20, 24, 32, 48, 64, 256 px) from assets/icon.svg.
# Needs rsvg-convert (librsvg) and ImageMagick. The .ico is committed so CI needs neither.
set -euo pipefail
cd "$(dirname "$0")"
tmp=$(mktemp -d); trap "rm -rf $tmp" EXIT
pngs=()
for s in 16 20 24 32 48 64 256; do rsvg-convert -w $s -h $s icon.svg -o "$tmp/$s.png"; pngs+=("$tmp/$s.png"); done
convert "${pngs[@]}" icon.ico
