#!/usr/bin/env python3
from __future__ import annotations

import argparse
import importlib.util
import json
import pathlib
import tempfile
import unittest
import zipfile


ROOT = pathlib.Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "tools" / "build_operator_fallback_release.py"
SPEC = importlib.util.spec_from_file_location("fallback_builder", SCRIPT)
assert SPEC and SPEC.loader
builder = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(builder)


class FallbackReleaseBuilderTests(unittest.TestCase):
    def test_deterministic_operator_package_and_merge(self) -> None:
        meta_url = "https://torappu.prts.wiki/assets/char_spine/char_test/meta.json"
        prefix = "https://torappu.prts.wiki/assets/char_spine/char_test/"
        meta = json.dumps(
            {
                "prefix": prefix,
                "name": "测试干员",
                "skin": {"默认": {"基建": {"file": "default/build/test"}}},
            },
            ensure_ascii=False,
            separators=(",", ":"),
        ).encode("utf-8")
        atlas = b"texture.png\nsize: 8,8\nformat: RGBA8888\n\n"
        responses = {
            meta_url: meta,
            prefix + "default/build/test.atlas": atlas,
            prefix + "default/build/test.skel": b"skeleton",
            prefix + "default/build/texture.png": b"texture",
        }
        original_download = builder.download_bytes
        builder.download_bytes = lambda url, timeout, retries: responses[url]
        try:
            with tempfile.TemporaryDirectory() as temporary:
                root = pathlib.Path(temporary)
                first, warnings = builder.build_operator_package(
                    {"id": "char_test", "name": "测试干员"},
                    root / "first",
                    "assets-v1.0.0",
                    1,
                    0,
                )
                second, _ = builder.build_operator_package(
                    {"id": "char_test", "name": "测试干员"},
                    root / "second",
                    "assets-v1.0.0",
                    1,
                    0,
                )
                first_zip = root / "first" / "packages" / "operator-char_test.zip"
                second_zip = root / "second" / "packages" / "operator-char_test.zip"
                self.assertEqual(first_zip.read_bytes(), second_zip.read_bytes())
                self.assertEqual([], warnings)
                self.assertEqual(builder.sha256(first_zip.read_bytes()), first["package_sha256"])
                self.assertEqual(first["package_sha256"], second["package_sha256"])
                with zipfile.ZipFile(first_zip) as archive:
                    self.assertEqual(
                        [
                            "operators/char_test/default/build/test.atlas",
                            "operators/char_test/default/build/test.skel",
                            "operators/char_test/default/build/texture.png",
                        ],
                        sorted(archive.namelist()),
                    )

                partials = root / "partials"
                partials.mkdir()
                partial = {
                    "schema_version": 1,
                    "snapshot_id": "fixture",
                    "release_tag": "assets-v1.0.0",
                    "operators": [first],
                    "warnings": [],
                }
                (partials / "partial-00.json").write_text(
                    json.dumps(partial, ensure_ascii=False), encoding="utf-8"
                )
                merged_path = root / "manifest.json"
                result = builder.command_merge(
                    argparse.Namespace(partials=partials, output=merged_path, expected_count=1)
                )
                self.assertEqual(0, result)
                merged = json.loads(merged_path.read_text(encoding="utf-8"))
                self.assertEqual("char_test", merged["operators"][0]["character_id"])
        finally:
            builder.download_bytes = original_download


if __name__ == "__main__":
    unittest.main()
