"""Collect every Russian UI string (L.T / L.F in code, texts in XAML) into tools/i18n/catalog.json.

    python tools/i18n/extract.py

Then translate the missing keys in Assets/lang/<code>.json (check.py lists them).
"""
import glob
import html
import json
import os
import re

root = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
os.chdir(root)
cyr = re.compile(r"[А-Яа-яЁё]")
keys = []


def add(s):
    s = html.unescape(s)
    if cyr.search(s) and s not in keys:
        keys.append(s)


skip = ("Demo", "Promo", "Review", "Brand", "LiveDemo")
for path in glob.glob("**/*.cs", recursive=True):
    if path.startswith(("obj", "bin")) or any(k in path for k in skip):
        continue
    src = open(path, encoding="utf-8-sig").read()
    for m in re.finditer(r'L\.[TF]\("((?:[^"\\]|\\.)*)"', src):
        add(m.group(1).replace('\\"', '"'))

for path in ["MainWindow.xaml", "App.xaml"] + glob.glob("Views/*.xaml"):
    src = open(path, encoding="utf-8-sig").read()
    src = re.sub(r"<!--.*?-->", "", src, flags=re.S)
    for m in re.finditer(r'\b(?:Text|Content|ToolTip|Header|Tag|Title)="([^"]*)"', src):
        v = m.group(1)
        if "|" in v:
            v = v.split("|")[0]
        if v.startswith("{"):
            continue
        add(v)
    for m in re.finditer(r"<Run[^>]*>([^<]+)</Run>", src):
        add(m.group(1).strip())

json.dump(keys, open("tools/i18n/catalog.json", "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print(len(keys), "strings")
