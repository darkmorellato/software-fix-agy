#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Converte os dicionarios de idioma do Software Fix (lang/<locale>/<locale>)
— que sao ResourceDictionaries XAML em texto puro — para arquivos JSON
chave/valor, garantindo paridade 100% de textos, avisos e mensagens de erro
entre o aplicativo original e o port Linux.

Saida: configs_and_manifests/lang_json/<locale>.json  (18 idiomas)
"""

import json
import xml.etree.ElementTree as ET
from pathlib import Path

SOURCE = Path("/run/media/dark/1224A54224A529A5/Program Files/Software Fix/lang")
DEST = Path(__file__).resolve().parent.parent / "configs_and_manifests" / "lang_json"
XKEY = "{http://schemas.microsoft.com/winfx/2006/xaml}Key"


def convert(locale_file: Path) -> dict:
    root = ET.parse(locale_file).getroot()
    out = {}
    for el in root.iter():
        if el.tag.endswith("String") and XKEY in el.attrib:
            out[el.attrib[XKEY]] = el.text or ""
    return out


def main():
    DEST.mkdir(parents=True, exist_ok=True)
    count = 0
    for loc in sorted(SOURCE.iterdir()):
        f = loc / loc.name
        if not f.is_file():
            continue
        data = convert(f)
        out = DEST / f"{loc.name}.json"
        out.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"  {loc.name}: {len(data)} chaves -> {out.name}")
        count += 1
    print(f"Total: {count} locales convertidos para {DEST}")


if __name__ == "__main__":
    main()
