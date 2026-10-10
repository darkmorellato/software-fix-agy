# Resgate MediaTek (moto g17 / família `lamuc`) — fluxo PreLoader + DA assinado

O Software Fix oficial resgata aparelhos MediaTek (ex.: moto g17 / XT2623-1, MT6768 k68v1)
com um **SP Flash Tool Console customizado ("LamuC")** em **modo PreLoader**, enviando o
**DA assinado** (`DA_A15_lamuc_FORBID_SIGNED.bin`) — não por fastboot/BROM.

Este port replica esse fluxo **no Linux**, usando o `mtkclient` como transporte/protocolo
e o **DA assinado do próprio pacote LamuC** (obrigatório: o DA embutido do mtkclient é
rejeitado pelo bootloader com `DAA_SIG_VERIFY_FAILED 0x7024`).

> Validado no aparelho em 10/10/2026: DA1 assinado aceito (DAA) → stage 2 →
> `DA SLA is enabled` → `Trying lamu ....` → `SLA Signature was accepted.` →
> **GPT/tabela de partições lida com sucesso** (~256 GB, slots A/B).
> Evidência: `~/.local/share/software-fix/analysis-2026-10-10/sucesso_gpt_read_g17.log`.

## Pré-requisitos

| Item | Onde | Como apontar outro caminho |
| --- | --- | --- |
| `mtkclient` (binário `mtk`) | `~/.venvs/lmsa-tools/bin/mtk` | `export SOFTWARE_FIX_MTK=/caminho/mtk` |
| DA assinado | `~/.local/share/software-fix/da/DA_A15_lamuc_FORBID_SIGNED.bin` | `export SOFTWARE_FIX_MTK_DA=/caminho/DA.bin` |
| Regra udev para `0e8d` (MediaTek) | `/etc/udev/rules.d/` | `MODE="0666"` / `uaccess` |

Instalação do mtkclient (se necessário):

```bash
python -m venv ~/.venvs/lmsa-tools
~/.venvs/lmsa-tools/bin/pip install git+https://github.com/bkerler/mtkclient
```

## Regras de ouro (aprendidas no aparelho)

1. **O mtkclient precisa iniciar ANTES de o aparelho aparecer.** Se o aparelho já estiver
   conectado quando o processo começa, o bootloader não completa o handshake e o
   mtkclient aborta com *"Please disconnect, start mtkclient and reconnect."* —
   os comandos do port já cuidam disso (aguardam você desconectar e só então iniciam).
2. **Aparelho desligado; conectar segurando Volume ↓** → modo PreLoader (`0e8d:2000`).
   (O BROM é bloqueado por eFuse nos Motos MTK novos — botões não o expõem, e não é preciso.)
3. **PreLoader "travado"** (handshake falhando em loop, geralmente após tentativas de
   crash): reconectar NÃO resolve — **desligue o aparelho** (Power 20–30 s) e reconecte.
4. Prepare-se para a sessão ser única: se o aparelho "sair" no meio, desligue/reconecte.
5. (interno) As opções do `mtkclient` vão **depois do subcomando** (`mtk printgpt --loader X`);
   antes do subcomando elas são silenciosamente descartadas (argparse). O runner do port
   já monta a linha correta.

## Comandos

```
software-fix mtk-status      # configuração do fluxo + dispositivos MediaTek no USB
software-fix mtk-connect     # PreLoader → DA → lê o GPT (SOMENTE leitura)
software-fix mtk-dump        # dump (leitura) de partições — backup
    --partitions lk_a,lk_b,boot_a,...     (default: conjunto crítico)
    --out <dir>
software-fix mtk-write-test  # homologação da ESCRITA: lê → regrava idêntico → relê → SHA-256
    --partition <nome>        (default: mrdump — área de dump, conteúdo descartável)
software-fix mtk-flash       # gravação pelo plano partição→imagem do *scatter* da ROM
    --dir <pasta ROM extraída>  [--dry-run] [--only p1,p2] [--slotted] [--verify] [--yes]
```

## Passo a passo do resgate (Linux, 100%)

1. **Conexão**: rode `software-fix mtk-connect`. O comando pede para você desconectar o
   aparelho (e desligá-lo, se preciso) e só então inicia o mtkclient — conecte segurando
   **Volume ↓**. O GPT (tabela de partições) aparece no console.
2. **Backup (recomendado)**: `software-fix mtk-dump` → salva as partições críticas em
   `~/.local/share/software-fix/Dump/<data>/`.
3. **Conferência do plano de flash**: `software-fix mtk-flash --dir <ROM> --dry-run`
   (lê o `MT6768_Android_scatter.txt` da ROM oficial e lista partição←imagem).
4. **Flash**: `software-fix mtk-flash --dir <ROM> [--verify]` — pede confirmação
   (`FLASH`) e conduz tudo: ritual de conexão, gravação, e (com `--verify`) releitura
   comparando SHA-256 com os arquivos da ROM.
5. Ao final, o aparelho sai do modo de download — desligue/reconecte ou aguarde o boot.

## Gravação (por que existe o modo "download")

O DA `FORBID_SIGNED` da Motorola **recusa o comando padrão de escrita** do mtkclient
(`CMD_WRITE_DATA` → `0xc002000c Write data not allowed`) **e valida as imagens** ao final
do download (`0xc0020007 Image verify fail` para conteúdo não assinado). O caminho aceito
é o **`CMD_DOWNLOAD`** — o mesmo que o SP Flash Tool usa — com este formato, extraído do
log GLB do resgate real (09/10) e validado byte a byte contra o `preloader_lamu.bin` da
ROM oficial (checksum bateu exato):

    DOWNLOAD → status; nome da partição → tamanho (u64) → status (parameter check);
    por bloco (≤ 2 MB): host_status(0), checksum (soma dos bytes & 0xFFFF), dados;
    → status por bloco; +1 status final.

Ativação: `scripts/mtkclient-patches/apply_mtkclient_patches.py` (aplica no mtkclient do
venv: habilita `CC_START_DL_INFO`, adiciona `download_partition()` e a rota no `da_write`)
+ env **`SOFTWARE_FIX_MTK_DOWNLOAD=1`** nas gravações.
Consequência prática: **apenas imagens oficiais assinadas passam** — exatamente o que o
flash das ROMs oficiais faz. O `mtk-write-test` com bytes brutos NÃO passa neste DA; use-o
apenas como diagnóstico do gate.

## Verificação pós-flash

`mtk-flash --verify` relê as partições gravadas e compara **o prefixo** de cada uma com o
arquivo da ROM (as imagens costumam ser menores que a partição), reportando OK/DIVERGENTE.

## Limitações desta versão (v1)

- `mtk-flash` **não regrava `preloader` nem a tabela (`PGPT`)** por segurança.
- O DA `FORBID_SIGNED` pode impor política de assinatura na gravação — o
  `mtk-write-test` existe justamente para validar o caminho de escrita no aparelho
  antes de um flash completo.
- `--slotted` duplica nomes sem sufixo para `_a`/`_b` (útil se o scatter vier sem slot).
- Logs de cada execução: `~/.local/share/software-fix/logs/mtk/`.

## Paridade com o fluxo oficial (LMSA/LamuC)

| Etapa LMSA | Equivalente neste port |
| --- | --- |
| `fastboot oem shutdown` (monitor do log) | instrução: desligue o aparelho (Power 20–30 s) |
| `PreLoader` detectado pelo SPFT console | `mtk` aguardando `0e8d:2000` |
| DA assinado enviado pelo SPFT | `--loader DA_A15_lamuc_FORBID_SIGNED.bin` |
| `CC_SET_REMOTE_SEC_POLICY`/SLA do DA | tratado localmente pelo mtkclient (`Trying lamu ....` → aceito) |
| "Download" (scatter) | `mtk-flash` (plano via scatter) |
| Read back / Verify | `mtk-dump` / `mtk-flash --verify` |
