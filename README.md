# Software Fix - Linux Edition (Port Oficial C# / Avalonia / .NET 8)

Port completo e cross-platform do **Lenovo / Motorola Software Fix** (anteriormente *Rescue and Smart Assistant* / LMSA), reescrito em **.NET 8** moderno e **Avalonia UI** para execução 100% nativa em sistemas **Linux** (Ubuntu, Debian, Fedora, Arch, Manjaro, openSUSE, etc.).

---

## 🚀 Arquitetura e Módulos do Projeto

O projeto foi decomposto em bibliotecas limpas e modulares que eliminam todas as dependências legadas do Windows (WPF, Win32 P/Invoke, Registro, DPAPI e Serviços Windows):

| Módulo | Descrição |
| :--- | :--- |
| **`SoftwareFix.Core`** | Biblioteca base com modelos de dados, resolução de diretórios XDG (`~/.local/share/software-fix`), cliente REST autenticado da Lenovo (`https://lsa.lenovo.com`), criptografia RSA (troca de chaves Java-X.509) e gerador de impressões digitais de requisição (`X-Device-Fingerprint`). |
| **`SoftwareFix.Device`** | Monitor de dispositivos USB em tempo real. Identifica conexões em modo **Fastboot** e **ADB**, lê propriedades do bootloader (`sku`, `imei`, `ro.carrier`, `fsgVersion`, `fingerprint`, `dualsim`) e comanda reinicializações. |
| **`SoftwareFix.Download`** | Gerenciador de download de firmwares multithread com suporte a resume (HTTP Range), cálculo de velocidade em tempo real (Mbps), verificação de integridade MD5/SHA256 e descompactação nativa de arquivos `.zip`, `.tar` e `.tgz`. |
| **`SoftwareFix.Flash`** | Motor de gravação (Rescue/Flash) que interpreta arquivos XML (`flashfile.xml`) e scripts Motorola em tempo real, executando gravações seguras de partições com controle de timeouts e progresso por etapa. |
| **`SoftwareFix.CLI`** | Utilitário completo de linha de comando (`software-fix`) ideal para terminais, servidores ou usuários avançados. |
| **`SoftwareFix.UI`** | Interface gráfica moderna construída em **Avalonia UI 11**, com tema escuro idêntico ao aplicativo original, cartões de diagnóstico do aparelho, busca automática de ROM e terminal de logs integrado. |

---

## 🛠️ Instalação (todas as distros Linux)

> 📖 **Guia completo** (Ubuntu, Debian, Mint, Fedora, Arch, CachyOS, Manjaro, openSUSE e mais — com tabelas de dependências por distro, regras USB e troubleshooting): **[INSTALL.md](INSTALL.md)**

**Instalação rápida** (requer apenas o .NET 8 SDK — veja o INSTALL.md para instalar por distro):

```bash
git clone https://github.com/darkmorellato/software-fix-agy.git
cd software-fix-agy
./scripts/install-user.sh --with-udev   # compila, instala no ~/.local e configura o USB
```

Depois:
- **GUI:** `software-fix-ui` (ou no menu de aplicativos → "Software Fix")
- **CLI:** `software-fix --help`

**Sem instalar** (execução direta da pasta do projeto):
```bash
./scripts/build-linux.sh
./dist/ui/SoftwareFix.UI                # GUI
./dist/cli/SoftwareFix.CLI api-test     # CLI (teste de contrato ao vivo)
```

**Diagnóstico do ambiente USB:** `./scripts/check_device_env.sh`

---

## 🔬 Paridade com o aplicativo oficial (v2)

* **Contratos de rede 1:1** — headers, envelope JSON e assinatura `X-Device-Fingerprint` (RSA)
  replicados de `lenovo.mbg.service.common.webservices` e validados ao vivo contra
  `lsa.lenovo.com` (ver `docs/CONTRATOS_REDE.md`).
* **Login Lenovo ID** — `software-fix login` obtém a URL OAuth2/PKCE oficial, abre no
  navegador e valida o token WUST em `/user/getSFUserInfo.jhtml`, salvando a sessão em
  `~/.config/software-fix/session.json`.
* **Smoke test dos contratos** — `software-fix api-test`.
* **Design tokens oficiais** — paleta, tipografia e componentes extraídos do tema v6
  descompilado (ver `docs/DESIGN_TOKENS.md`).
* **Testes de paridade sem hardware** — mocks de fastboot/adb em `tests/parity/`,
  checklist completo em `docs/HOMOLOGACAO.md`.
* **Referência de engenharia reversa** — código descompilado em `src_decompiled/`
  (6.282 `.cs` + 358 `.xaml` + assets; relatório em `docs/RELATORIO_DESCOMPILACAO.md`).

### Limitações conhecidas

* **Modelos Motorola MediaTek recentes** (ex.: Moto G17, G24, G34, G54, G84): a
  **autenticação por assinatura** do resgate ("GPS auth" / `SLA_Challenge`) está
  **implementada no port** — `software-fix odm-server` / `odm-sign` (detalhes em
  `docs/ODM_SLA_AUTH.md`) — mas ainda **não validada em aparelho real** e **não
  integrada** ao fluxo automático de flash (o cliente do socket usado pelo fluxo
  oficial segue em investigação). Aparelhos Qualcomm (fluxo testado) não a utilizam.

---

## 📂 Comparativo com o Aplicativo Windows Original

| Recurso Original (Windows) | Equivalente no Port Linux | Vantagem do Port Linux |
| :--- | :--- | :--- |
| Interface WPF (.NET Framework 4.7.2) | Avalonia UI (.NET 8 LTS) | Funciona em Linux (X11 / Wayland), macOS e Windows com aceleração gráfica |
| `AdbWinApi.dll` / `adb.exe` | Linux native `fastboot` e `adb` | Maior velocidade de transferência e compatibilidade nativa com o kernel Linux |
| Criptografia DPAPI do Windows | Padrão seguro compatível Linux | Arquivos salvos nos diretórios XDG do usuário sem travar credenciais |
| `LmsaWindowsService.exe` | Daemon não-obrigatório / regras udev | Dispensa serviços em background em execução constante consumindo RAM |
| Resgate Fastboot Motorola | `SoftwareFix.Flash` (motor assíncrono nativo) | Suporte completo a `flashfile.xml` idêntico ao original |
