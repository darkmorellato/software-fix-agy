#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
=============================================================================
 EXTRATOR E DESCOMPILADOR .NET — SOFTWARE FIX (LENOVO / MOTOROLA)
=============================================================================
Automatiza a engenharia reversa estatica do aplicativo "Software Fix"
(Lenovo / Motorola - Rescue and Smart Assistant, .NET/WPF) para replicacao
funcional no port Linux "software-fix-agy".

Versao 2 - aprimorada para paridade 100% (visual e de protocolo):
  * Converte recursos BAML (XAML compilado do WPF) em arquivos .xaml
    legiveis usando ilspycmd >= 10.1 (o modo -p descompila BAML
    automaticamente; --decompile-baml mantida por compatibilidade).
  * Pos-processamento: .baml restantes como bytes crus sao convertidos
    individualmente via --resource (cobre recursos que o modo -p nao
    associa a Pages, ex.: styles sem code-behind).
  * Cobertura completa: assemblies da raiz, varredura recursiva de
    plugins/ e bibliotecas de terceiros para referencia (_extras_ref/).
  * Copia configuracoes (.xml/.config), manifestos, idiomas (lang/) e
    metadados de plugins para configs_and_manifests/.
  * Relatorio final em JSON + resumo no console.

Uso:
    python3 scripts/decompile_software_fix.py                # execucao completa
    python3 scripts/decompile_software_fix.py --sem-extras   # sem 3rd-party
    python3 scripts/decompile_software_fix.py --apenas NOME  # so modulos que
                                                             # contenham NOME
    python3 scripts/decompile_software_fix.py --workers 2    # paralelismo
=============================================================================
"""

import argparse
import json
import os
import shutil
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path

# ---------------------------------------------------------------------------
# Configuracao de caminhos
# ---------------------------------------------------------------------------
SOURCE_DIR = Path("/run/media/dark/1224A54224A529A5/Program Files/Software Fix")
PROJECT_ROOT = Path(__file__).resolve().parent.parent
DEST_SRC_DIR = PROJECT_ROOT / "src_decompiled"
DEST_CONFIG_DIR = PROJECT_ROOT / "configs_and_manifests"
REPORT_PATH = PROJECT_ROOT / "relatorio_decompilacao.json"

# DLLs de runtime/sistema ignoradas na varredura recursiva de plugins/
IGNORED_DLL_PREFIXES = (
    "Microsoft.", "System.", "Newtonsoft.", "GoogleAnalytics.",
    "WebView2", "libmp3lame", "vcredist", "AdbWin",
)

# Bibliotecas de terceiros da raiz descompiladas apenas como REFERENCIA
# (nao fazem parte da solucao; util para entender cripto, SVGs etc).
EXTRAS_REF = (
    "BouncyCastle.Crypto.dll",
    "Common.Logging.dll",
    "Common.Logging.Core.dll",
    "Common.Logging.Log4Net.Universal.dll",
    "GoogleAnalytics.Core.dll",
    "GoogleAnalytics.WPF.Managed.dll",
    "Newtonsoft.Json.dll",
    "protobuf-net.dll",
    "SharpVectors.Converters.dll",
    "SharpVectors.Core.dll",
    "SharpVectors.Css.dll",
    "SharpVectors.Dom.dll",
    "SharpVectors.Model.dll",
    "SharpVectors.Rendering.Gdi.dll",
    "SharpVectors.Rendering.Wpf.dll",
    "SharpVectors.Runtime.dll",
)


# ---------------------------------------------------------------------------
# Ambiente
# ---------------------------------------------------------------------------
def check_environment() -> str:
    """Valida ilspycmd e cria os diretorios de destino. Retorna a versao."""
    print("[1/5] Validando ambiente e ferramentas...")
    ilspycmd = shutil.which("ilspycmd")
    if not ilspycmd:
        print("[-] 'ilspycmd' nao encontrado no PATH.")
        print("[*] Instalando via 'dotnet tool install -g ilspycmd'...")
        subprocess.run(["dotnet", "tool", "install", "-g", "ilspycmd"], check=True)
        user_tools = Path.home() / ".dotnet" / "tools"
        os.environ["PATH"] = f"{user_tools}:{os.environ['PATH']}"
        ilspycmd = shutil.which("ilspycmd")

    version_raw = subprocess.run(
        ["ilspycmd", "--version"], capture_output=True, text=True, check=True
    ).stdout.strip()
    print(f"[+] Ferramenta ativa: {version_raw.splitlines()[0]}")

    # Verifica suporte a BAML->XAML (ilspycmd >= 10.1)
    try:
        major, minor = (int(x) for x in version_raw.split(":")[1].split()[0].split(".")[:2])
        baml_ok = (major, minor) >= (10, 1)
    except Exception:
        baml_ok = False
    if not baml_ok:
        print("[!] ATENCAO: ilspycmd < 10.1 - recursos BAML ficarao como bytes crus.")
        print("    Atualize para >= 10.1 para converter BAML em XAML legivel.")
    else:
        print("[+] Suporte a BAML->XAML habilitado (modo -p).")

    for d in (DEST_SRC_DIR, DEST_CONFIG_DIR):
        d.mkdir(parents=True, exist_ok=True)
    print(f"[+] Codigo-fonte  -> {DEST_SRC_DIR}")
    print(f"[+] Configuracoes -> {DEST_CONFIG_DIR}")
    return version_raw


# ---------------------------------------------------------------------------
# Configs, manifestos, idiomas
# ---------------------------------------------------------------------------
def copy_configs_and_manifests():
    print("\n[2/5] Copiando configuracoes, manifestos e idiomas...")
    for item in SOURCE_DIR.iterdir():
        if item.is_file() and item.suffix.lower() in (".xml", ".config", ".installstate"):
            shutil.copy2(item, DEST_CONFIG_DIR / item.name)
            print(f"  -> config: {item.name}")

    for folder in ("lang", "plugins"):
        src = SOURCE_DIR / folder
        if not src.is_dir():
            continue
        dest = DEST_CONFIG_DIR / folder
        if folder == "lang":
            if dest.exists():
                shutil.rmtree(dest)
            shutil.copytree(src, dest)
            print(f"  -> pasta '{folder}' copiada integralmente")
        else:
            # plugins: copia apenas os metadados (DLLs vao para src_decompiled)
            for root, _dirs, files in os.walk(src):
                rel = Path(root).relative_to(src)
                target = dest / rel
                target.mkdir(parents=True, exist_ok=True)
                for f in files:
                    if not f.lower().endswith((".dll", ".exe", ".pdb")):
                        shutil.copy2(Path(root) / f, target / f)
            print(f"  -> metadados da pasta 'plugins' copiados")


# ---------------------------------------------------------------------------
# Descompilacao
# ---------------------------------------------------------------------------
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


def get_plugin_friendly_name(guid: str) -> str:
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


def decompile_assembly(bin_path: Path, output_dir: Path, use_baml_flag: bool) -> dict:
    """Descompila um assembly para output_dir (projeto C# completo)."""
    output_dir.mkdir(parents=True, exist_ok=True)
    cmd = ["ilspycmd", "-p", "-o", str(output_dir), "--nested-directories"]
    if use_baml_flag:
        cmd.append("--decompile-baml")
    cmd.append(str(bin_path))

    size_mb = max(1, bin_path.stat().st_size // (1024 * 1024))
    timeout = max(600, min(3600, size_mb * 90))
    started = time.time()
    try:
        proc = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout)
        err = proc.stderr or ""
        if use_baml_flag and "Unrecognized" in err:
            # versao muito nova removeu a flag: tenta de novo sem ela
            cmd.remove("--decompile-baml")
            proc = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout)
            err = proc.stderr or ""
        cs_files = list(output_dir.rglob("*.cs"))
        xaml_files = list(output_dir.rglob("*.xaml"))
        native = "does not contain any managed metadata" in err
        success = len(cs_files) > 0
        return {
            "name": bin_path.name,
            "source": str(bin_path),
            "output_dir": str(output_dir),
            "status": "ok" if success else ("nativo" if native else "falha"),
            "cs": len(cs_files),
            "xaml": len(xaml_files),
            "baml_raw": len(list(output_dir.rglob("*.baml"))),
            "seconds": round(time.time() - started, 1),
            "error": "" if success or native else err[:400],
        }
    except subprocess.TimeoutExpired:
        return {
            "name": bin_path.name, "source": str(bin_path),
            "output_dir": str(output_dir), "status": "timeout",
            "cs": 0, "xaml": 0, "baml_raw": 0,
            "seconds": round(time.time() - started, 1),
            "error": f"timeout apos {timeout}s",
        }


def build_task_list(include_extras: bool, only_filter: str):
    tasks = []  # (bin_path, output_dir, grupo)

    for item in sorted(SOURCE_DIR.iterdir()):
        if item.is_file() and should_decompile_root(item):
            tasks.append((item, DEST_SRC_DIR / item.stem.replace(" ", "_"), "raiz"))

    src_plugins = SOURCE_DIR / "plugins"
    if src_plugins.is_dir():
        for plugin_dir in sorted(src_plugins.iterdir()):
            if not plugin_dir.is_dir():
                continue
            friendly = get_plugin_friendly_name(plugin_dir.name)
            for dll_file in sorted(plugin_dir.rglob("*.dll")):
                if dll_file.name.startswith(IGNORED_DLL_PREFIXES):
                    continue
                rel_parent = dll_file.parent.relative_to(plugin_dir)
                out = DEST_SRC_DIR / "plugins" / friendly
                if str(rel_parent) != ".":
                    out = out / rel_parent
                tasks.append((dll_file, out / dll_file.stem.replace(" ", "_"), "plugins"))

    if include_extras:
        for name in EXTRAS_REF:
            f = SOURCE_DIR / name
            if f.exists():
                tasks.append((f, DEST_SRC_DIR / "_extras_ref" / f.stem, "extras"))

    if only_filter:
        low = only_filter.lower()
        tasks = [t for t in tasks if low in str(t[1]).lower() or low in t[0].name.lower()]

    return tasks


# ---------------------------------------------------------------------------
# Pos-processamento: consolida BAML em XAML
# ---------------------------------------------------------------------------
def resolve_baml_resources(results):
    """Consolida os recursos BAML:
      1. remove os .baml ja convertidos pelo modo -p (bytes crus);
      2. converte individualmente via --resource os que restaram crus."""
    print("\n[4/5] Consolidando recursos BAML -> XAML...")
    removed, converted, kept = 0, [], []
    for r in results:
        if r["status"] != "ok":
            continue
        out_dir = Path(r["output_dir"])
        bin_path = Path(r["source"])
        xaml_stems = {p.stem.lower() for p in out_dir.rglob("*.xaml")}
        for baml in sorted(out_dir.rglob("*.baml")):
            if baml.stem.lower() in xaml_stems:
                baml.unlink()
                removed += 1
                continue
            # tentativa de conversao individual (recurso cru)
            rel = baml.relative_to(out_dir).as_posix()
            resource = f"{bin_path.stem}.g.resources/{rel}"
            tmp = out_dir / "__baml_tmp__"
            tmp.mkdir(exist_ok=True)
            try:
                subprocess.run(
                    ["ilspycmd", str(bin_path), "--resource", resource, "-o", str(tmp)],
                    capture_output=True, text=True, timeout=180)
                xamls = sorted(tmp.glob("*.xaml"))
                if xamls:
                    target = baml.parent / f"{baml.stem}.xaml"
                    shutil.move(str(xamls[0]), str(target))
                    baml.unlink()
                    converted.append(str(target.relative_to(DEST_SRC_DIR)))
                else:
                    kept.append(str(baml.relative_to(DEST_SRC_DIR)))
            except Exception:
                kept.append(str(baml.relative_to(DEST_SRC_DIR)))
            shutil.rmtree(tmp, ignore_errors=True)
    print(f"  -> {removed} .baml convertidos pelo modo -p (bytes crus removidos)")
    print(f"  -> {len(converted)} .baml convertidos individualmente via --resource")
    for c in converted:
        print(f"     * {c}")
    if kept:
        print(f"  -> {len(kept)} .baml NAO convertidos (mantidos como bytes crus):")
        for k in kept[:20]:
            print(f"     * {k}")
    return removed, converted, kept


# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------
def main():
    parser = argparse.ArgumentParser(description="Descompilador .NET do Software Fix")
    parser.add_argument("--sem-extras", action="store_true",
                        help="nao descompilar bibliotecas de terceiros de referencia")
    parser.add_argument("--apenas", default="", help="filtro: so modulos que contenham o texto")
    parser.add_argument("--workers", type=int, default=2, help="paralelismo (padrao: 2)")
    args = parser.parse_args()

    print("=" * 74)
    print("  EXTRATOR E DESCOMPILADOR .NET — SOFTWARE FIX (LENOVO / MOTOROLA)")
    print("=" * 74)

    check_environment()
    copy_configs_and_manifests()

    tasks = build_task_list(include_extras=not args.sem_extras, only_filter=args.apenas)
    total = len(tasks)
    print(f"\n[3/5] Descompilando {total} assemblies gerenciados...")

    results = []
    started = time.time()
    # maiores primeiro: os pesados comecam cedo e ocupam os slots
    tasks.sort(key=lambda t: t[0].stat().st_size, reverse=True)
    with ThreadPoolExecutor(max_workers=max(1, args.workers)) as pool:
        futures = {
            pool.submit(decompile_assembly, bin_path, out_dir, True): (bin_path, out_dir, grupo)
            for bin_path, out_dir, grupo in tasks
        }
        for fut in as_completed(futures):
            bin_path, out_dir, grupo = futures[fut]
            res = fut.result()
            res["grupo"] = grupo
            results.append(res)
            icon = {"ok": "[OK]", "nativo": "[NV]", "falha": "[!!]", "timeout": "[TO]"}[res["status"]]
            print(f"  {icon} {res['name']:<58} {res['cs']:>4} cs | {res['xaml']:>3} xaml | {res['seconds']:>6}s")

    # pos-processamento
    removed, converted, kept = resolve_baml_resources(results)

    # relatorio
    ok = [r for r in results if r["status"] == "ok"]
    nativo = [r for r in results if r["status"] == "nativo"]
    bad = [r for r in results if r["status"] not in ("ok", "nativo")]
    total_cs = sum(r["cs"] for r in ok)
    total_xaml = sum(r["xaml"] for r in ok)
    report = {
        "gerado_em": time.strftime("%Y-%m-%d %H:%M:%S"),
        "fonte": str(SOURCE_DIR),
        "destino": str(DEST_SRC_DIR),
        "duracao_total_s": round(time.time() - started, 1),
        "assemblies_processados": len(results),
        "ok": len(ok),
        "nativos": len(nativo),
        "falhas": [r for r in bad],
        "total_cs": total_cs,
        "total_xaml": total_xaml,
        "baml_crus_removidos": removed,
        "baml_convertidos_individualmente": converted,
        "baml_nao_convertidos": kept,
        "modulos": sorted(results, key=lambda r: (r["grupo"], r["name"])),
    }
    report_path = REPORT_PATH if not args.apenas else PROJECT_ROOT / "relatorio_decompilacao_parcial.json"
    report_path.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")

    print("\n" + "=" * 74)
    print("  RELATORIO FINAL DE DESCOMPILACAO")
    print("=" * 74)
    print(f"  Assemblies processados : {len(results)}")
    print(f"  Sucesso (gerenciados)  : {len(ok)}")
    print(f"  Nativos (sem .NET)     : {len(nativo)}  {[r['name'] for r in nativo]}")
    print(f"  Falhas/timouts         : {len(bad)}  {[r['name'] for r in bad]}")
    print(f"  Total de arquivos .cs  : {total_cs}")
    print(f"  Total de arquivos .xaml: {total_xaml} (+{len(converted)} via --resource)")
    print(f"  Fonte                  : {SOURCE_DIR}")
    print(f"  Codigo descompilado    : {DEST_SRC_DIR}")
    print(f"  Configs/manifestos     : {DEST_CONFIG_DIR}")
    print(f"  Relatorio JSON         : {report_path}")
    print("=" * 74)
    return 0 if not bad else 1


if __name__ == "__main__":
    sys.exit(main())
