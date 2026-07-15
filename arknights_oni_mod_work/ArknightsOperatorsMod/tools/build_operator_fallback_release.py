#!/usr/bin/env python3
"""Build sharded, deterministic GitHub Release fallback packages from PRTS assets."""

from __future__ import annotations

import argparse
import concurrent.futures
import hashlib
import json
import pathlib
import sys
import time
import urllib.parse
import urllib.request
import zipfile


SCHEMA_VERSION = 1
MAX_SOURCE_FILE_BYTES = 64 * 1024 * 1024
TECHNICAL_PACKAGE_LIMIT_BYTES = 512 * 1024 * 1024
LARGE_PACKAGE_REPORT_BYTES = 100 * 1024 * 1024
ALLOWED_SOURCE_HOSTS = {"torappu.prts.wiki", "static.prts.wiki"}
RELEASE_ROOT = "https://github.com/nya-a-cat/arknights-oni/releases/download"


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest().upper()


def require_safe_relative_path(value: str, label: str) -> str:
    normalized = value.strip().replace("\\", "/")
    if (
        not normalized
        or normalized.startswith("/")
        or normalized == ".."
        or "../" in normalized
        or ":" in normalized
        or "//" in normalized
    ):
        raise ValueError(f"invalid {label}: {value!r}")
    return normalized


def require_source_url(value: str) -> str:
    parsed = urllib.parse.urlparse(value)
    if parsed.scheme != "https" or parsed.hostname not in ALLOWED_SOURCE_HOSTS:
        raise ValueError(f"unapproved PRTS source URL: {value}")
    return value


def download_bytes(url: str, timeout: int, retries: int) -> bytes:
    require_source_url(url)
    last_error: Exception | None = None
    for attempt in range(retries + 1):
        try:
            request = urllib.request.Request(url, headers={"User-Agent": "arknights-oni-fallback-builder/1"})
            with urllib.request.urlopen(request, timeout=timeout) as response:
                final_url = require_source_url(response.geturl())
                length_header = response.headers.get("Content-Length")
                if length_header and int(length_header) > MAX_SOURCE_FILE_BYTES:
                    raise ValueError(f"source file exceeds 64 MiB: {final_url}")
                chunks: list[bytes] = []
                length = 0
                while True:
                    chunk = response.read(64 * 1024)
                    if not chunk:
                        break
                    length += len(chunk)
                    if length > MAX_SOURCE_FILE_BYTES:
                        raise ValueError(f"source file exceeds 64 MiB: {final_url}")
                    chunks.append(chunk)
                return b"".join(chunks)
        except Exception as error:  # the final exception retains the URL and retry history
            last_error = error
            if attempt < retries:
                print(
                    f"retry {attempt + 1}/{retries} after {type(error).__name__}: {url}",
                    file=sys.stderr,
                    flush=True,
                )
                time.sleep(2**attempt)
    raise RuntimeError(f"download failed after {retries + 1} attempts: {url}") from last_error


def parse_atlas_pages(atlas: bytes) -> list[str]:
    pages: list[str] = []
    first_line_of_block = True
    for raw_line in atlas.decode("utf-8-sig").splitlines():
        line = raw_line.strip()
        if not line:
            first_line_of_block = True
            continue
        if first_line_of_block:
            pages.append(require_safe_relative_path(line, "atlas page"))
            first_line_of_block = False
    return pages


def make_file_record(
    role: str,
    page_name: str | None,
    relative_path: str,
    source_url: str,
    data: bytes,
) -> dict[str, object]:
    return {
        "role": role,
        "page_name": page_name,
        "relative_path": relative_path,
        "archive_path": relative_path,
        "source_url": source_url,
        "length": len(data),
        "sha256": sha256(data),
    }


def build_operator_package(
    operator: dict[str, object],
    output_dir: pathlib.Path,
    release_tag: str,
    timeout: int,
    retries: int,
) -> tuple[dict[str, object], list[str]]:
    character_id = require_safe_relative_path(str(operator["id"]), "character ID")
    if "/" in character_id:
        raise ValueError(f"invalid character ID: {character_id}")
    print(f"starting {character_id}", flush=True)
    meta_url = f"https://torappu.prts.wiki/assets/char_spine/{urllib.parse.quote(character_id)}/meta.json"
    meta_bytes = download_bytes(meta_url, timeout, retries)
    meta_text = meta_bytes.decode("utf-8-sig")
    meta = json.loads(meta_text)
    prefix = require_source_url(str(meta.get("prefix") or meta_url.rsplit("/", 1)[0] + "/"))
    character_name = str(meta.get("name") or operator.get("name") or character_id)
    resource_version = sha256(meta_text.encode("utf-8"))[:16]
    skins = meta.get("skin")
    if not isinstance(skins, dict) or not skins:
        raise ValueError(f"PRTS metadata has no skins: {character_id}")

    files_by_path: dict[str, bytes] = {}
    appearances: list[dict[str, object]] = []

    def get_asset(relative_source_path: str) -> tuple[str, str, bytes]:
        source_path = require_safe_relative_path(relative_source_path, "source asset path")
        source_url = require_source_url(urllib.parse.urljoin(prefix, urllib.parse.quote(source_path, safe="/")))
        archive_path = f"operators/{character_id}/{source_path}"
        if archive_path not in files_by_path:
            files_by_path[archive_path] = download_bytes(source_url, timeout, retries)
        return archive_path, source_url, files_by_path[archive_path]

    for skin_name, models in skins.items():
        if not isinstance(models, dict):
            continue
        for model_name, model in models.items():
            if not isinstance(model, dict) or not model.get("file"):
                continue
            file_base = require_safe_relative_path(str(model["file"]), "model file base")
            appearance_files: list[dict[str, object]] = []
            atlas_path, atlas_url, atlas_bytes = get_asset(file_base + ".atlas")
            atlas_record = make_file_record("atlas", None, atlas_path, atlas_url, atlas_bytes)
            appearance_files.append(atlas_record)

            skel_path, skel_url, skel_bytes = get_asset(file_base + ".skel")
            skel_record = make_file_record("skel", None, skel_path, skel_url, skel_bytes)
            appearance_files.append(skel_record)

            pages = parse_atlas_pages(atlas_bytes)
            if not pages:
                raise ValueError(f"atlas has no pages: {character_id} {skin_name}/{model_name}")
            source_directory = file_base.rsplit("/", 1)[0] if "/" in file_base else ""
            for page_name in pages:
                if "/" in page_name:
                    raise ValueError(f"nested atlas page is unsupported: {character_id} {page_name}")
                page_source_path = f"{source_directory}/{page_name}" if source_directory else page_name
                page_path, page_url, page_bytes = get_asset(page_source_path)
                page_record = make_file_record("page", page_name, page_path, page_url, page_bytes)
                appearance_files.append(page_record)

            appearances.append(
                {
                    "skin": str(skin_name),
                    "model": str(model_name),
                    "resource_version": resource_version,
                    "files": appearance_files,
                }
            )

    if not appearances:
        raise ValueError(f"PRTS metadata has no usable appearances: {character_id}")

    packages_dir = output_dir / "packages"
    packages_dir.mkdir(parents=True, exist_ok=True)
    package_name = f"operator-{character_id}.zip"
    package_path = packages_dir / package_name
    with zipfile.ZipFile(package_path, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for archive_path in sorted(files_by_path):
            info = zipfile.ZipInfo(archive_path, date_time=(1980, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o644 << 16
            archive.writestr(info, files_by_path[archive_path], compresslevel=9)

    package_bytes = package_path.read_bytes()
    package_length = len(package_bytes)
    if package_length > TECHNICAL_PACKAGE_LIMIT_BYTES:
        raise ValueError(f"package exceeds 512 MiB technical limit: {character_id} ({package_length} bytes)")
    warnings: list[str] = []
    if package_length >= LARGE_PACKAGE_REPORT_BYTES:
        warnings.append(f"{character_id} package is {package_length} bytes (at least 100 MiB)")
    package_url = (
        f"{RELEASE_ROOT}/{urllib.parse.quote(release_tag, safe='')}/"
        f"{urllib.parse.quote(package_name, safe='')}"
    )
    return (
        {
            "character_id": character_id,
            "character_name": character_name,
            "package_url": package_url,
            "package_length": package_length,
            "package_sha256": sha256(package_bytes),
            "appearances": appearances,
        },
        warnings,
    )


def command_build(args: argparse.Namespace) -> int:
    catalog = json.loads(args.catalog.read_text(encoding="utf-8"))
    operators = list(catalog.get("operators") or [])
    if args.operator_id:
        wanted = set(args.operator_id)
        operators = [operator for operator in operators if operator.get("id") in wanted]
        missing = wanted - {str(operator.get("id")) for operator in operators}
        if missing:
            raise ValueError("operator IDs missing from catalog: " + ", ".join(sorted(missing)))
    else:
        operators = [
            operator
            for position, operator in enumerate(operators)
            if position % args.shard_count == args.shard_index
        ]

    args.output.mkdir(parents=True, exist_ok=True)
    results: list[dict[str, object]] = []
    warnings: list[str] = []
    with concurrent.futures.ThreadPoolExecutor(max_workers=args.workers) as executor:
        futures = {
            executor.submit(
                build_operator_package,
                operator,
                args.output,
                args.release_tag,
                args.timeout,
                args.retries,
            ): str(operator.get("id"))
            for operator in operators
        }
        for future in concurrent.futures.as_completed(futures):
            character_id = futures[future]
            package, package_warnings = future.result()
            results.append(package)
            warnings.extend(package_warnings)
            print(f"built {character_id}: {package['package_length']} bytes", flush=True)

    partial = {
        "schema_version": SCHEMA_VERSION,
        "snapshot_id": args.snapshot_id,
        "release_tag": args.release_tag,
        "operators": sorted(results, key=lambda item: str(item["character_id"])),
        "warnings": warnings,
    }
    partial_path = args.output / f"partial-{args.shard_index:02d}.json"
    partial_path.write_text(json.dumps(partial, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"wrote {partial_path} with {len(results)} operators")
    return 0


def command_merge(args: argparse.Namespace) -> int:
    partial_paths = sorted(args.partials.rglob("partial-*.json"))
    if not partial_paths:
        raise ValueError(f"no partial manifests found under {args.partials}")
    operators: dict[str, dict[str, object]] = {}
    warnings: list[str] = []
    snapshot_id: str | None = None
    release_tag: str | None = None
    for partial_path in partial_paths:
        partial = json.loads(partial_path.read_text(encoding="utf-8"))
        if partial.get("schema_version") != SCHEMA_VERSION:
            raise ValueError(f"schema mismatch in {partial_path}")
        if snapshot_id is None:
            snapshot_id = str(partial["snapshot_id"])
            release_tag = str(partial["release_tag"])
        if partial.get("snapshot_id") != snapshot_id or partial.get("release_tag") != release_tag:
            raise ValueError(f"snapshot mismatch in {partial_path}")
        warnings.extend(str(item) for item in partial.get("warnings") or [])
        for operator in partial.get("operators") or []:
            character_id = str(operator["character_id"])
            if character_id in operators:
                raise ValueError(f"duplicate operator in partial manifests: {character_id}")
            operators[character_id] = operator
    if len(operators) != args.expected_count:
        raise ValueError(f"expected {args.expected_count} operators, found {len(operators)}")
    manifest = {
        "schema_version": SCHEMA_VERSION,
        "snapshot_id": snapshot_id,
        "release_tag": release_tag,
        "operators": [operators[key] for key in sorted(operators)],
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"wrote {args.output} with {len(operators)} operators")
    for warning in warnings:
        print(f"warning: {warning}", file=sys.stderr)
    return 0


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)

    build = subparsers.add_parser("build", help="build one package shard")
    build.add_argument("--catalog", type=pathlib.Path, required=True)
    build.add_argument("--output", type=pathlib.Path, required=True)
    build.add_argument("--release-tag", required=True)
    build.add_argument("--snapshot-id", required=True)
    build.add_argument("--shard-index", type=int, default=0)
    build.add_argument("--shard-count", type=int, default=1)
    build.add_argument("--operator-id", action="append")
    build.add_argument("--workers", type=int, default=3)
    build.add_argument("--timeout", type=int, default=180)
    build.add_argument("--retries", type=int, default=3)
    build.set_defaults(func=command_build)

    merge = subparsers.add_parser("merge", help="merge shard manifests")
    merge.add_argument("--partials", type=pathlib.Path, required=True)
    merge.add_argument("--output", type=pathlib.Path, required=True)
    merge.add_argument("--expected-count", type=int, required=True)
    merge.set_defaults(func=command_merge)
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    if args.command == "build":
        if args.shard_count <= 0 or args.shard_index < 0 or args.shard_index >= args.shard_count:
            raise ValueError("invalid shard index/count")
        if args.workers <= 0:
            raise ValueError("workers must be positive")
    return int(args.func(args))


if __name__ == "__main__":
    raise SystemExit(main())
