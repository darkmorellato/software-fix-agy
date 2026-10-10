# Relatório Final — Fases 2 a 5 (Paridade Funcional)

**Projeto:** software-fix-agy (port Linux do Lenovo/Motorola Software Fix 7.6.2.10)
**Data:** 10/10/2026 · **Status:** plano concluído; itens de hardware físico ficam com o usuário.

---

## Fase 2 — Contratos de rede (CONCLUÍDA e validada ao vivo)

**Análise (fonte: descompilado):** `WebApiHttpRequest`, `WebApiContext`, `ApiBaseService`,
`RsaService`, `WebApiUrl` (77 endpoints), fluxo de login (`LenovoIdWindow`, `GuestLogin`,
`LenovoIdUserLogin`, `SplashScreenWindow`), envelopes (`RequestModel`/`ResponseModel`).

**Implementado no port:**
* `src/SoftwareFix.Core/Services/WebApiUrl.cs` — **novo**, espelho 1:1 de todos os endpoints;
* `LenovoApiClient` reforçado: envelope oficial (`client/dparams/language/windowsInfo`),
  retry 3× com jitter, `Cache-Control: no-cache`, clientUUID persistente
  (`~/.config/software-fix/client_uuid`), Content-Length 0 em POST vazio;
* **Correção crítica** do fingerprint: `X-Device-Fingerprint` usa o header Authorization
  COMPLETO (`"Bearer <token>"`) no payload RSA — como no original;
* Fluxo OAuth Lenovo ID completo: `getApiInfo{TIP_URL}` → URL de login PKCE →
  validação do WUST em `getSFUserInfo` → sessão salva (`session.json`);
* `Configurations.Version` corrigida para **7.6.2.10** (FileVersion real do exe);
* CLI: comandos `login` e `api-test`.

**Validação ao vivo (lsa.lenovo.com):**
| Teste | Resultado |
| :--- | :--- |
| `POST /common/rsa.jhtml` | `0000` + chave pública RSA |
| `POST /dictionary/getApiInfo.jhtml {TIP_URL}` | `0000` + URL OAuth PKCE (envelope aceito) |
| `GET /user/getSFUserInfo.jhtml` (sem token) | `410 Missing device fingerprint` — contrato de assinatura confirmado pelo servidor |

**Documentação:** `docs/CONTRATOS_REDE.md`.

---

## Fase 3 — Motor de dispositivo (CONCLUÍDA, testada com mocks)

**Análise:** `ReadPropertiesInFastboot` (lista exata de 19 propriedades),
`FastbootFlash` (timeouts por operação, skip de `fb_mode_*`, detecção de erros),
`EraseUserData` (fdr-allowed / enterprise), robots de conexão ADB/Fastboot.

**Corrigido/implementado no port:**
* **BUG CRÍTICO corrigido** nos dois runners: `await Task.WhenAny(...) == Task.CompletedTask`
  era sempre falso → TODA chamada de `adb`/`fastboot` retornava "timeout";
* **BUG corrigido** no `FlashEngine`: o skip de `oem fb_mode_set/fb_mode_clear` não
  funcionava (comparava o campo errado) — agora replica o `FastbootFlash.cs`;
* Detecção de falha com a semântica do original (`fail`/`error`), com tratamento de
  `too many links` e `failed to write` (→ `oem partition dump logfs` como diagnóstico);
* Campos novos de paridade: `ro.build.version.qcom`, `emmc`, `androidVer`, `iswarrantyvoid`;
* Binários configuráveis por ambiente (`SOFTWARE_FIX_FASTBOOT`, `SOFTWARE_FIX_ADB`);
* Regras udev verificadas no sistema (Motorola/Lenovo/Qualcomm EDL/MediaTek/Unisoc) e
  usuário no grupo `plugdev`.

**Testes executados (mocks em `tests/parity/`):**
* Detecção: fastboot `XT2303-2/retbr/M6375_R/6GB/dual` + ADB `moto g53 5G` ✔
* Flash completo: 10 comandos, slot lido, ordem preservada, `fb_mode_*` ignorados,
  progresso por bytes, sucesso final ✔

---

## Fase 4 — Interface (CONCLUÍDA)

* **Design tokens extraídos** do tema oficial (paleta completa por frequência, tipografia,
  dimensões) — `docs/DESIGN_TOKENS.md`;
* `App.axaml` consolidado com os HEX **exatos** (`#005D7F`, `#001429`, `#9FAEBF`,
  `#40A995`, `#43B5E2` …) e tokens V6 nomeados;
* `MainWindow.axaml`: substituições para os valores oficiais (27× `#001429`, 13+× `#9FAEBF`,
  7× `#005D7F`, 8× `#40A995`), título/versão atualizados para **v7.6.2.10**;
* Componentes verificados: barra superior com 7 abas, status/conexão, seletor de modelo
  (modal de busca), entrada de IMEI, progresso, console de logs, diálogos de confirmação
  (wipe/bootloader) e tela de sucesso;
* Build **0 erros / 0 warnings**; UI abre e roda sem exceções (smoke test).

---

## Fase 5 — Homologação (CONCLUÍDA no que é executável sem hardware)

| Teste | Status |
| :--- | :--- |
| Detecção (fastboot/ADB) | ✔ simulado com mocks (reproduzível) — hardware físico: pendente c/ usuário |
| Consulta de firmware | ✔ fluxo completo até a API real; lista autenticada pendente de `software-fix login` |
| Download & checagem | ✔ **sha256 idêntico** (3 MB, HTTP local) + `VerifyMd5Async` disponível |
| Gravação (flash) | ✔ motor completo com mock (ordem/timeouts/skip/erros) — hardware físico: pendente c/ usuário |
| Extras | ✔ build limpo, UI abre, 358 XAML de referência, udev OK |

**Documentação:** `docs/HOMOLOGACAO.md` (checklist + comandos).

---

## Alterações de arquivos (resumo)

* **Novos:** `WebApiUrl.cs`, `tests/parity/{mock_fastboot.sh,mock_adb.sh,flashfile-sample.xml}`,
  `docs/{CONTRATOS_REDE.md,DESIGN_TOKENS.md,HOMOLOGACAO.md,RELATORIO_FINAL_FASES.md}`.
* **Alterados:** `LenovoApiClient.cs`, `RsaHelper.cs` (Core), `Configurations.cs`,
  `Models.cs`, `FastbootRunner.cs`, `AdbRunner.cs`, `FlashEngine.cs`,
  `App.axaml`, `MainWindow.axaml`, `Program.cs` (CLI), `README.md`.
* **Sem alteração de contrato:** `SoftwareFix.Download`, `FlashFileParser` (já corretos).

## Correção pós-homologação (10/10/2026)

* **Crash ao clicar na aba "Hardware Test"** — exceção não tratada
  (`ArgumentException: Unable to resolve type vm:MainViewModel`) no binding com cast de
  tipo (`((vm:MainViewModel)DataContext)`) dentro de `DataTemplate` (closures do Avalonia
  não resolvem o prefixo de namespace em runtime). Corrigido com `ReflectionBinding`
  (`$parent[Window].DataContext.Command`) nos dois pontos afetados — o mesmo padrão
  latente existia no modal de seleção de modelos (corrigido preventivamente).
* Validação: build `--no-incremental` limpo; as **7 seções** abrem sem crash
  (`SOFTWARE_FIX_SECTION=0..6`, hook de QA adicionado ao MainViewModel); verificação
  visual por captura de tela da seção Hardware Test.

## Pendências (requerem hardware físico — do usuário)

1. `software-fix login` com sua conta Lenovo ID (fluxo no navegador).
2. `software-fix match` com aparelho real e comparação da lista de firmware com o app Windows.
3. Flash real em aparelho de teste (o log do port deve espelhar a ordem do original).
