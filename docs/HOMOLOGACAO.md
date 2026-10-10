# Checklist de Homologação — Paridade com o Software Fix oficial

Metodologia: os testes sem hardware físico usam **mocks de paridade**
(`tests/parity/mock_fastboot.sh`, `tests/parity/mock_adb.sh`) que emitem as
respostas EXATAS de um Motorola XT2303-2 / moto g53, permitindo validar o motor
completo de forma reproduzível. Os itens com hardware ficam marcados como
pendentes para execução com o aparelho real.

## 1. Teste de Detecção (modo ligado + fastboot)

* [x] **Fastboot (simulado)**: `SOFTWARE_FIX_FASTBOOT=tests/parity/mock_fastboot.sh software-fix scan`
      → identifica `MOCK123456`, SKU `XT2303-2`, carrier `retbr`, baseband `M6375_R`,
      fingerprint, RAM, Dual SIM ✔
* [x] **ADB (simulado)**: `SOFTWARE_FIX_ADB=tests/parity/mock_adb.sh software-fix scan`
      → identifica `MOCKADB01`, SKU/modele ✔
* [ ] **Hardware real (pendente)**: conecte o aparelho (fastboot e depois com
      depuração USB) e rode `software-fix scan` — valide que os campos são os mesmos
      exibidos pelo app Windows.

## 2. Teste de Consulta (lista de firmwares)

* [x] **Contrato (real)**: `software-fix api-test` → `rsa.jhtml` + `getApiInfo{TIP_URL}` OK,
      servidor validando fingerprint (`410 Missing device fingerprint`).
* [x] **Fluxo completo (simulado)**: `SOFTWARE_FIX_FASTBOOT=... software-fix match`
      → device identificado, chamada real a `/rescueDevice/getNewResource.jhtml`
      executada, resposta tratada ✔
* [ ] **Com sessão autenticada (pendente do usuário)**: `software-fix login`
      (login Lenovo ID no navegador) e repetir `match` — a lista de firmware deve
      bater com a do app oficial para o mesmo SKU/IMEI.

## 3. Teste de Download & Checagem (hash)

* [x] **Integridade (real)**: download de 3 MB via `RomDownloader` (HTTP local) —
      **sha256 idêntico** ao original (`7bca870a…`), progresso/velocidade reportados ✔
* [x] **Verificação oficial**: `RomDownloader.VerifyMd5Async` implementa o check MD5
      usado pelo app (mesma semântica da checagem K0323/`K0429`).
* [ ] **ROM oficial (pendente)**: com sessão ativa, baixar um firmware real e
      comparar o MD5 com o exibido pelo app Windows.

## 4. Teste de Gravação (Flash)

* [x] **Motor completo (simulado)**: `software-fix flash tests/parity/flashfile-sample.xml`
      com o mock:
  * [x] leitura do slot (`getvar current-slot`)
  * [x] `oem fb_mode_set` / `oem fb_mode_clear` **ignorados** (como no FastbootFlash.cs)
  * [x] ordem das partições preservada (bootloader → boot_a → super → vbmeta_a → erases)
  * [x] timeouts por operação (flash 300 s, erase 60 s, getvar 20 s…)
  * [x] progresso por bytes (13,3% → 100%)
  * [x] mensagens de erro mapeadas (`fail`/`error`, `too many links`, `failed to write` → dump logfs)
* [ ] **Hardware real (pendente, sob sua responsabilidade)**: aparelho de teste em
      fastboot + ROM oficial → acompanhar o log: cada partição deve ser gravada na
      mesma ordem do app oficial; conferir o encerramento com reboot.

## 5. Autenticação ODM/SLA (MediaTek — "GPS auth")

* [x] **Servidor + cliente (offline, real)**: `python3 tests/odm/test_odm_e2e.py`
      → sonda `IsRunning`→`0`, assinatura → 512 bytes (256 da assinatura + zeros),
      contrato do POST (campos/auth Basic) conferido campo a campo, falha → zeros ✔
* [ ] **Contrato ao vivo (pendente)**: `software-fix odm-sign --imei <IMEI>` com a
      conta de serviço — o host `ebiz-esb.cloud.motorola.net` não resolve publicamente
      (ver `docs/ODM_SLA_AUTH.md` §2/§4).
* [x] **Fluxo real identificado (10/10/2026)**: resgate do Moto G17 (XT2623-1) = SP
      Flash Tool Console "LamuC" em modo BROM + `SLA_Challenge.dll` → serviço TSDC
      (HTTP). Protocolo do DA (`CC_GET_DEV_FW_SEC_INFO` → desafio; `CC_SET_REMOTE_SEC_POLICY`
      com 256 bytes assinados) e API do TSDC mapeados dos logs de um resgate real —
      ver `docs/ODM_SLA_AUTH.md` §1.
* [ ] **Fluxo LamuC no Linux (pendente/roadmap)**: capturar host/credenciais do TSDC
      ao vivo (Windows) e avaliar **mtkclient** para BROM (MT6768) no Linux.

## 6. Extras de verificação

* [x] Build da solução completa (6 projetos): **0 erros, 0 warnings**
* [x] Interface Avalonia: abre e executa sem exceções (smoke de 8 s)
* [x] 358 XAML do tema descompilados disponíveis como referência pixel-perfect
* [x] Regras udev instaladas cobrindo Motorola/Lenovo/Qualcomm/MTK/Unisoc + usuário no `plugdev`

## Comandos de referência rápida

```bash
cd ~/software-fix-agy

# 1) Diagnóstico de ambiente USB
./scripts/check_device_env.sh

# 2) Testes de paridade sem hardware
SOFTWARE_FIX_FASTBOOT=$PWD/tests/parity/mock_fastboot.sh \
SOFTWARE_FIX_ADB=$PWD/tests/parity/mock_adb.sh \
dotnet run --project src/SoftwareFix.CLI -- scan

printf 'y\n' | SOFTWARE_FIX_FASTBOOT=$PWD/tests/parity/mock_fastboot.sh \
  dotnet run --project src/SoftwareFix.CLI -- flash tests/parity/flashfile-sample.xml

# 3) Com hardware real
dotnet run --project src/SoftwareFix.CLI -- scan
dotnet run --project src/SoftwareFix.CLI -- login        # autentica via Lenovo ID (navegador)
dotnet run --project src/SoftwareFix.CLI -- match        # consulta firmware oficial
dotnet run --project src/SoftwareFix.CLI -- flash <rom>/flashfile.xml

# 4) Autenticação ODM/SLA (MediaTek)
python3 tests/odm/test_odm_e2e.py          # e2e offline (mock do serviço Motorola)
software-fix odm-sign --imei <IMEI>        # contrato ao vivo (conta de serviço)
software-fix odm-server --imei <IMEI>      # servidor durante o resgate MTK
```
