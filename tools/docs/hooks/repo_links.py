"""MkDocs hook: links from a docs page to a file outside the site (code, examples, deploy files, sprint plans) point at
the file on GitHub, so the site has no broken links and strict mode stays on. Links between site pages are untouched.

The repository's own link check (scripts/check-links.py) already proves every such file exists.
"""

import posixpath
import re

REPO = "https://github.com/tnbozman/evidence-hound"
LINK = re.compile(r"(?P<prefix>!?\[(?:[^\]\[]|\[[^\]]*\])*\]\()(?P<target>[^)\s#]*)(?P<rest>#[^)\s]*)?(?P<suffix>(?:\s+\"[^\"]*\")?\))")
FENCE = re.compile(r"^\s*(```|~~~)")
EXTERNAL = re.compile(r"^[a-z][a-z0-9+.-]*:", re.IGNORECASE)
OFF_SITE_DOCS = ("sprints/", "evidence/")


def _github_url(page_src: str, target: str, rest: str) -> str | None:
    """The GitHub URL for a relative target that is not a page on the site, else None."""
    if not target or EXTERNAL.match(target) or target.startswith("/"):
        return None
    in_docs = posixpath.normpath(posixpath.join(posixpath.dirname(page_src), target))
    if not in_docs.startswith("../") and not in_docs.startswith(OFF_SITE_DOCS) and not in_docs.endswith(".pdf"):
        return None
    repo_path = posixpath.normpath(posixpath.join("docs", in_docs))
    kind = "tree" if target.endswith("/") else "blob"
    return f"{REPO}/{kind}/main/{repo_path}{'/' if kind == 'tree' else ''}{rest or ''}"


def on_page_markdown(markdown, page, config, files):
    lines, fenced = [], False
    for line in markdown.split("\n"):
        if FENCE.match(line):
            fenced = not fenced
        elif not fenced:
            line = LINK.sub(
                lambda m: f"{m['prefix']}{url}{m['suffix']}"
                if (url := _github_url(page.file.src_uri, m["target"], m["rest"])) else m.group(0),
                line,
            )
        lines.append(line)
    return "\n".join(lines)
