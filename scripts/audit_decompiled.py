#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Auditoria de cobertura da descompilação .NET — Software Fix (Lenovo/Motorola).

Compara cada assembly-alvo (raiz + plugins + extras) com o que existe em
src_decompiled/ e reporta: COBERTO, VAZIO, FALTANTE ou NATIVO.

Uso: python3 scripts/audit_decompiled.py
"""

import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path

SOURCE_DIR = Path("/run/media/dark/1224A54224A529A5/Program Files/Software Fix")
PROJECT_ROOT = Path("/home/dark/software-fix-agy")
DEST_SRC_DIR = PROJECT_ROOT / "src_decompiled"

# DLLs de terceiros/runtime ignoradas por padrão (mesma política do extrator)
IGNORED_DLL_PREFIXES = (
    "Microsoft.", "System.", "Newtonsoft.", "GoogleAnalytics.",
    "WebView2", "libmp3lame", "vcredist", "AdbWin",
)

# Binários nativos conhecidos (não são .NET, não há o que descompilar)
KNOWN_NATIVE = {
    "adb.exe", "fastboot.exe", "fastbootmonitor.exe",
    "AdbWinApi.dll", "AdbWinUsbApi.dll", "WebView2Loader.dll",
    "libmp3lame.32.dll", "7z.dll", "7z64.dll",
    "vcredist_x86_2010.exe", "InstallUtil64.exe", "Uninstall.exe",
}


def cs_count(d: Path) -> int:
    return len(list(d.rglob("*.cs"))) if d.is_dir() else -1


def should_decompile_root(p: Path) -> bool:
    name = p.name
    lower = name.lower()
    if lower.endswith(".exe"):
        return lower in ("software fix.exe", "lmsawindowsservice.exe")
    if lower.endswith(".dll"):
        if name.startswith("lenovo."):
            return True
        if lower in ("sharpadbclient.dll", "bmaframe9.dll"):
            return True
    return False


def is_native_assembly(p: Path) -> bool:
    """True se o PE não contém metadados .NET gerenciados."""
    try:
        proc = subprocess.run(
            ["ilspycmd", "-l", "c", str(p)],
            capture_output=True, text=True, timeout=120)
        return "managed metadata" in (proc.stderr or "")
    except Exception:
        return False


def plugin_friendly_name(guid: str) -> str:
    plugins_xml = SOURCE_DIR / "plugins.xml"
    if plugins_xml.exists():
        try:
            tree = ET.parse(plugins_xml)
            for plugin in tree.findall(".//Plugin"):
                if plugin.attrib.get("PluginID", "").lower() == guid.lower():
                    name = plugin.attrib.get("PluginName", guid)
                    clean = name.replace("\n", "").replace("&amp;", "&").replace(" ", "_")
                    return f"{clean}_{guid}"
        except Exception:
            pass
    return guid


def status_for(out_dir: Path) -> str:
    n = cs_count(out_dir)
    if n < 0:
        return "FALTANTE"
    if n == 0:
        return "VAZIO"
    return f"COBERTO ({n} .cs)"


def main():
    print("=" * 74)
    print("  AUDITORIA DE COBERTURA DA DESCOMPILAÇÃO — SOFTWARE FIX")
    print("=" * 74)

    print("\n### 1. ASSEMBLIES RAIZ (alvo da tarefa)")
    root_missing, root_ok, root_empty = [], [], []
    for f in sorted(SOURCE_DIR.iterdir()):
        if f.is_file() and f.suffix.lower() in (".dll", ".exe"):
            if should_decompile_root(f):
                out = DEST_SRC_DIR / f.stem.replace(" ", "_")
                st = status_for(out)
                if st == "VAZIO" and (f.name in KNOWN_NATIVE or is_native_assembly(f)):
                    st = "NATIVO (OK - sem metadados .NET)"
                tag = "OK " if (st.startswith("COBERTO") or st.startswith("NATIVO")) else "!! "
                print(f"  [{tag}] {f.name:52s} -> {out.name:44s} {st}")
                if st == "FALTANTE":
                    root_missing.append(f)
                elif st == "VAZIO":
                    root_empty.append(f)
                else:
                    root_ok.append(f)

    print("\n### 2. DLLs RAIZ NÃO-ALVO (informação)")
    for f in sorted(SOURCE_DIR.iterdir()):
        if f.is_file() and f.suffix.lower() in (".dll", ".exe"):
            if not should_decompile_root(f):
                native = f.name in KNOWN_NATIVE
                kind = "NATIVO/SISTEMA" if native else "3rd-party (ver _extras_ref)"
                print(f"  [--] {f.name:52s} {f.stat().st_size:>10,} b  {kind}")

    print("\n### 3. DLLs DOS PLUGINS (varredura recursiva)")
    plug_ok, plug_missing, plug_empty, plug_ignored = [], [], [], []
    src_plugins = SOURCE_DIR / "plugins"
    if src_plugins.is_dir():
        for pdir in sorted(src_plugins.iterdir()):
            if not pdir.is_dir():
                continue
            friendly = plugin_friendly_name(pdir.name)
            base = DEST_SRC_DIR / "plugins" / friendly
            print(f"\n  -- plugin {pdir.name[:12]}... -> {friendly}")
            for dll in sorted(pdir.rglob("*.dll")):
                rel = dll.relative_to(pdir)
                if dll.name.startswith(IGNORED_DLL_PREFIXES):
                    plug_ignored.append(str(rel))
                    print(f"     [skip] {str(rel):68s} (sistema/MS)")
                    continue
                out = base / dll.stem.replace(" ", "_")
                st = status_for(out)
                tag = "OK " if st.startswith("COBERTO") else "!! "
                print(f"     [{tag}] {str(rel):68s} {st}")
                if st == "FALTANTE":
                    plug_missing.append((dll, out))
                elif st == "VAZIO":
                    plug_empty.append((dll, out))
                else:
                    plug_ok.append((dll, out))

    print("\n### 4. CONFIGS E MANIFESTOS")
    cfg_dir = PROJECT_ROOT / "configs_and_manifests"
    for f in sorted(SOURCE_DIR.iterdir()):
        if f.is_file() and f.suffix.lower() in (".xml", ".config", ".installstate"):
            ok = (cfg_dir / f.name).exists()
            print(f"  [{'OK ' if ok else '!! '}] {f.name}")
    for sub in ("lang", "lang_json", "plugins"):
        ok = (cfg_dir / sub).is_dir()
        print(f"  [{'OK ' if ok else '!! '}] {sub}/ (pasta copiada/conversão)")

    print("\n### 5. RECURSOS EXTRAÍDOS (XAML/assets)")
    xaml = list(DEST_SRC_DIR.rglob("*.xaml"))
    resx = list(DEST_SRC_DIR.rglob("*.resx"))
    baml = list(DEST_SRC_DIR.rglob("*.baml"))
    others = {}
    for f in DEST_SRC_DIR.rglob("*"):
        if f.is_file() and f.suffix.lower() not in (".cs", ".xaml", ".resx", ".baml"):
            others[f.suffix.lower()] = others.get(f.suffix.lower(), 0) + 1
    print(f"  XAML: {len(xaml)} | BAML crus restantes: {len(baml)} | RESX: {len(resx)}")
    print(f"  assets: {others}")

    print("\n" + "=" * 74)
    print("  RESUMO DA AUDITORIA")
    print("=" * 74)
    print(f"  Raiz     -> OK: {len(root_ok)} | VAZIOS: {len(root_empty)} | FALTANTES: {len(root_missing)}")
    print(f"  Plugins  -> OK: {len(plug_ok)} | VAZIOS: {len(plug_empty)} | FALTANTES: {len(plug_missing)} | ignorados: {len(plug_ignored)}")
    if root_empty:
        print("\n  VAZIOS na raiz:")
        for f in root_empty:
            print(f"    - {f.name}")
    if root_missing:
        print("\n  FALTANTES na raiz:")
        for f in root_missing:
            print(f"    - {f.name}")
    if plug_empty:
        print("\n  VAZIOS em plugins:")
        for dll, out in plug_empty:
            print(f"    - {dll.name} -> {out}")
    if plug_missing:
        print("\n  FALTANTES em plugins:")
        for dll, out in plug_missing:
            print(f"    - {dll.name} -> {out}")
    print("\n  Fim da auditoria.")


if __name__ == "__main__":
    main()
