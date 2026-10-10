#!/usr/bin/env bash
# Verificador de Diagnóstico e Permissões USB para Motorola / Lenovo no CachyOS / Linux

echo "=================================================="
echo " Software Fix Linux - Diagnóstico de Ambiente USB "
echo "=================================================="

# 1. Verificar binários
echo -n "[1/4] Verificando adb e fastboot... "
ADB_BIN=$(which adb 2>/dev/null)
FASTBOOT_BIN=$(which fastboot 2>/dev/null)

if [ -n "$ADB_BIN" ] && [ -n "$FASTBOOT_BIN" ]; then
    echo "OK"
    echo "      adb: $ADB_BIN ($($ADB_BIN --version | head -n 1))"
    echo "      fastboot: $FASTBOOT_BIN ($($FASTBOOT_BIN --version | head -n 1))"
else
    echo "FALHA (Instale android-tools)"
fi

# 2. Verificar regras udev para Motorola (VID 22b8)
echo -n "[2/4] Verificando regras udev para Motorola (VID 22b8)... "
if [ -f "/etc/udev/rules.d/51-motorola-lenovo.rules" ] || [ -f "/usr/lib/udev/rules.d/51-android.rules" ]; then
    echo "OK (Regras instaladas)"
else
    echo "AVISO (Regras locais encontradas em udev/51-motorola-lenovo.rules)"
    echo "      Para instalar: sudo cp udev/51-motorola-lenovo.rules /etc/udev/rules.d/ && sudo udevadm control --reload-rules"
fi

# 3. Verificar aparelhos conectados em ADB
echo "[3/4] Varrendo dispositivos conectados em modo ADB..."
adb devices -l

# 4. Verificar aparelhos conectados em Fastboot
echo "[4/4] Varrendo dispositivos conectados em modo Fastboot..."
fastboot devices
echo "=================================================="
