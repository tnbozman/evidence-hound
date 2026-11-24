#!/usr/bin/env python3
"""Check that relative links in Markdown files resolve: the file or folder exists and, for a #section, the heading does.

Usage: python3 -I scripts/check-links.py <folder>   (make lint-docs runs it on the repository)

External links (http, https, mailto) are not fetched. Links inside code blocks and inline code are not links.
Sprint plans, their evidence logs and test fixtures are skipped when the folder is the repository root.
Exit status: 0 when every link resolves, 1 when any does not (each is printed as file:line: target: reason).
"""

import html
import re
import sys
from pathlib import Path

SKIPPED = ("docs/sprints/", "docs/evidence/", "tests/", ".dev/", ".git/")
SKIPPED_PARTS = {"node_modules", "bin", "obj"}
LINK = re.compile(r"!?\[(?:[^\]\[]|\[[^\]]*\])*\]\(\s*<?([^)\s>]+)>?(?:\s+\"[^\"]*\")?\s*\)")
FENCE = re.compile(r"^\s*(```|~~~)")
INLINE_CODE = re.compile(r"(`+)(?:(?!\1).)+\1")
HEADING = re.compile(r"^\s{0,3}(#{1,6})\s+(.*?)\s*#*\s*$")
EXTERNAL = re.compile(r"^[a-z][a-z0-9+.-]*:", re.IGNORECASE)


def slug(heading: str) -> str:
    """GitHub's anchor for a heading: lower case, punctuation dropped, spaces to hyphens."""
    text = re.sub(r"`", "", html.unescape(heading)).strip().lower()
    text = re.sub(r"\[([^\]]*)\]\([^)]*\)", r"\1", text)
    text = re.sub(r"[^\w\- ]", "", text)
    return text.replace(" ", "-")


def lines_outside_code(path: Path):
    """Each line number and line that is not inside a fenced code block."""
    fenced = False
    for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
        if FENCE.match(line):
            fenced = not fenced
            continue
        if not fenced:
            yield number, line


def anchors(path: Path, cache: dict) -> set:
    if path not in cache:
        found, seen = set(), {}
        for _, line in lines_outside_code(path):
            match = HEADING.match(line)
            if match:
                base = slug(match.group(2))
                count = seen.get(base, 0)
                seen[base] = count + 1
                found.add(base if count == 0 else f"{base}-{count}")
        cache[path] = found
    return cache[path]


def problems(root: Path):
    cache = {}
    skip_repo_paths = (root / ".git").exists()
    for path in sorted(root.rglob("*.md")):
        relative = path.relative_to(root).as_posix()
        if SKIPPED_PARTS.intersection(path.relative_to(root).parts):
            continue
        if skip_repo_paths and relative.startswith(SKIPPED):
            continue
        for number, line in lines_outside_code(path):
            for match in LINK.finditer(INLINE_CODE.sub("", line)):
                target = match.group(1)
                if EXTERNAL.match(target):
                    continue
                file_part, _, section = target.partition("#")
                resolved = (path.parent / file_part).resolve() if file_part else path
                if not resolved.exists():
                    yield f"{relative}:{number}: {target}: no such file"
                elif section and resolved.suffix == ".md" and section.lower() not in anchors(resolved, cache):
                    yield f"{relative}:{number}: {target}: no such section"


def main(argv):
    if len(argv) != 2:
        print(__doc__.strip().splitlines()[2], file=sys.stderr)
        return 2
    found = list(problems(Path(argv[1]).resolve()))
    for problem in found:
        print(problem)
    return 1 if found else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
