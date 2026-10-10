# Autenticação ODM/SLA ("GPS auth") — resgate de Motorola MediaTek

Documento de engenharia do handshake de autenticação que o bootloader dos modelos
Motorola **MediaTek** (ex.: Moto G17, G24, G34, G54, G84) exige para liberar o flash
no resgate. No app oficial aparece nos logs como `GPS Signed Hash` /
`Signed HASH sent to SLA_Challenge` — é o "GPS auth" citado nos fóruns.

---

## 1. Como funciona (do descompilado → port)

1. A receita de resgate do modelo (vem do servidor) inclui o passo
   `RunODMSocketServer`, que inicia um servidor TCP local em **127.0.0.1:5000**
   com o **IMEI** do aparelho e **usuário/senha de serviço** (PARAMS da receita).
2. O lado fastboot do resgate obtém o **"phone hash"** (desafio do bootloader) e o
   envia ao servidor local (mensagem = 2 bytes de tipo + hash em **bytes binários**).
3. O servidor assina o hash na nuvem:
   `POST https://ebiz-esb.cloud.motorola.net/callsimunlockservice` — JSON +
   HTTP Basic. Campos: `newIMEI, mascid, clientIP, clientReqType, rsd_log_id,
   prod_id, type, keyname, data(=hex do hash)`. Resposta `{responseCode,
   responseMsg, msg}`; `responseCode != "0"` = erro (até 3 tentativas, 500ms).
4. A assinatura (`msg`, em hex → bytes, **buffer de 512**: 256 bytes da assinatura
   + zeros) volta pelo socket.
5. O lado fastboot instala os 256 bytes no bootloader pelo mecanismo **padrão do
   fastboot AOSP**: `fb->Download("signature", data)` +
   `fb->RawCommand("signature", "installing signature")` — o mesmo que o comando
   `fastboot signature <arquivo>` (arquivo de exatamente 256 bytes) e o que o
   `flashall` faz quando existe um `<imagem>.sig` ao lado da imagem.
   **Verificado: o fastboot do Linux (platform-tools 37.0.1) tem o comando.**
6. Com a assinatura instalada, o bootloader MediaTek aceita os comandos de flash.

Constantes do protocolo (do descompilado; usadas como padrão no port):

| Constante       | Valor                              |
| :-------------- | :--------------------------------- |
| porta           | `127.0.0.1:5000`                   |
| `rsd_log_id`    | `91C44564934F4E96A1533641D2694B38` |
| `prod_id`       | `M6102`                            |
| `type`          | `23`                               |
| `keyname`       | `1`                                |
| `clientReqType` | `0x00`                             |
| `mascid`        | `GC-7777777-TEST-190`              |
| `clientIP`      | `99.99.99.99` (fixo)               |

---

## 2. O que está implementado no port

| Arquivo | Papel |
| :--- | :--- |
| `src/SoftwareFix.Core/Services/OdmSigningClient.cs` | Cliente do serviço de assinatura (paridade com `Web.cs` / `DataSigningODMService.cs` / `RestService.cs`; retry 3×/500ms de `signDataCollectPenang`) |
| `src/SoftwareFix.Core/Services/OdmSocketServer.cs` | Servidor local `127.0.0.1:5000` (paridade com `ODMServerMain.cs`) |
| CLI `software-fix odm-server --imei <IMEI>` | Sobe o servidor (senha por **prompt oculto** ou env `SOFTWARE_FIX_ODM_PASSWORD`; usuário `--user`/env) |
| CLI `software-fix odm-sign --imei <IMEI>` | Teste de contrato da assinatura contra o serviço real |
| `tests/odm/test_odm_e2e.py` | E2E offline: mock do serviço Motorola + servidor real (sonda, assinatura, contrato do corpo/auth, falha→zeros) |

**Executado e verificado (10/10/2026):** build 0 warnings/0 erros; e2e offline
**todos os testes OK** (sonda `IsRunning`→`0`; assinatura → 512 bytes = 256 da
assinatura + 256 zeros; contrato do POST conferido campo a campo; falha de
assinatura → 512 zeros, como no app oficial).

### Notas finas de paridade

- O original compara a sonda `"IsRunning"` com a representação **hex** da mensagem
  (nunca casa) e tentaria parsear a resposta `"0"` como hex (`"0".Substring(0,2)`
  estoura) — dois defeitos latentes que tornam aquele caminho inalcançável. O port
  implementa o **comportamento pretendido**: textual `"IsRunning"` → `"0"`.
- No caminho de falha o app oficial envia o buffer de 512 bytes **zerado**; o port
  replica (o bootloader rejeita e o resgate falha visivelmente).
- Uma mensagem por conexão (como no original, que não re-arma a leitura) — o lado
  fastboot reconecta para a próxima mensagem.

---

## 3. Pendências (estado honesto)

1. **Cliente do socket no fluxo de flash oficial** — quem conecta em
   `127.0.0.1:5000` ainda não foi identificado estaticamente:
   - `fastbootmonitor.exe` = fastboot **AOSP 35.0.2** + imports `WS2_32`/`AdbWinApi`
     (as strings `installing signature` / `signature must be 256 bytes` são do
     AOSP, não patch); **não contém** strings do socket ODM — descartado como
     cliente direto até prova em contrário;
   - candidato principal: ferramenta **baixada por modelo** em `ToolFiles` (as
     receitas usam `RecipeResources.GetFastbootToolPath(name, "TOOL")`), ou o
     runtime do próprio app.
   - **Próximo passo concreto:** rodar `software-fix login` e buscar a receita do
     aparelho (`/rescueDevice/getRescueModelRecipe.jhtml`, já implementado no
     client) — ela lista passos e ferramentas exatas do modelo; e/ou inspecionar a
     pasta `ToolFiles` numa execução oficial no Windows.
2. **Validação ao vivo** — requer a conta de serviço (usuário/senha) + aparelho
   MediaTek em fastboot. Observação de rede (10/10/2026): o host
   `ebiz-esb.cloud.motorola.net` **não resolve publicamente** (zona AWS responde
   NOERROR/NODATA — "empty non-terminal", sem A/AAAA/CNAME). O endpoint pode ser
   alcançável apenas em redes/regiões específicas ou ter sido migrado; o port
   permite apontar para outro host via `SOFTWARE_FIX_ODM_URL` se necessário.
3. **Integração automática** no comando `flash` (subir o servidor ODM + instalar a
   assinatura com `fastboot signature`) — depende do item 1.

---

## 4. Como testar

```bash
# 1) Offline (não toca na internet) — mock do serviço Motorola
dotnet build src/SoftwareFix.CLI -v q --nologo
python3 tests/odm/test_odm_e2e.py

# 2) Contrato ao vivo (usa a conta de serviço; evite digitar a senha na shell)
SOFTWARE_FIX_ODM_USER='...' SOFTWARE_FIX_ODM_PASSWORD='...' \
  software-fix odm-sign --imei <IMEI>

# 3) Runtime (durante um resgate MediaTek, com o aparelho em fastboot)
software-fix odm-server --imei <IMEI>     # deixa rodando; Ctrl+C encerra
# O lado fastboot (quando replicado) conecta em 127.0.0.1:5000 e a assinatura
# resultante é instalada com:  fastboot signature <arquivo-de-256-bytes>
```

Fonte primária: `src_decompiled/lenovo.mbg.service.framework.smartdevice/.../ODMSocketServer/*.cs`,
`.../Steps/RunODMSocketServer.cs` (Software Fix 7.6.2.10).
