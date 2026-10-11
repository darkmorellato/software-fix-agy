# Plano de Resgate — fluxos por chipset (Snapdragon / MediaTek / Unisoc)

Motorizado pelo porte dos "planners" do projeto **LenovoMotoFirmwareDownloader**
(https://github.com/darkmorellato/LenovoMotoFirmwareDownloader) para o nosso
Software Fix Linux, e validado contra os pacotes oficiais reais.

## O fluxo oficial da Motorola é FASTBOOT (surpresa boa)

Os ROMs oficiais da Motorola trazem o próprio plano de flash dentro do ZIP —
**não é preciso o SP Flash Tool nem o DA** no fluxo normal:

| Pacote | Arquivos-chave | Método | Modelos (exemplos reais) |
|--|--|--|--|
| MTK "Tinno" | `flashall.sh` / `flashall.bat` / `LMSA.bat` | Fastboot por script | lamuc (G17), lagos (G85), cfc_flash.bat |
| XML do fabricante | `flashfile.xml` (apaga) / `servicefile.xml` (mantém dados) | Fastboot por XML | fogos_g (Edge 60), bogota_g (Edge 70), nice_g (G56) |
| Snapdragon "de emergência" | `prog_firehose*.mbn` + `rawprogram*.xml` + `patch*.xml` | **EDL/qdl** (9008) | quando não há fastboot |
| Unisoc | `*.pac` | SPD tool | modelos Unisoc (T7250 etc.) |

O **Software Fix** monta o plano automaticamente — e o dispatcher escolhe o
método pelos arquivos presentes (prioridade: script → XML → EDL → PAC).

## Comandos

```bash
# 1. Ver o plano (dry-run, nada é executado):
software-fix rescue-plan --dir ~/.local/share/software-fix/download/RomFiles/lamuc_rom
software-fix rescue-plan --dir /caminho/rom --data-reset no    # manter dados (servicefile)

# 2. Executar (pede CONFIRMAR + valida o codename via getvar product antes!):
software-fix rescue-run --dir /caminho/rom --yes
software-fix rescue-run --dir /caminho/rom --data-reset no --yes     # manter dados
software-fix rescue-run --dir /caminho/rom --dry-run                 # só lista

# 3. Detectar o aparelho (fastboot / EDL 05c6:9008 / PreLoader MTK 0e8d:2000):
software-fix rescue-detect
```

Na GUI (Assistente), o plano aparece **automaticamente** ao selecionar a ROM
(e atualiza ao trocar o modo "resgatar × manter dados").

## Segurança (fiel aos scripts oficiais)

- **Validação de codename**: antes do primeiro flash, roda `getvar product` e
  compara com o `phone_model`/PRODUCT_NAME do pacote — aborta se não conferir
  (o flashall.sh oficial também para aí).
- **Modo manter dados**: usa `servicefile.xml` e nunca gera `erase
  userdata/metadata`.
- **Dry-run por padrão no `rescue-plan`** e `--yes` obrigatório para executar.
- Super esparso: `flash -S 1G super super.img` (script) ou chunks
  `super.img_sparsechunk.*` (XML) — ambos suportados.

## Emergência: quando o fastboot não sobe

- **MediaTek**: PreLoader + DA assinado (nosso fluxo `mtk-*` já validado no G17).
- **Snapdragon**: EDL 9008 (`lsusb 05c6:9008`) + `qdl` (linux-msm/qdl) com o
  programmer firehose e rawprogram do pacote — `rescue-plan` prepara o comando.
- **Unisoc**: modo BROM/SPD + `spd-tool` (`github:enigma550/spd-tool-bun`).

## Ferramentas

- fastboot/adb: `~/.local/share/software-fix/tools/platform-tools/` (já instalado).
- qdl: instalar separadamente (linux-msm/qdl) — o plano aponta o comando completo.
- spd-tool: para os pacotes PAC (G06 e outros Unisoc, a confirmar por modelo).
