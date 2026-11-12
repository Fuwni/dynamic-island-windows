"""Build Assets/lang/<code>.json from tools/i18n/strings*.tsv and report gaps against catalog.json.

    python tools/i18n/extract.py   # refresh the list of strings the app uses
    python tools/i18n/build.py     # write the tables, list anything untranslated

Each .tsv row: the Russian original, then en, uk, es, de, pt, zh (tab-separated).
To add a language, add a column.
"""
import glob
import json
import os
import re

here = os.path.dirname(os.path.abspath(__file__))
root = os.path.join(here, "..", "..")
rows, header = [], None
for path in sorted(glob.glob(os.path.join(here, "strings*.tsv"))):
    lines = open(path, encoding="utf-8").read().split("\n")
    header = lines[0].split("\t")
    for line in lines[1:]:
        if line.strip():
            rows.append(line.split("\t"))

langs = header[1:]
catalog = json.load(open(os.path.join(here, "catalog.json"), encoding="utf-8"))
placeholder = re.compile(r"\{\d+(?::[^}]*)?\}")
problems = 0
tables = {code: {} for code in langs}
for r in rows:
    if len(r) != len(header):
        print("bad row:", r[0][:60], len(r))
        problems += 1
        continue
    ru = r[0]
    for code, text in zip(langs, r[1:]):
        if sorted(placeholder.findall(text)) != sorted(placeholder.findall(ru)):
            print(f"placeholders differ [{code}]: {ru[:50]}")
            problems += 1
        tables[code][ru] = text

os.makedirs(os.path.join(root, "Assets", "lang"), exist_ok=True)
for code, table in tables.items():
    json.dump(table, open(os.path.join(root, "Assets", "lang", f"{code}.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)

missing = [k for k in catalog if k not in tables[langs[0]]]
unused = [k for k in tables[langs[0]] if k not in catalog]
print(f"{len(rows)} strings x {len(langs)} languages, {len(missing)} missing, {len(unused)} unused, {problems} problems")
for k in missing:
    print("  missing:", k)
