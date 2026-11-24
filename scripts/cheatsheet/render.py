#!/usr/bin/env python3
"""Render docs/cheatsheet.md as a two-page HTML cheat sheet: boxed command groups on page 1, usage paths on page 2."""
import html
import re
import sys

def inline(text):
    text = html.escape(text)
    # Each token unbroken, so an option never wraps after its hyphens.
    text = re.sub(r"`([^`]+)`", lambda m: "<code>" + " ".join(f'<span class="t">{t}</span>' for t in m.group(1).split(" ")) + "</code>", text)
    return re.sub(r"\*\*([^*]+)\*\*", r'<b class="p">\1</b>', text)

def parse(md):
    title, intro, sections, current, box = "", [], {}, None, None
    for line in md.splitlines():
        if line.startswith("# "):
            title = line[2:]
        elif line.startswith("## "):
            current = sections.setdefault(line[3:], [])
        elif line.startswith("### "):
            box = {"title": line[4:], "items": []}
            current.append(box)
        elif current is None and line.strip():
            intro.append(line.strip())
        elif box is not None and re.match(r"(- |\d+\. )", line):
            box["items"].append({"text": re.sub(r"^(- |\d+\. )", "", line), "opts": []})
        elif box is not None and line.startswith("  - "):
            box["items"][-1]["opts"].append(line[4:])
    return title, " ".join(intro), sections

def command(item):
    name, _, desc = item["text"].partition(": ")
    opts = "".join(f'<div class="o">{inline(o)}</div>' for o in item["opts"])
    return f'<div class="c"><div class="n">{inline(name)}</div><div class="d">{inline(desc)}</div>{opts}</div>'

def page(title, sub, boxes, render, cls):
    body = "".join(f'<section class="box"><h2>{inline(b["title"])}</h2>{render(b)}</section>' for b in boxes)
    return f'<div class="page"><header><h1>{html.escape(title)}</h1><span>{sub}</span></header><div class="{cls}">{body}</div></div>'

def main(src, out):
    title, intro, sections = parse(open(src, encoding="utf-8").read())
    intro = inline(re.sub(r" The PDF is rendered.*$", "", intro))
    commands = page(title, intro, sections["Commands"], lambda b: "".join(command(i) for i in b["items"]), "cols")
    paths = page(title + ": typical paths", "Agent steps run in the person's coding assistant; <b class=\"p\">Person</b> steps need a terminal.",
                 sections["Typical paths"], lambda b: "<ol>" + "".join(f"<li>{inline(i['text'])}</li>" for i in b["items"]) + "</ol>", "grid")
    css = """@page{size:A4 landscape;margin:7mm}*{box-sizing:border-box}
body{font-family:"DejaVu Sans",Arial,sans-serif;font-size:6.9pt;color:#111;margin:0;background:#fff}
.page{page-break-after:always;height:196mm;overflow:hidden}.page:last-child{page-break-after:auto}
header{display:flex;align-items:baseline;gap:10px;border-bottom:2px solid #111;margin-bottom:4px;padding-bottom:2px}
h1{font-size:15pt;margin:0;white-space:nowrap;font-family:"DejaVu Sans Mono",monospace}header span{font-size:6.8pt;color:#333}
.cols{column-count:4;column-gap:5px}.grid{display:grid;grid-template-columns:repeat(4,1fr);gap:6px}
.box{break-inside:avoid;border:1.2px solid #111;border-radius:3px;margin:0 0 5px;padding:0 4px 3px}
h2{font-size:7.6pt;margin:0 -4px 3px;padding:2px 4px;background:#111;color:#fff;letter-spacing:.2px}
.c{margin:0 0 3px;break-inside:avoid}.n code{font-weight:bold;font-size:7pt;background:none;padding:0}
.d{margin-left:6px;color:#222}.o{margin-left:6px;color:#444}
code{font-family:"DejaVu Sans Mono",monospace;font-size:6.3pt;background:#eef0f3;padding:0 1px;border-radius:2px}
.grid .box{font-size:9.5pt;padding:2px 6px 5px}.grid h2{font-size:10.5pt;margin:0 -6px 4px;padding:3px 6px}
.grid code{font-size:8.6pt}ol{margin:0;padding-left:17px}li{margin:0 0 6px}.grid{gap:9px}b.p{color:#a11}.t{white-space:nowrap}"""
    with open(out, "w", encoding="utf-8") as f:
        f.write(f'<!doctype html><html><head><meta charset="utf-8"><style>{css}</style></head><body>{commands}{paths}</body></html>')

if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
