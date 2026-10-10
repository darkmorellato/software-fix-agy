# Contratos de Rede — API Lenovo (Software Fix 7.6.2.10)

Documento de paridade de protocolo (`protocol-perfect`) extraído de
`lenovo.mbg.service.common.webservices.dll` + `Software Fix.exe` (descompilados em `src_decompiled/`)
e validado ao vivo contra `https://lsa.lenovo.com` (ver §5).

## 1. Base e envelope

* Base: `https://lsa.lenovo.com/Interface` (`BaseHttpUrl` + `/Interface`).
* Envelope do corpo (POST) — espelho 1:1 de `RequestModel.cs`:

```json
{
  "client":      { "version": "7.6.2.10" },
  "dparams":     { ... parâmetros do endpoint ... },
  "language":    "pt-BR",
  "windowsInfo": "Linux x86_64, 64bit"
}
```

* Resposta padrão (`ResponseModel.cs`): `{ "code": "0000", "desc": "...", "success": true, "content": ... }`.
  Códigos relevantes: `0000` = sucesso; `402`–`409` = token expirado/inválido (ver `WebApiHttpRequest.CodeToMessage`);
  `410`–`414` = fingerprint ausente/inválido/expirado/usado/verificação falhou (`FingerPrintMessage`).

## 2. Headers (todos os requests)

| Header | Valor | Origem (descompilado) |
| :--- | :--- | :--- |
| `User-Agent` | `Mozilla/5.0 (Windows NT 6.3; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/51.0.2704.79 Safari/537.36` | `WebApiHttpRequest.RequestBase` |
| `Request-Tag` | `lmsa` | idem |
| `Cache-Control` | `no-cache` | idem |
| `clientVersion` | `7.6.2.10` (FileVersion do exe) | `LMSAContext.MainProcessVersion` |
| `windowsInfo` | Base64 de `Linux x86_64, 64bit` (original: caption do SO Windows) | `WebApiContext.WINDOWS_VERSION_BASE64` |
| `language` | `pt-BR` | `LMSAContext.CurrentLanguage` |
| `clientUUID` | UUID persistente do cliente (arquivo `~/.config/software-fix/client_uuid`) | `GlobalFun.GetClientUUID` |
| `Authorization` | `Bearer <token>` (WUST Lenovo ID) — apenas em requests autenticados | `WebApiContext.REQUEST_AUTHOR_HEADERS` |
| `X-Device-Fingerprint` | ver §3 — apenas quando há `Authorization` | `CreateClientRequestFinger` |

## 3. Assinatura: X-Device-Fingerprint (réplica exata)

```
payload   = "<unix_ms>|<valor COMPLETO do header Authorization>|<segmento>interface"
fingerprint = Base64( RSA_Encrypt_PKCS1v15(payload, chave_publica) )
```

* `segmento`: último segmento da URL com a extensão substituída pela palavra `interface`.
  Ex.: `.../user/getSFUserInfo.jhtml` → `getSFUserInfointerface` (sem separador — como no original).
* No payload entra o header completo, **incluindo o prefixo `Bearer `** (detalhe crítico do original).
* A chave pública vem de `POST /common/rsa.jhtml` → campo `desc` (DER SubjectPublicKeyInfo base64, "Java X.509").
* Exceção: em `/common/rsa.jhtml` o fingerprint não é calculado.

## 4. Fluxo de sessão (OAuth Lenovo ID)

1. `POST /common/rsa.jhtml` → chave pública RSA (`desc`).
2. `POST /dictionary/getApiInfo.jhtml` `{"key":"TIP_URL"}` → URL de autorização OAuth2/PKCE
   (`https://passport-glb.lenovo.com/v1.0/utility/lenovoid/oauth2/authorize?...`).
   O servidor já inclui `state`, `client_id`, `code_challenge` (S256) e `redirect_uri`.
3. Usuário autentica (no app: WebView2; no port: navegador externo — comando `software-fix login`).
4. Redirecionamento para `https://lsa.lenovo.com/Tips/lenovoIdSuccess.html?lenovoid.wust=<WUST>&...`;
   o WUST é o token de sessão.
5. `GET /user/getSFUserInfo.jhtml` com `Authorization: Bearer <WUST>` + fingerprint valida a sessão
   e retorna os dados do usuário (`userId`, `name`, `email`, `country`, ...).
6. No original, a sessão é reenviada ao serviço Windows a cada 20 min (pipe `LMSA_DATA`);
   no port, a sessão é persistida em `~/.config/software-fix/session.json`.

## 5. Validação ao vivo (10/10/2026, via `software-fix api-test`)

| Teste | Resultado |
| :--- | :--- |
| `POST /common/rsa.jhtml` | `code 0000` + chave pública (base64) — OK |
| `POST /dictionary/getApiInfo.jhtml {TIP_URL}` | `code 0000` + URL OAuth PKCE — OK (envelope aceito pelo servidor) |
| `GET /user/getSFUserInfo.jhtml` sem token | `{"code":"410","desc":"Missing device fingerprint."}` — contrato de assinatura confirmado |
| Sessão autenticada | requer login Lenovo ID do usuário (`software-fix login`) |

## 6. Endpoints principais por área

| Área | Endpoint | Constante |
| :--- | :--- | :--- |
| Chave pública | `/common/rsa.jhtml` | `GET_PUBLIC_KEY` |
| Login/sessão | `/user/getSFUserInfo.jhtml`, `/user/guestLogin.jhtml`, `/user/recordLogin.jhtml`, `/user/logout.jhtml` | `LENOVOID_LOGIN_CALLBACK`, ... |
| Resgate — match ROM | `/rescueDevice/getNewResource.jhtml`, `/getNewResourceByImei.jhtml`, `/getNewResourceBySN.jhtml`, `/getRomMatchParams.jhtml`, `/getRescueModelRecipe.jhtml`, `/getRescueModelNames.jhtml`, `/modelListByMarketName.jhtml`, `/getResource.jhtml`, `/getXamlList.jhtml` | `RESUCE_AUTOMATCH_GETROM`, ... |
| Catálogo | `/rescueDevice/getModelNames.jhtml`, `/rescueDevice/smartMarketNames.jhtml` | `GET_ALLMODELNAMES`, `LOAD_SMART_DEVICE` |
| Dicionário/B2B | `/dictionary/getApiInfo.jhtml`, `/vip/getB2BInfo.jhtml`, `/vip/card.jhtml`, ... | `CALL_API_URL`, ... |
| Atualização | `/client/getNextUpdateClient.jhtml`, `/client/getClientPlugins.jhtml`, `/apk/download.jhtml`, `/client/renewFileLink.jhtml` | `CLIENT_VERSION`, ... |
| Telemetria | `/dataCollection/UpgradeFlashInfo.jhtml`, `/RescueflashInfo.jhtml`, `/HwDetectionInfo.jhtml`, `/addBackupRestore.jhtml`, `/nativeToolLog.jhtml`, ... | `POST_UPGRADE_FLASH_INFO`, ... |

> Lista integral (77 endpoints, nomes idênticos ao original): `src/SoftwareFix.Core/Services/WebApiUrl.cs`.

## 7. Observações de implementação

* Retry: 3 tentativas com jitter de 300–1000 ms (réplica do `ApiBaseService.RequestBase`).
* Timeout de request: 120 s (original: `Timeout=120000`).
* POST sem corpo envia `Content-Length: 0` com `Content-Type: application/json` (como o original).
* `windowsInfo` do header é Base64; no corpo é texto — ambos replicados.
* O endpoint `initToken.jhtml` existe no descompilado mas **não é usado pelo app oficial**;
  mantido apenas para investigação (`InitTokenAsync`).
* Validação com mitmproxy do app original (Windows) permanece como etapa opcional de homologação.
