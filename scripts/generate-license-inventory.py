#!/usr/bin/env python3
"""Generate deterministic NuGet/pnpm license inventory, notices and CycloneDX SBOM."""
import argparse
import glob
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
ALLOWED = {"Apache-2.0", "MIT", "BSD-2-Clause", "BSD-3-Clause", "ISC", "0BSD", "MPL-2.0", "Python-2.0", "BlueOak-1.0.0", "CC-BY-4.0", "PostgreSQL"}
DENIED = re.compile(r"(GPL|AGPL|LGPL|SSPL|BUSL|Elastic|Commons-Clause|UNLICENSED|Unknown)", re.I)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def nuget_components():
    package_root = Path.home() / ".nuget" / "packages"
    overrides = json.loads((ROOT / "deploy/license-overrides.json").read_text()) if (ROOT / "deploy/license-overrides.json").exists() else {}
    found = {}
    for assets in glob.glob(str(ROOT / "src" / "**" / "obj" / "project.assets.json"), recursive=True) + glob.glob(str(ROOT / "tests" / "**" / "obj" / "project.assets.json"), recursive=True):
        for name, library in json.loads(Path(assets).read_text()).get("libraries", {}).items():
            if library.get("type") != "package" or "/" not in name:
                continue
            package, version = name.rsplit("/", 1)
            key = (package.lower(), version)
            if key in found:
                continue
            directory = package_root / Path(library["path"])
            nuspec = next(directory.glob("*.nuspec"), None)
            license_id = "Unknown"
            if nuspec:
                root = ET.parse(nuspec).getroot()
                node = next((item for item in root.iter() if item.tag.rsplit("}", 1)[-1] == "license"), None)
                if node is not None:
                    expression = (node.text or "").strip() or node.attrib.get("value", "").strip()
                    if node.attrib.get("type") == "file" and (directory / expression).exists():
                        license_id = infer_license_text((directory / expression).read_text(errors="ignore"))
                    elif expression and expression != "LICENSE":
                        license_id = expression
                    else:
                        license_id = infer_license(node.attrib.get("value", ""))
                if license_id == "Unknown":
                    license_url = next((item.text or "" for item in root.iter() if item.tag.rsplit("}", 1)[-1] == "licenseUrl"), "")
                    license_id = infer_license(license_url)
            override = overrides.get(f"{package.lower()}@{version}")
            if override:
                license_id = override["spdx"]
            found[key] = component("nuget", package, version, license_id, f"nuget:{package.lower()}@{version}")
    return list(found.values())


def infer_license(url):
    lowered = url.lower()
    for candidate in ("apache-2.0", "mit", "bsd-3-clause", "bsd-2-clause", "isc", "mpl-2.0"):
        if candidate in lowered:
            return candidate
    return "Unknown"


def npm_components():
    result = subprocess.run(["corepack", "pnpm", "licenses", "list", "--json"], cwd=ROOT,
                            capture_output=True, text=True, check=True)
    catalog = json.loads(result.stdout)
    found = {}
    for license_id, packages in catalog.items():
        for package in packages:
            for version in package.get("versions", []):
                name = package["name"]
                found[(name, version)] = component("npm", name, version, license_id, f"npm:{name}@{version}")
    for manifest_path in list((ROOT / "apps").glob("*/package.json")) + list((ROOT / "packages").glob("*/package.json")):
        manifest = json.loads(manifest_path.read_text())
        for section in ("dependencies", "devDependencies"):
            for name, spec in manifest.get(section, {}).items():
                if spec.startswith("workspace:"):
                    continue
                candidates = [manifest_path.parent / "node_modules" / name / "package.json", ROOT / "node_modules" / name / "package.json"]
                metadata_path = next((path for path in candidates if path.exists()), None)
                if metadata_path is None:
                    continue
                metadata = json.loads(metadata_path.read_text())
                version = metadata["version"]
                license_id = metadata.get("license") or "Unknown"
                if isinstance(license_id, dict):
                    license_id = license_id.get("type", "Unknown")
                found[(name, version)] = component("npm", name, version, license_id, f"npm:{name}@{version}")
    return list(found.values())


def infer_license_text(text):
    lowered = text.lower()
    if "creativecommons.org/licenses/by/4.0" in lowered or "creative commons attribution 4.0" in lowered:
        return "CC-BY-4.0"
    if "apache license" in lowered and "version 2.0" in lowered:
        return "Apache-2.0"
    if "permission is hereby granted, free of charge" in lowered:
        return "MIT"
    if "redistribution and use in source and binary forms" in lowered:
        return "BSD-2-Clause" if "neither the name" in lowered else "BSD-3-Clause"
    if "permission to use, copy, modify, and/or distribute" in lowered:
        return "ISC"
    return "Unknown"


def component(ecosystem, name, version, license_id, purl):
    return {
        "ecosystem": ecosystem, "name": name, "version": version, "purl": purl,
        "spdx": license_id,
        "copyleft": bool(re.search(r"GPL|AGPL|LGPL|SSPL|MPL", license_id, re.I)),
        "compatibility": "allowed" if compatible(license_id) else "blocked"
    }


def compatible(expression):
    if not expression or expression == "Unknown" or DENIED.search(expression):
        return False
    ids = set(re.findall(r"[A-Za-z0-9][A-Za-z0-9.+-]*", expression)) - {"AND", "OR", "WITH", "LicenseRef-scancode-unknown"}
    return bool(ids) and ids <= ALLOWED


def documents(components):
    components = sorted(components, key=lambda item: (item["ecosystem"], item["name"].lower(), item["version"]))
    artifact = digest(json.dumps(components, sort_keys=True, separators=(",", ":")).encode())
    inventory = {"schema": "regent-license-inventory/v1", "artifact_digest": artifact,
                 "component_count": len(components), "components": components}
    sbom = {"bomFormat": "CycloneDX", "specVersion": "1.6", "version": 1,
            "metadata": {"component": {"type": "application", "name": "regent-agent", "bom-ref": "pkg:generic/regent-agent@source"}},
            "components": [{"type": "library", "name": item["name"], "version": item["version"],
                            "purl": item["purl"], "licenses": [{"license": {"id": item["spdx"]}}] if compatible(item["spdx"]) else [],
                            "properties": [{"name": "regent:compatibility", "value": item["compatibility"]}]}
                           for item in components]}
    notices = ["# Third-party notices", "", "Generated from NuGet and pnpm dependency metadata. Original license texts remain with their packages.", ""]
    for item in components:
        notices.append(f"- `{item['name']} {item['version']}` — {item['spdx']} ({item['ecosystem']})")
    return inventory, sbom, "\n".join(notices) + "\n"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    components = nuget_components() + npm_components()
    inventory, sbom, notices = documents(components)
    blocked = [item for item in inventory["components"] if item["compatibility"] != "allowed"]
    outputs = {
        ROOT / "deploy/license-inventory.json": json.dumps(inventory, indent=2, sort_keys=True) + "\n",
        ROOT / "deploy/sbom.json": json.dumps(sbom, indent=2, sort_keys=True) + "\n",
        ROOT / "docs/third-party-notices.md": notices,
    }
    for path, content in outputs.items():
        if args.check and path.read_text() != content:
            raise SystemExit(f"License/SBOM artifact is stale: {path.relative_to(ROOT)}")
        if not args.check:
            path.write_text(content)
    if blocked:
        for item in blocked:
            print(f"BLOCKED: {item['ecosystem']} {item['name']} {item['version']} license={item['spdx']}")
        raise SystemExit(1)
    print(f"PASS: {len(components)} dependencies, 0 unknown/blocked licenses, artifact={inventory['artifact_digest'][:12]}")


if __name__ == "__main__":
    main()
