# Autenticação ODM/SLA ("GPS auth") — resgate de Motorola MediaTek

**Atualizado em 10/10/2026** com a análise completa de um **resgate real bem-sucedido
de um Moto G17 (XT2623-1, família `lamuc_g`)** — a ferramenta baixada pelo LMSA e os
logs do SP Flash Tool foram encontrados no disco Windows. Leia §0 primeiro.

---

## 0. Resumo executivo

Existem **dois mecanismos distintos** com "SLA" no ecossistema:

| Mecanismo | Onde vive | Estado |
| :--- | :--- | :--- |
| **A. LamuC / TSDC** — fluxo REAL do resgate MediaTek (G17/XT2623 e família `lamuc`) | SP Flash Tool Console customizado ("LamuC") em modo BROM/Preloader + `SLA_Challenge.dll` → serviço HTTP "TSDC" | Fluxo e protocolos **mapeados** (§1); host/credenciais do TSDC ainda não capturados |
| **B. ODM socket + DataSignODM** | Framework .NET do Software Fix (`smartdevice`): servidor TCP local 127.0.0.1:5000 que faz relay p/ `callsimunlockservice` | **Implementado neste port** (`odm-server`/`odm-sign`); uso real ainda não identificado |

SLA = **Serial Link Authentication** (autenticação do BROM/DA nos chips MediaTek —
confirmado nas strings de `FlashtoollibEx.dll`: `BRom_Base::SerialLinkAuthentication()`,
`CALLBACK_SLA_CHALLENGE`, `boot_rom::preloader_sla`).

> Correção de hipótese anterior: as strings `installing signature` / `signature must be
> 256 bytes` do `fastbootmonitor.exe` são recursos do **fastboot AOSP** e **não** fazem
> parte deste fluxo — o resgate lamuc não passa por fastboot.

---

## 1. Fluxo REAL (LamuC / TSDC) — como o resgate MediaTek funciona

**Evidências**: tool zip `LamuC_FlashTool_Console_LMSA_5.2404.03_release.zip`
(baixado pelo LMSA em `C:\ProgramData\RSA\Download\ToolFiles\`), `SLA_Challenge.dll`
dentro dele, logs do resgate em `C:\ProgramData\SP_FT_Logs\SP_FT_Dump_10-09-2026-17-52-15\`
(`ADPT_*.log`, `BROM_DLL_V5.log`, `GLB_*.log`, `QT_FLASH_TOOL.log`) e o registro do
resgate em `...\Software Fix\logs\2026-10-friendly.log`.

### 1.1 Sequência

1. LMSA baixa o pacote de ROM `fastboot_lamuc_g_user_15_VVTB35.41_87_d127d2_release-keys.zip`
   — que contém o **scatter MTK** (`MT6768_Android_scatter.txt`), as imagens e o **DA**
   (`DA_A15_lamuc_FORBID_SIGNED.bin`, header `MTK_AllInOne_DA_v3.3001`).
   (O prefixo "fastboot_" é só nomenclatura do CMS — o flash é BROM/SP-Flash.)
2. Aparelho em **BROM/Preloader** (USB `0e8d:0003` / `0e8d:2000` / `0e8d:2001`).
3. Ferramenta conecta BROM → baixa o DA (259.144 bytes no resgate de referência) →
   `DA Connected`.
4. **SLA (Serial Link Authentication)** — troca com o DA (vista byte a byte no GLB log):

   | Ordem | Controle | Papel |
   | :--- | :--- | :--- |
   | 1 | `CC_SET_ALL_IN_ONE_SIGNATURE` (0x2000C) | sinaliza modo "all-in-one" |
   | 2 | `CC_GET_DEV_FW_SEC_INFO` (0x40013) | devolve info de segurança — inclui o **desafio** (bloco de 32 bytes, ex. `26 6e 5b ad 66 11 84 c9 ...`) |
   | 3 | `CC_GET_SLA_ENABLED_STATUS` (0x40016) | `01 00 00 00` = SLA habilitado |
   | 4 | `CC_SET_REMOTE_SEC_POLICY` (0x2000B) | host envia **256 bytes assinados** (resposta ao desafio) |
   | 5 | `STATUS_OK` → download liberado | |

   Em falha, aparece `STATUS_SEC_DL_FORBIDDEN` (o parser .NET trata como
   `AuthorizedError`). A ferramenta recebe na linha de comando `-u/--TSDCUser` e
   `-w/--TSDCPwd` ("DA SLA server user/pwd").

5. A **assinatura** vem de `SLA_Challenge.dll` (carregada pela ferramenta; exports
   `DA_SLA_Challenge`, `PL_SLA_Challenge`, `SLA_Feature_Config`, `SLA_SetServerAddr`,
   `SLA_SetLoginInfo`, `SLA_SetRomName`, `SLA_SetWinInternet`), que fala **HTTP
   (WinINet)** com o serviço "TSDC":
   - `GET {server}/api/user/login?username=%s&password=%s&tool=%s` → resposta com `token`
   - `GET {server}/api/auth/plslaverify?token=%s&Rom_name=%s&Plain_text=%s` → resposta
     com `Encrypt_text` (RSA) = os 256 bytes enviados ao DA
   - O endereço do servidor entra por `SLA_SetServerAddr` (não está embutido no
     binário — **capturar ao vivo**, ver §4).

### 1.2 Onde estão os artefatos (disco Windows)

- Ferramenta: `C:\ProgramData\RSA\Download\ToolFiles\LamuC_FlashTool_Console_LMSA_5.2404.03_release.zip`
- ROM/DA do resgate: `C:\ProgramData\RSA\Download\RomFiles\fastboot_lamuc_g_user_15_VVTB35.41_87_d127d2_release_keys\` (limpo após sucesso)
- Logs do flash tool (TEXTO, legíveis): `C:\ProgramData\SP_FT_Logs\SP_FT_Dump_<data>\`
- Log do app: `...\Software Fix\logs\<data>.log.dpapi` (**DPAPI** — só abre no Windows do usuário)
- Registro do resgate: `...\Software Fix\logs\<data>-friendly.log` (texto simples)

### 1.3 Extrair o zip da ferramenta (senha cifrada) — receita

A senha vem em `ProgramData\LMSA\Download\downloaded_resources.json` (`ZipPwd`),
**AES-128-CBC** com chave fixa embutida no app (`Security.DecryptAseString` /
`RandomAesKeyHelper`): **chave = `LsdW^19dG1s&Pc*M` (ASCII), IV = primeiros 16 bytes
do conteúdo base64-decoded**, o restante é o ciphertext (PKCS#7).

```bash
python3 - <<'EOF'
import base64
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
raw = base64.b64decode("Ghq5ZRIBYieRV0FMNGIyQw5mWRbNHPjrAspXOoq+fpo=")  # ZipPwd de 09/10/2026
dec = Cipher(algorithms.AES(b"LsdW^19dG1s&Pc*M"), modes.CBC(raw[:16])).decryptor()
pt = dec.update(raw[16:]) + dec.finalize()
print(pt[:-pt[-1]].decode())   # -> EFyGFw
EOF
bsdtar -xf LamuC_*.zip -C destino --passphrase 'EFyGFw'
```

Dentro do zip: `SLA_Challenge.dll` (PE32 nativo), `flash_tool.exe` (SP Flash Tool
5.2404 fork — "Console Mode", args `-c -d -s -u -w -a/-e ...`), `assistant.exe`,
`FlashToolLib*.dll`, `FlashtoollibEx.dll`, `DA_A15_lamuc_FORBID_SIGNED.bin`,
`console_mode.xsd`.

### 1.4 Descobertas da 2ª rodada (10/10, 100% do Linux)

- **A receita do resgate é pública**:
  `https://rsddownload-secure.lenovo.com/Rescue_MTK_Lamu26_FastbootConnect_V1_20260107.json`
  (S3 da Lenovo, leitura anônima; obtida da URL presignada que aparece no log do app).
  Passos: `WaitConnectByFastboot` → `Sleep` → `ReadDeviceMode` → `LoadFiles` →
  **`CopyFiles` (copia `DA_A15_lamuc_FORBID_SIGNED.bin` + `sign_info.txt` do ToolFolder
  para o RomFolder)** → `FastbootShell` (current-slot) → `FastbootDeviceMatchCheck`
  (LMSA.bat) → `EraseUserData` → **`Shell`: `flash_tool.exe -c download -d
  "...DA_A15_lamuc_FORBID_SIGNED.bin" -s "...MT6768_Android_scatter.txt"`**
  (com `LogMonitorActions`: ao ver "Search usb" → `fastboot oem shutdown`, que desliga o
  aparelho para o usuário entrar em BROM) → retries (scatter `_reserve_userdata` para
  manter dados) → `CopyLogs` de `SP_FT_Logs`. **A receita NÃO tem credenciais nem o
  endereço do TSDC** — esses vêm de dentro do bundle LamuC.
- **ROM e ferramenta públicos** (baixáveis do Linux): `fastboot_lamuc_g_user_15_VVTB35.41-87_d127d2_release-keys.zip`
  (**6,7 GB** — scatter MT6768 + imagens + DA) e `LamuC_FlashTool_Console_LMSA_5.2404.03_release.zip`
  (34 MB). Listagem do bucket é negada (somente GET de objetos).
- **Artefatos do app decifrados offline (DPAPI machine-scope, SEM senha)** com as hives do
  próprio disco: `Windows/System32/config/{SYSTEM,SECURITY}` + master key em
  `Windows/System32/Microsoft/Protect/S-1-5-18/fea19208-7bcd-42cd-a1c7-2a9c40907e56`
  (impacket: `dpapi.py masterkey -file <mk> -system SYSTEM -security SECURITY` →
  `dpapi.py unprotect -file <blob> -key <hex>`). Decifrados: **log do app do resgate**
  (683 linhas — o fluxo completo, incluindo a linha de comando do flash_tool) +
  `flashrecords/rescueddevice/options/…check.json`. Cópias em
  `~/.local/share/software-fix/analysis-2026-10-10/` (contém dados pessoais — não versionar).
- **Onde as credenciais TSDC NÃO estão**: receita, linha de comando do flash_tool, hives de
  registro (ASCII nem UTF-16). Hipótese atual: default embutido/obfuscado no bundle LamuC
  (ou configuração efêmera da execução).

---

## 2. Mecanismo B — ODM socket + DataSignODM (implementado no port)

Do framework .NET (`lenovo.mbg.service.framework.smartdevice`):

- `Steps/RunODMSocketServer` inicia servidor TCP em **127.0.0.1:5000** com IMEI +
  usuário/senha (PARAMS da receita); `ODMSocketServer/ODMServerMain.cs`: sonda
  `"IsRunning"` → `"0"`; assinatura = `<tipo 2 bytes>` + hash → resposta com a
  assinatura (hex→bytes em buffer de 512); falha → 512 zeros.
- Assinatura na nuvem: `POST https://ebiz-esb.cloud.motorola.net/callsimunlockservice`
  (JSON + Basic auth), campos `newIMEI, mascid, clientIP, clientReqType, rsd_log_id,
  prod_id, type, keyname, data`; resp `{responseCode,responseMsg,msg}`; retry 3×/500ms.
- Constantes: logId `91C44564934F4E96A1533641D2694B38`, prodId `M6102`, type `23`,
  keyname `1`, clientReqType `0x00`, mascid `GC-7777777-TEST-190`, clientIP fixo.

**Nota (10/10/2026)**: o resgate lamuc real (§1) **não usa** este socket — o cliente
deste mecanismo no fluxo oficial segue desconhecido (possivelmente outras famílias/
gerações de aparelho ou fluxo legado). O host `ebiz-esb.cloud.motorola.net` também
**não resolve publicamente** hoje (NOERROR/NODATA na zona AWS).

### 2.1 Implementação no port (verificada)

| Arquivo | Papel |
| :--- | :--- |
| `src/SoftwareFix.Core/Services/OdmSigningClient.cs` | cliente do serviço de assinatura (paridade 1:1 com `Web.cs`/`DataSigningODMService.cs`/`RestService.cs`; retry 3×/500ms) |
| `src/SoftwareFix.Core/Services/OdmSocketServer.cs` | servidor local 127.0.0.1:5000 (paridade com `ODMServerMain.cs`) |
| CLI `software-fix odm-server --imei <IMEI>` | sobe o servidor (senha por prompt oculto ou env `SOFTWARE_FIX_ODM_PASSWORD`) |
| CLI `software-fix odm-sign --imei <IMEI>` | teste de contrato ao vivo |
| `tests/odm/test_odm_e2e.py` | e2e offline com mock do serviço (sonda, assinatura, contrato, falha→zeros — 3/3 OK) |

Notas finas de paridade: a sonda do original compara hex com `"IsRunning"` (defeito
latente — implementamos o comportamento pretendido); falha de assinatura envia 512
zeros (como o original); uma mensagem por conexão.

---

## 3. Comparação com o app oficial

Para estes aparelhos o app oficial apenas: baixa ROM + ferramenta, coloca o aparelho
em BROM e executa o console (`TN_MTK_TSDC_Flashtool_V5.2404.1.exe -c download -d
DA_...bin -s MT6768_Android_scatter.txt -u <user> -w <pwd>`), lendo o progresso de
`WRITE TO PARTITION` / `All command exec done!` (`Steps/ShellResponseFlashtool.cs`).

## 4. Pendências / próximos passos

1. **Teste com o aparelho (preparado, aguardando o usuário)** — com o G17 em BROM
   (desligado + volume + USB): rodar **mtkclient** (já instalado no venv de análise:
   `~/.hermes/cache/scratch/venv-dpapi/bin/mtk`) para handshake/leitura e avaliar se o
   MT6768 é destravável via **payload genérico (sem SLA)** ou se precisaremos da
   assinatura. Regra udev para `0e8d` já instalada; o port já identificou o aparelho
   (`scan`: SKU XT2623-1 / moto g17).
2. **ROM oficial pública** (6,7 GB) para o flash no Linux: baixar de
   `https://rsddownload-secure.lenovo.com/fastboot_lamuc_g_user_15_VVTB35.41-87_d127d2_release-keys.zip`
   quando formos executar o resgate de verdade.
3. **Credenciais/host do TSDC** (SLA remoto): origem desconhecida (não está na receita,
   na linha de comando nem nos hives). Se o mtkclient não bastar: capturar ao vivo no
   Windows (ProcMon/TCPView) ou dissecar o bundle LamuC.
4. **Validar o mecanismo B** no port (`software-fix odm-sign --imei <IMEI>`) quando houver
   conta de serviço + rede onde `ebiz-esb.cloud.motorola.net` resolve.

## 5. Como testar o port

```bash
# 1) Offline (não toca na internet) — mock do serviço Motorola
dotnet build src/SoftwareFix.CLI -v q --nologo
python3 tests/odm/test_odm_e2e.py

# 2) Contrato ao vivo (usa a conta de serviço; evite digitar a senha na shell)
SOFTWARE_FIX_ODM_USER='...' SOFTWARE_FIX_ODM_PASSWORD='...' \
  software-fix odm-sign --imei <IMEI>

# 3) Runtime (durante um resgate MediaTek com o servidor do mecanismo B)
software-fix odm-server --imei <IMEI>     # Ctrl+C encerra
```

Fontes: `src_decompiled/.../smartdevice/ODMSocketServer/*.cs` + `Steps/*` (Software Fix
7.6.2.10); `LamuC_FlashTool_Console_LMSA_5.2404.03_release.zip` (`SLA_Challenge.dll`,
`flash_tool.exe`, release notes); logs do resgate de 09/10/2026
(`SP_FT_Logs/SP_FT_Dump_10-09-2026-17-52-15/` + `2026-10-friendly.log`).
