#!/usr/bin/env bash
# =============================================================================
# Mock do binário `adb` para testes de paridade do Software Fix Linux.
# Simula um Motorola "moto g53 5G" conectado via USB com depuração ativa.
# Uso: SOFTWARE_FIX_ADB=<caminho>/mock_adb.sh software-fix scan
# =============================================================================
LOG="$(dirname "$0")/mock_adb.log"
echo "[mock] adb $*" >> "$LOG"

case "$1" in
  devices)
    printf 'List of devices attached\nMOCKADB01\tdevice\n\n'
    exit 0
    ;;
  version)
    printf 'Android Debug Bridge version 1.0.41\nVersion 35.0.2-mock\n'
    exit 0
    ;;
esac

if [ "$3" = "shell" ] && [ "$4" = "getprop" ]; then
  case "$5" in
    ro.boot.hardware.sku)          printf 'XT2303-2\n' ;;
    ro.product.model)              printf 'moto g53 5G\n' ;;
    ro.build.version.release)      printf '12\n' ;;
    ro.build.display.id)           printf 'U1TZS34.83-18-9\n' ;;
    ro.build.fingerprint)          printf 'motorola/urtxd/urtxd:12/U1TZS34.83-18-9/9dd6:user/release-keys\n' ;;
    ro.carrier)                    printf 'retbr\n' ;;
    gsm.version.baseband)          printf 'M6375_R\n' ;;
    ro.soc.model)                  printf 'SM6375\n' ;;
    persist.radio.multisim.config) printf 'dsds\n' ;;
    *)                             printf '\n' ;;
  esac
  exit 0
fi

if [ "$3" = "shell" ] && [ "$4" = "dumpsys" ]; then
  printf 'Current Battery Service state:\n  AC powered: false\n  USB powered: true\n  level: 87\n'
  exit 0
fi

if [ "$3" = "shell" ] && [ "$4" = "df" ]; then
  printf 'Filesystem      Size  Used Avail Use%% Mounted on\n/dev/block/dm-4  106G   32G   73G  31%% /data\n'
  exit 0
fi

exit 0
