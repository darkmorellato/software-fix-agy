#!/usr/bin/env bash
# ============================================================================
# Software Fix — handler do scheme "softwareFix://"
#
# O novo fluxo de login da Lenovo (página /Tips/lenovoIdSuccess.html) devolve um
# deep link "softwareFix://authcallback?Authorization=<token>&fullName=<nome>".
# No Windows, o app oficial registrado para esse scheme recebe o login.
# No Linux, este script é o handler: recebe a URL do navegador e conclui o login
# pelo CLI (software-fix login --url), salvando a sessão para a GUI.
# ============================================================================
URL="$1"
LOG_DIR="$HOME/.local/share/software-fix/logs"
LOG="$LOG_DIR/login-callback.log"
mkdir -p "$LOG_DIR"

# usa o CLI instalado; se não existir, tenta o dist do repositório
BIN=""
if command -v software-fix >/dev/null 2>&1; then
    BIN="$(command -v software-fix)"
elif [ -x "$HOME/software-fix-agy/dist/cli/SoftwareFix.CLI" ]; then
    BIN="$HOME/software-fix-agy/dist/cli/SoftwareFix.CLI"
else
    notify-send -a "Software Fix" "Login não capturado" "CLI (software-fix) não encontrado. Abra o app e faça login manualmente." 2>/dev/null || true
    exit 1
fi

{
    echo "=== $(date '+%F %T') ==="
    echo "URL: $URL"
    if "$BIN" login --url "$URL" 2>&1; then
        if command -v notify-send >/dev/null 2>&1; then
            notify-send -a "Software Fix" "Login capturado com sucesso ✔" "A conta foi conectada — volte ao Software Fix."
        fi
        echo "RESULTADO: OK"
    else
        if command -v notify-send >/dev/null 2>&1; then
            notify-send -a "Software Fix" "Falha ao capturar o login" "Abra o Software Fix e cole a URL da página de sucesso manualmente."
        fi
        echo "RESULTADO: FALHA"
    fi
} >>"$LOG" 2>&1
