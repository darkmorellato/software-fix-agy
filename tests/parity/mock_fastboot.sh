#!/usr/bin/env bash
# =============================================================================
# Mock do binário `fastboot` para testes de paridade do Software Fix Linux.
# Simula um Motorola XT2303-2 em modo fastboot, SEM hardware real.
# Uso: SOFTWARE_FIX_FASTBOOT=<caminho>/mock_fastboot.sh software-fix scan
# =============================================================================
LOG="$(dirname "$0")/mock_fastboot.log"
echo "[mock] fastboot $*" >> "$LOG"

# Suporte a "-s <serial>" (o runner usa -s quando há serial conhecido)
if [ "$1" = "-s" ]; then shift 2; fi

case "$1" in
  devices)
    printf 'MOCK123456\tfastboot\n'
    ;;
  getvar)
    case "$2" in
      all)
        printf '(bootloader) sku: XT2303-2\n'
        printf '(bootloader) ro.boot.hardware.sku: XT2303-2\n'
        printf '(bootloader) serialno: MOCK123456\n'
        printf '(bootloader) imei: 350000000000001\n'
        printf '(bootloader) ro.carrier: retbr\n'
        printf '(bootloader) version-baseband: M6375_R\n'
        printf '(bootloader) ro.build.fingerprint: motorola/urtxd/urtxd:11/RZBS31.Q2-143-27-9/9dd6:user/release-keys\n'
        printf '(bootloader) ro.build.version.full: RZBS31.Q2-143-27-9\n'
        printf '(bootloader) ro.build.version.qcom: LA.UM.9.15.2\n'
        printf '(bootloader) oem hw dualsim: true\n'
        printf '(bootloader) softwareVersion: 110\n'
        printf '(bootloader) ram: 6GB\n'
        printf '(bootloader) emmc: 128GB\n'
        printf '(bootloader) androidVer: 11\n'
        printf '(bootloader) fdr-allowed: yes\n'
        printf '(bootloader) securestate: oem_locked\n'
        printf '(bootloader) iswarrantyvoid: false\n'
        printf '(bootloader) channelid: retbr\n'
        printf '(bootloader) cid: 0x0032\n'
        printf 'OKAY [ 0.5s ]\n'
        ;;
      current-slot)
        printf '(bootloader) current-slot: a\nOKAY [ 0.1s ]\n'
        ;;
      sku)
        printf '(bootloader) sku: XT2303-2\nOKAY [ 0.1s ]\n'
        ;;
      max-sparse-size)
        printf '(bootloader) max-sparse-size: 268435456\nOKAY [ 0.1s ]\n'
        ;;
      *)
        printf '(bootloader) %s: mock-value\nOKAY [ 0.1s ]\n' "$2"
        ;;
    esac
    ;;
  oem)
    case "$2" in
      read_sv)
        printf '(bootloader) signinfo id sum: 2\nOKAY [ 0.1s ]\n'
        ;;
      *)
        printf 'OKAY [ 0.1s ]\n'
        ;;
    esac
    ;;
  flash)
    sleep 0.3
    printf 'Sending sparse {%s} (1 KB)...\n' "$2"
    printf 'OKAY [ 0.3s ]\n'
    printf 'Writing %s...\n' "$2"
    printf 'OKAY [ 0.4s ]\n'
    printf 'Finished. Total time: 0.7s\n'
    ;;
  erase)
    printf 'Erasing %s...\n' "$2"
    printf 'OKAY [ 0.2s ]\n'
    printf 'Finished. Total time: 0.2s\n'
    ;;
  format)
    printf 'Formatting %s...\n' "$2"
    printf 'OKAY [ 0.2s ]\n'
    ;;
  *)
    printf 'OKAY [ 0.1s ]\n'
    ;;
esac
exit 0
