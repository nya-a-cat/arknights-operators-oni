#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="/tmp/ModLocalizationTests-${UID}.exe"

cleanup() {
	rm -f "$OUT"
}
trap cleanup EXIT

mcs \
	-langversion:latest \
	-out:"$OUT" \
	"$ROOT/src/ModLocalization.cs" \
	"$ROOT/tests/ModLocalizationTests.cs"

mono "$OUT"
