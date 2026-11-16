#!/usr/bin/env python3
"""docs/bible/data/*.json -> docs/bible/bundle.js (web) + docs/bible/md/*.md (docs). Usage: python3 tools/bible_build.py"""
import glob, json, os, sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "docs", "bible")
STATUS = {"confirmed": "확정", "proposed": "제안", "open": "미정"}


def cell(v):
    return str(v if v is not None else "").replace("|", "\\|").replace("\n", " ")


def to_md(sec):
    out = ["# %s" % sec["title"], "", "> 상태: %s · 요약: %s" % (STATUS.get(sec.get("status", ""), "-"), sec.get("summary", "")), ""]
    for p in sec.get("parts", []):
        out.append("## %s" % p.get("title", ""))
        out.append("")
        t = p.get("type")
        if t == "prose":
            for para in p.get("paragraphs", []):
                out += [para, ""]
            for b in p.get("bullets", []):
                out.append("- " + b)
            out.append("")
        elif t == "table":
            cols = p.get("columns", [])
            heads = ["상태"] + [c.get("label", c["key"]) for c in cols] + ["근거"]
            out.append("| " + " | ".join(heads) + " |")
            out.append("|" + "---|" * len(heads))
            for r in p.get("rows", []):
                vals = [STATUS.get(r.get("status", ""), "")] + [cell(r.get(c["key"], "")) for c in cols] + [cell(r.get("source", ""))]
                out.append("| " + " | ".join(vals) + " |")
            out.append("")
        elif t == "decisions":
            for d in p.get("items", []):
                box = "x" if d.get("status") == "confirmed" else " "
                out.append("- [%s] **%s** (%s) %s" % (box, d.get("q", ""), STATUS.get(d.get("status", ""), ""), "→ " + d["answer"] if d.get("answer") else ""))
                if d.get("options"):
                    out.append("  - 선택지: " + " / ".join(d["options"]) + ("  · 추천: " + d["recommended"] if d.get("recommended") else ""))
                if d.get("why"):
                    out.append("  - 근거: " + d["why"])
            out.append("")
    return "\n".join(out) + "\n"


def main():
    secs, errors = [], []
    for path in sorted(glob.glob(os.path.join(ROOT, "data", "*.json"))):
        try:
            sec = json.load(open(path, encoding="utf-8"))
        except Exception as e:  # keep building the rest
            errors.append("%s: %s" % (os.path.basename(path), e))
            continue
        if sec.get("id") != os.path.splitext(os.path.basename(path))[0]:
            errors.append("%s: id '%s' != file name" % (os.path.basename(path), sec.get("id")))
        secs.append(sec)
    secs.sort(key=lambda s: (s.get("order", 999), s.get("id", "")))
    os.makedirs(os.path.join(ROOT, "md"), exist_ok=True)
    for s in secs:
        open(os.path.join(ROOT, "md", s["id"] + ".md"), "w", encoding="utf-8").write(to_md(s))
    open(os.path.join(ROOT, "bundle.js"), "w", encoding="utf-8").write("window.BIBLE = " + json.dumps(secs, ensure_ascii=False) + ";\n")
    tpl = open(os.path.join(ROOT, "index.template.html"), encoding="utf-8").read()
    bundle = "window.BIBLE = " + json.dumps(secs, ensure_ascii=False).replace("</", "<\\/") + ";"
    open(os.path.join(ROOT, "index.html"), "w", encoding="utf-8").write(tpl.replace("/*BUNDLE*/", bundle))
    print("built %d sections" % len(secs))
    for e in errors:
        print("ERROR", e)
    sys.exit(1 if errors else 0)


main()
