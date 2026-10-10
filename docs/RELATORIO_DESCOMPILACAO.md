# Relatório de Descompilação — Software Fix (Lenovo / Motorola)

**Data:** 10/10/2026 · **Projeto destino:** `/home/dark/software-fix-agy` · **Fonte:** `/run/media/dark/1224A54224A529A5/Program Files/Software Fix`

---

## 1. Resumo executivo

| Métrica | Resultado |
| :--- | ---: |
| Assemblies processados | **61** |
| Descompilados com sucesso | **60** (0 falhas) |
| Nativos sem metadados .NET (ignorados corretamente) | 1 (`BMAFrame9.dll`) |
| Arquivos `.cs` gerados | **6.282** |
| Arquivos `.xaml` gerados (BAML 100% convertido) | **358** (0 `.baml` cru restante) |
| Idiomas convertidos p/ JSON | 18 locales × 2.060 chaves |
| Assets extraídos (PNG/GIF/fontes) | 884 PNG, 72 GIF, 18 TTF, 4 OTF, 5 ICO, 10 JPG |

O pipeline é **reproduzível e idempotente** via `scripts/decompile_software_fix.py`.

---

## 2. Ferramentas

* **ilspycmd 11.1.0.9782** (ICSharpCode.Decompiler 11.1.0.9782) — instalado manualmente
  em `~/.dotnet/tools/.store/ilspycmd/11.1.0.9782/` com shim em `~/.dotnet/tools/ilspycmd`.
* **Runtime .NET 10** (`10.0.12`) instalado **lado a lado** em `~/.dotnet` — o SDK 8 do
  projeto não foi alterado. (O `dotnet tool update` do SDK 8 falha para pacotes `net10.0`
  com o erro enganoso *"DotnetToolSettings.xml was not found"*; por isso a instalação
  manual via `.nupkg` do NuGet.)
* **BAML → XAML**: o modo `-p` do ilspycmd ≥ 10.1 converte BAML automaticamente.
  Recursos que o `-p` não associa a Pages (ex.: styles sem code-behind) foram convertidos
  individualmente via `--resource "<Assembly>.g.resources/<caminho>.baml"`:
  * `lenovo.themes.generic/themesv6/treeviewstyle.xaml`
  * `plugins/Rescue_*/lenovo.mbg.service.lmsa.flash/viewv6/phoneqmatchviewv6.xaml`

---

## 3. Estrutura gerada

```
software-fix-agy/
├── src_decompiled/               # código-fonte C# + XAML + assets (137 MB)
│   ├── Software_Fix/             # executável principal (347 cs, 68 xaml)
│   ├── LmsaWindowsService/       # serviço do Windows (25 cs)
│   ├── lenovo.themes.generic/    # TEMA visual completo (325 cs, 103 xaml, PNGs, fontes)
│   ├── lenovo.mbg.service.*      # 16 módulos do framework Lenovo
│   ├── SharpAdbClient/           # cliente ADB (74 cs)
│   ├── plugins/                  # 7 plugins, 24 assemblies (2.593 cs, 184 xaml)
│   └── _extras_ref/              # bibliotecas de terceiros p/ referência (2.455 cs)
├── configs_and_manifests/        # .xml/.config/manifestos + lang/ + plugins/ (metadados)
│   └── lang_json/                # 18 idiomas chave/valor em JSON
├── relatorio_decompilacao.json   # relatório da execução (máquina-legível)
├── decompilacao.log              # log completo da execução
└── scripts/
    ├── decompile_software_fix.py # extrator/descompilador (reproduzível)
    ├── audit_decompiled.py       # auditoria de cobertura
    └── extract_lang_json.py      # conversor de idiomas → JSON
```

### Módulos da raiz (por arquivo .dll/.exe)

| Módulo | .cs | .xaml |
| :--- | ---: | ---: |
| Software Fix.exe | 347 | 68 |
| lenovo.themes.generic.dll | 325 | 103 |
| lenovo.mbg.service.framework.smartdevice.dll | 81 | – |
| SharpAdbClient.dll | 74 | – |
| lenovo.mbg.service.framework.socket.dll | 66 | – |
| lenovo.mbg.service.common.utilities.dll | 64 | – |
| lenovo.mbg.service.framework.services.dll | 59 | – |
| lenovo.mbg.service.framework.updateversion.dll | 34 | – |
| lenovo.mbg.service.lmsa.common.dll | 32 | 2 |
| lenovo.mbg.service.framework.devicemgt.dll | 31 | – |
| LmsaWindowsService.exe | 25 | – |
| lenovo.mbg.service.framework.download.dll | 24 | – |
| lenovo.mbg.service.common.webservices.dll | 22 | – |
| lenovo.mbg.service.framework.lang.dll | 12 | – |
| lenovo.mbg.service.common.log.dll | 12 | – |
| lenovo.mbg.service.framework.resources.dll | 8 | – |
| lenovo.mbg.service.framework.pipes.dll | 6 | – |
| lenovo.mbg.service.framework.hostcontroller.dll | 6 | – |
| lenovo.mbg.service.framework.smartbase.dll | 3 | – |
| lenovo.mbg.service.lmsa.hostproxy.dll | 3 | – |
| BMAFrame9.dll | *nativo — nada a descompilar* | – |

### Plugins (catálogo de `plugins.xml`)

| Plugin | ID | Assembly principal | .cs | .xaml |
| :--- | :--- | :--- | ---: | ---: |
| Rescue | `8ab04aa9…` | lenovo.mbg.service.lmsa.flash | 233 | 54 |
| My Device | `02928af0…` | lenovo.mbg.service.lmsa.phoneManager(+.apps/.common) | 907 | 66 |
| Backup & Restore | `13f79fe4…` | lenovo.mbg.service.lmsa.backuprestore | 123 | 17 |
| Toolbox | `dd537b5c…` | lenovo.mbg.service.lmsa.toolbox (+NAudio, SuperSocket, FFmpeg.AutoGen) | 1.217 | 25 |
| Support | `a6099126…` | lenovo.mbg.service.lmsa.support/forum/messenger/tips | 78 | 15 |
| Hardware Test | `985c66ac…` | lenovo.mbg.service.lmsa.hardwaretest | 21 | 4 |
| Data Transfer | `d8042f96…` | lenovo.mbg.service.lmsa.dataTransfer | 14 | 3 |

---

## 4. Onde estão as classes e serviços de REDE

*(pedido explícito: "indicar o caminho exato onde as classes e serviços de rede foram colocados")*

### 4.1 REST Lenovo — `https://lsa.lenovo.com`

📂 `src_decompiled/lenovo.mbg.service.common.webservices/lenovo/mbg/service/common/webservices/`

| Arquivo | Papel |
| :--- | :--- |
| `RsaWebClient.cs` | Cliente HTTP que assina requisições com RSA |
| `RsaHelper.cs` | Troca de chaves RSA (formato Java-X.509) |
| `WebApiContext.cs` | Contexto de sessão/token da API |
| `WebApiHttpRequest.cs`, `HttpMethod.cs` | Montagem de requisições (headers/token) |
| `WebApiUrl.cs` | **Mapa de endpoints da API Lenovo** |
| `WebApiServices/ApiService.cs`, `ApiBaseService.cs` | Chamadas REST de negócio |
| `WebApiServices/RsaService.cs` | Serviço de chave pública/assinatura |
| `WebApiServices/WarrantyService.cs` | Consulta de garantia |
| `WebApiModel/*` | DTOs: `RequestModel`, `ResponseModel`, `RSAKey`, `TokenExpiredModel`, `ToolVersionModel`, `FlashedDevModel`, `OrderItem`, `PriceInfo`, `RespOrders` |

Configuração de rede em `configs_and_manifests/Software Fix.exe.Config`:
`BaseHttpUrl=https://lsa.lenovo.com`, `ConnectionField=PLJoR50KSVLIIiQC`, `maxconnection=1024`.

### 4.2 Protocolo Socket com o telefone (app Mobile Assistant / Device Help)

📂 `src_decompiled/lenovo.mbg.service.framework.socket/lenovo/mbg/service/framework/socket/`

| Arquivo | Papel |
| :--- | :--- |
| `AES.cs`, `AESNew.cs` | Criptografia AES do canal |
| `RsaSocketDataSecurity.cs` (+Factory) | Segurança RSA handshake por socket |
| `MessageReader.cs`, `MessageWriter.cs`, `MessageReaderAndWriter.cs` | Framing de mensagens |
| `DataPacket.cs`, `SendDataPacket.cs`, `PackageSpliter.cs` | Pacotes e fragmentação |
| `FileTransfer.cs`, `FileReader.cs`, `FileWriter.cs`, `FileEntry/*`, `FileHeader/*` | Transferência de arquivos (backup/restore/clone) |
| `HeartbeatSocketWrapper.cs` | Keepalive |
| `SuperSocket*` (em Toolbox) | Engine TCP por baixo |

### 4.3 Demais serviços de sistema

| Área | Caminho |
| :--- | :--- |
| Controle do serviço/host (pipes) | `src_decompiled/lenovo.mbg.service.framework.hostcontroller/` + `...lmsa.hostproxy/` + `...framework.pipes/` |
| Download de firmware/ROMs | `src_decompiled/lenovo.mbg.service.framework.download/` |
| Comunicação ADB | `src_decompiled/SharpAdbClient/` |
| Gestão de dispositivo | `src_decompiled/lenovo.mbg.service.framework.devicemgt/` + `...smartdevice/` |
| Motor de flash (Rescue) | `src_decompiled/plugins/Rescue_8ab04aa9…/lenovo.mbg.service.lmsa.flash/` |
| Atualização de versão/plugins | `src_decompiled/lenovo.mbg.service.framework.updateversion/` |

---

## 5. Tema visual, assets e design tokens

📂 `src_decompiled/lenovo.themes.generic/` — 325 classes + **103 XAML legíveis** + 157 PNG.

Paleta principal extraída dos XAML (mais frequentes):

| Cor | Uso provável |
| :--- | :--- |
| `#FF005D7F` | Azul-marinho Lenovo (primário) |
| `#FF345375`, `#FF001429` | Tons escuros de fundo/bordas |
| `#FF43B5E2`, `#FF6EC2ED` | Azul claro (destaques/botões) |
| `#FF9FAEBF`, `#FFD4DCE3`, `#FFD1D1D1`, `#FFD3D3D3` | Cinzas de texto/bordas |
| `#00FFFFFF`, `#FFFFFFFF` | Transparente/branco |
| `#FF494949`, `#FFAAAAAA`, `#FF6D86AB` | Texto secundário |
| `#FFFF4042` | Vermelho de alerta/erro |

Estilos com chave versionada `v6_*` (ex.: `v6_listview_border_style`) confirmam o tema "V6".

> **Nota técnica:** o XAML gerado pelo ILSpy usa uma sintaxe expandida
> (`<x:TypeExtension>`/`<Ctor>`), excelente para leitura e comparação, mas não
> recompila diretamente no WPF/Avalonia — use como referência de paridade.

---

## 6. Internacionalização (i18n)

* Originais: `configs_and_manifests/lang/<locale>/<locale>` (ResourceDictionary XAML).
* Convertidos: `configs_and_manifests/lang_json/<locale>.json` — **18 idiomas × 2.060 chaves**
  (`ar-SA`, `bg-BG`, `cs-CZ`, `de-DE`, `en-US`, `es-ES`, `fr-FR`, `hi-IN`, `id-ID`, `it-IT`,
  `ja-JP`, `pl-PL`, **`pt-BR`**, `ro-RO`, `ru-RU`, `sk-SK`, `sr-RS`, `zh-CN`).
* O port já consome o pt-BR via `src/SoftwareFix.Core/Services/LanguageService.cs`.

---

## 7. Nativos e exclusões (conforme a especificação)

**Nativos ignorados corretamente** (sem metadados .NET): `BMAFrame9.dll`, `adb.exe`,
`fastboot.exe`, `fastbootmonitor.exe`, `AdbWinApi.dll`, `AdbWinUsbApi.dll`,
`WebView2Loader.dll`, `7z.dll`/`7z64.dll` (7zSharp), `libmp3lame.32.dll`,
`vcredist_x86_2010.exe`, `InstallUtil64.exe`, `Uninstall.exe` (NSIS).

**Bibliotecas de sistema Microsoft** ignoradas (conforme especificado):
`Microsoft.*`, `System.*`, `WebView2*`.

**Terceiros NÃO-Microsoft** descompilados como referência em `src_decompiled/_extras_ref/`:
BouncyCastle.Crypto (1.478 cs), Newtonsoft.Json, protobuf-net, log4net, Common.Logging*,
GoogleAnalytics.*, SharpVectors.* (SVG).

**Fora do escopo .NET:** `apk/lmsa.apk` (app Android do Mobile Assistant — necessário para
o protocolo socket), `Document/` (licença/aviso), pasta `logs/`.

---

## 8. Como reproduzir

```bash
cd ~/software-fix-agy
python3 scripts/decompile_software_fix.py          # execução completa (~3 min)
python3 scripts/decompile_software_fix.py --apenas <modulo>   # um módulo
python3 scripts/decompile_software_fix.py --sem-extras        # sem 3rd-party
python3 scripts/audit_decompiled.py                # auditoria de cobertura
python3 scripts/extract_lang_json.py               # idiomas → JSON
```

Requisitos: `ilspycmd` ≥ 10.1 no PATH (ver §2) e o disco da fonte montado em
`/run/media/dark/1224A54224A529A5/`.

---

## 9. Verificação (checklist)

- [x] Todos os assemblies-alvo da raiz descompilados (21/21, incl. nativo classificado)
- [x] Todos os plugins (24/24 gerenciados; 8 DLLs de sistema ignoradas por política)
- [x] 0 arquivos `.baml` crus restantes (353 convertidos → 358 XAML)
- [x] Configs/manifestos/idiomas copiados (12/12 itens)
- [x] Relatório JSON + log de execução gerados
- [ ] Fase 2: validar contratos REST com mitmproxy (requer rodar o original no Windows)
- [ ] Fase 5: homologação de paridade com dispositivo físico

---

## 10. Estado do port Linux (replicação)

Já existente em `src/`: `SoftwareFix.Core` (LenovoApiClient, RsaHelper, LanguageService),
`SoftwareFix.Device` (AdbRunner, FastbootRunner, DeviceMonitor), `SoftwareFix.Download`
(RomDownloader, ArchiveExtractor), `SoftwareFix.Flash` (FlashEngine, FlashFileParser),
`SoftwareFix.CLI`, `SoftwareFix.UI` (Avalonia, MainWindow.axaml ~75 KB + Assets/official).

Próximos incrementos recomendados (com base no material descompilado):
1. **Fase 2** — portar `WebApiUrl.cs`/`ApiService.cs` para `LenovoApiClient` (endpoints + assinatura RSA) e documentar contratos OpenAPI.
2. **Fase 3** — protocolo socket AES/RSA com o app MA (`framework.socket`) para Backup/Restore/Clone no Linux.
3. **Fase 4** — usar os 358 XAML + paleta da §5 para paridade pixel-perfect do tema v6.
