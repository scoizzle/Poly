#!/usr/bin/env python3
"""Fails when a relative Markdown link under docs/ or .github/ points at a missing file.

Usage: python3 scripts/check-doc-links.py   (from anywhere in the repo)
Skips URLs, mail links and in-page anchors; checks the path part of everything else.
"""
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
SCOPES = ["docs", ".github"]
LINK = re.compile(r"\[[^\]]*\]\(([^)\s]+)(?:\s+\"[^\"]*\")?\)")
FENCE = re.compile(r"^\s*(```|~~~)")


def links(path):
    in_fence = False
    for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        if FENCE.match(line):
            in_fence = not in_fence
            continue
        if in_fence:
            continue
        for match in LINK.finditer(re.sub(r"`[^`]*`", "", line)):
            yield number, match.group(1)


def main():
    broken = []
    for scope in SCOPES:
        for path in sorted((ROOT / scope).rglob("*.md")):
            for number, target in links(path):
                if re.match(r"^[a-z][a-z0-9+.-]*:", target) or target.startswith("#"):
                    continue
                file_part = target.split("#", 1)[0]
                if not file_part:
                    continue
                resolved = (path.parent / file_part).resolve()
                if not resolved.exists():
                    broken.append(f"{path.relative_to(ROOT)}:{number}: {target}")
    for line in broken:
        print(line)
    if broken:
        print(f"{len(broken)} broken relative link(s).", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
