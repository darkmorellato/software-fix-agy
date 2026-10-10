#!/usr/bin/env bash
# ============================================================================
# Software Fix — Linux Edition | Instalador de usuário (sem root)
# ============================================================================
# Compila (opcional) e instala a GUI + CLI no perfil do usuário (~/.local),
# cria os comandos 'software-fix-ui'/'software-fix' e o atalho no menu.
#
# Uso:
#   ./scripts/install-user.sh                # compila e instala
#   ./scripts/install-user.sh --skip-build   # usa o dist/ existente
#   ./scripts/install-user.sh --with-udev    # + regras USB do sistema (sudo)
#   ./scripts/install-user.sh --uninstall    # remove a instalação do usuário
# ============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"
DIST_DIR="$ROOT_DIR/dist"

APP_DIR="$HOME/.local/share/software-fix"
BIN_DIR="$HOME/.local/bin"
DESKTOP_DIR="$HOME/.local/share/applications"
ICON_DIR="$HOME/.local/share/icons/hicolor/256x256/apps"

SKIP_BUILD=0
WITH_UDEV=0
UNINSTALL=0

for arg in "$@"; do
    case "$arg" in
        --skip-build) SKIP_BUILD=1 ;;
        --with-udev)  WITH_UDEV=1 ;;
        --uninstall)  UNINSTALL=1 ;;
        *) echo "[!] Opção desconhecida: $arg"; exit 2 ;;
    esac
done

# ---------------------------------------------------------------- desinstalar
if [ "$UNINSTALL" = 1 ]; then
    rm -f "$BIN_DIR/software-fix-ui" "$BIN_DIR/software-fix"
    rm -f "$BIN_DIR/software-fix-login-callback"
    rm -f "$DESKTOP_DIR/software-fix.desktop" "$DESKTOP_DIR/software-fix-auth.desktop"
    rm -f "$ICON_DIR/software-fix.png"
    rm -rf "$APP_DIR"
    command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$DESKTOP_DIR" >/dev/null 2>&1 || true
    echo "[✓] Software Fix removido do perfil do usuário."
    echo "    (As regras em /etc/udev/rules.d não foram alteradas.)"
    exit 0
fi

echo "=============================================================="
echo " Software Fix — Linux Edition | Instalação no perfil do usuário"
echo "=============================================================="

# ---------------------------------------------------------------- 1) build
if [ "$SKIP_BUILD" = 0 ]; then
    if ! command -v dotnet >/dev/null 2>&1; then
        echo "[!] 'dotnet' não encontrado. Instale o .NET 8 SDK (veja INSTALL.md §1.1)"
        echo "    ou use --skip-build com um dist/ já compilado."
        exit 1
    fi
    echo "[1/5] Compilando (self-contained)... (use --skip-build para pular)"
    "$SCRIPT_DIR/build-linux.sh" >/dev/null
else
    echo "[1/5] Build ignorado (--skip-build)."
fi

for f in "$DIST_DIR/ui/SoftwareFix.UI" "$DIST_DIR/cli/SoftwareFix.CLI"; do
    [ -x "$f" ] || { echo "[!] Não encontrado: $f — rode o build primeiro."; exit 1; }
done

# ---------------------------------------------------------------- 2) copiar
echo "[2/5] Copiando aplicação para $APP_DIR ..."
mkdir -p "$APP_DIR" "$BIN_DIR" "$DESKTOP_DIR" "$ICON_DIR"
# copia TUDO (executável + DLLs + libs nativas libSkiaSharp.so/libHarfBuzzSharp.so —
# sem as nativas o Avalonia não inicia: "Unable to load shared library 'libSkiaSharp'")
cp -a "$DIST_DIR/ui/."  "$APP_DIR/"
cp -a "$DIST_DIR/cli/." "$APP_DIR/"
chmod +x "$APP_DIR/SoftwareFix.UI" "$APP_DIR/SoftwareFix.CLI"

# ---------------------------------------------------------------- 3) wrappers
echo "[3/5] Criando comandos em $BIN_DIR ..."
cat > "$BIN_DIR/software-fix-ui" <<EOF
#!/usr/bin/env bash
exec "$APP_DIR/SoftwareFix.UI" "\$@"
EOF
cat > "$BIN_DIR/software-fix" <<EOF
#!/usr/bin/env bash
exec "$APP_DIR/SoftwareFix.CLI" "\$@"
EOF
chmod +x "$BIN_DIR/software-fix-ui" "$BIN_DIR/software-fix"

# ---------------------------------------------------------------- 4) menu
echo "[4/5] Instalando ícone e atalho no menu de aplicativos..."
if [ -f "$ROOT_DIR/src/SoftwareFix.UI/Assets/software-fix.png" ]; then
    SRC_ICON="$ROOT_DIR/src/SoftwareFix.UI/Assets/software-fix.png"
    install -m 0644 "$SRC_ICON" "$ICON_DIR/software-fix.png"
    # gera os demais tamanhos (menus, docks e lançadores pedem vários)
    if command -v magick >/dev/null 2>&1 || command -v convert >/dev/null 2>&1; then
        MAGICK="$(command -v magick || command -v convert)"
        for s in 512 128 64 48 32 16; do
            d="$HOME/.local/share/icons/hicolor/${s}x${s}/apps"
            mkdir -p "$d"
            "$MAGICK" "$SRC_ICON" -resize "${s}x${s}" "$d/software-fix.png" 2>/dev/null || true
        done
    fi
    command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -f -t "$HOME/.local/share/icons/hicolor" >/dev/null 2>&1 || true
fi

# Alguns lançadores (fuzzel, rofi, docks) resolvem ícones pelo TEMA ATIVO —
# que costuma ser Papirus-Dark/breeze etc., não o hicolor. Sem o index.theme no
# hicolor do usuário ou sem o ícone DENTRO do tema ativo, o atalho fica sem imagem.
[ -f "$HOME/.local/share/icons/hicolor/index.theme" ] || cp /usr/share/icons/hicolor/index.theme "$HOME/.local/share/icons/hicolor/index.theme" 2>/dev/null || true
for theme in Papirus Papirus-Dark breeze breeze-dark Adwaita; do
    if [ -d "/usr/share/icons/$theme" ] && [ -f "$HOME/.local/share/icons/hicolor/256x256/apps/software-fix.png" ]; then
        [ -f "$HOME/.local/share/icons/$theme/index.theme" ] || cp "/usr/share/icons/$theme/index.theme" "$HOME/.local/share/icons/$theme/" 2>/dev/null || true
        for s in 48x48 64x64 128x128 256x256; do
            d="$HOME/.local/share/icons/$theme/$s/apps"
            mkdir -p "$d"
            cp -f "$HOME/.local/share/icons/hicolor/$s/apps/software-fix.png" "$d/software-fix.png"
        done
        command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -f -t "$HOME/.local/share/icons/$theme" >/dev/null 2>&1 || true
    fi
done
cat > "$DESKTOP_DIR/software-fix.desktop" <<EOF
[Desktop Entry]
Name=Software Fix
GenericName=Atualizar Motorola & Lenovo
Comment=Resgatar, atualizar e baixar firmware oficial de Motorola e Lenovo
Exec=$BIN_DIR/software-fix-ui
Terminal=false
Type=Application
Icon=software-fix
Categories=Utility;Development;
Keywords=motorola;lenovo;rescue;fastboot;flash;rom;atualizar;firmware;
StartupNotify=true
StartupWMClass=SoftwareFix.UI
EOF
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$DESKTOP_DIR" >/dev/null 2>&1 || true

# Handler do scheme "softwareFix://" — captura o retorno do login novo (Lenovo ID
# devolve o deep link softwareFix://authcallback?Authorization=... e o nosso
# handler conclui o login pelo CLI, salvando a sessão para a GUI detectar).
if [ -f "$SCRIPT_DIR/software-fix-login-callback.sh" ] && [ -f "$SCRIPT_DIR/software-fix-auth.desktop" ]; then
    install -m 0755 "$SCRIPT_DIR/software-fix-login-callback.sh" "$BIN_DIR/software-fix-login-callback"
    install -m 0644 "$SCRIPT_DIR/software-fix-auth.desktop" "$DESKTOP_DIR/software-fix-auth.desktop"
    if command -v xdg-mime >/dev/null 2>&1; then
        xdg-mime default software-fix-auth.desktop x-scheme-handler/softwarefix >/dev/null 2>&1 || true
        xdg-mime default software-fix-auth.desktop "x-scheme-handler/softwareFix" >/dev/null 2>&1 || true
    fi
    command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$DESKTOP_DIR" >/dev/null 2>&1 || true
    echo "    [i] Login automático ativo: scheme softwareFix:// → captura o login do navegador."
fi

# ---------------------------------------------------------------- 5) udev
if [ "$WITH_UDEV" = 1 ]; then
    echo "[5/5] Instalando regras udev (requer sudo)..."
    sudo cp "$ROOT_DIR/udev/51-motorola-lenovo.rules" /etc/udev/rules.d/
    sudo udevadm control --reload-rules
    sudo udevadm trigger
    if getent group plugdev >/dev/null 2>&1; then
        sudo usermod -aG plugdev "$USER" || true
        echo "    [i] Grupo 'plugdev' atualizado — faça logout/login para aplicar."
    else
        echo "    [i] Grupo 'plugdev' não existe aqui — as regras usam uaccess (logind): deve funcionar direto."
    fi
else
    echo "[5/5] Regras udev NÃO instaladas (opcional). Para permissões USB sem root:"
    echo "      sudo cp \"$ROOT_DIR/udev/51-motorola-lenovo.rules\" /etc/udev/rules.d/"
    echo "      sudo udevadm control --reload-rules && sudo udevadm trigger"
    echo "      (ou rode este script com --with-udev)"
fi

echo
echo "[✓] Instalação concluída!"
echo "    GUI : software-fix-ui   (ou no menu → \"Software Fix\")"
echo "    CLI : software-fix --help"
echo "    App : $APP_DIR"
echo
echo "    Desinstalar : $SCRIPT_DIR/install-user.sh --uninstall"
echo "    Diagnóstico : $SCRIPT_DIR/check_device_env.sh"
