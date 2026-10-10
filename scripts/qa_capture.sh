#!/usr/bin/env bash
# ============================================================================
# QA Visual — Software Fix (Linux Edition)
# Captura de tela de uma ou mais seções do app para revisão visual (grim/KDE).
# Uso: scripts/qa_capture.sh [secoes...]   (padrão: todas 0..6)
# Saída: /tmp/sf_qa/sec_<n>.png + logs em /tmp/sf_qa/sec_<n>.log
# ============================================================================
set -u
cd "$(dirname "$0")/.." || exit 1

BIN=src/SoftwareFix.UI/bin/Debug/net8.0/SoftwareFix.UI.dll
OUT=/tmp/sf_qa
mkdir -p "$OUT"

SECTIONS=("$@")
if [ ${#SECTIONS[@]} -eq 0 ]; then
    SECTIONS=(0 1 2 3 4 5 6)
fi

for n in "${SECTIONS[@]}"; do
    SOFTWARE_FIX_SECTION="$n" dotnet "$BIN" > "$OUT/sec_$n.log" 2>&1 &
    PID=$!
    sleep 10
    grim "$OUT/sec_$n.png" 2>/dev/null || echo "[QA] grim falhou para a seção $n"
    kill "$PID" 2>/dev/null
    wait "$PID" 2>/dev/null
    sleep 1
done

echo "[QA] Capturas em $OUT:"
ls -la "$OUT"/*.png 2>/dev/null

for n in "${SECTIONS[@]}"; do
    if grep -qi "exception\|unhandled error" "$OUT/sec_$n.log" 2>/dev/null; then
        echo "[QA] ATENÇÃO: exceção detectada na seção $n:"
        grep -i "exception" "$OUT/sec_$n.log" | head -3
    fi
done
echo "[QA] Concluído."
