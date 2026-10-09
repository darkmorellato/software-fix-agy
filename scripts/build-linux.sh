#!/usr/bin/env bash
set -e

echo "=========================================================="
echo " Compilando Software Fix - Linux Edition (Self-Contained) "
echo "=========================================================="

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"
DIST_DIR="$ROOT_DIR/dist"

mkdir -p "$DIST_DIR"

echo "[1/3] Publicando SoftwareFix.CLI (Console)..."
dotnet publish "$ROOT_DIR/src/SoftwareFix.CLI/SoftwareFix.CLI.csproj" \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -o "$DIST_DIR/cli"

echo "[2/3] Publicando SoftwareFix.UI (Avalonia Desktop)..."
dotnet publish "$ROOT_DIR/src/SoftwareFix.UI/SoftwareFix.UI.csproj" \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -o "$DIST_DIR/ui"

echo "[3/3] Copiando regras udev e desktop entry..."
cp "$ROOT_DIR/udev/51-motorola-lenovo.rules" "$DIST_DIR/"
cp "$ROOT_DIR/software-fix.desktop" "$DIST_DIR/"

echo "=========================================================="
echo " Build concluído com sucesso em: $DIST_DIR"
echo " Executável GUI: $DIST_DIR/ui/SoftwareFix.UI"
echo " Executável CLI: $DIST_DIR/cli/SoftwareFix.CLI"
echo "=========================================================="
