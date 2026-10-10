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
* **Fluxo MediaTek (PreLoader + DA assinado)** — `software-fix mtk-status / mtk-connect /
  mtk-dump / mtk-write-test / mtk-flash / mtk-fix-boot / mtk-roms`: **resgate completo
  100% no Linux, sem Windows — validado ponta a ponta no moto g17 em 10/10/2026**
  (receita e regras em **docs/MTK_RESCUE.md**).

### 🇧🇷 Fluxo padrão do colaborador (atualização/resgate de aparelho Motorola)

```bash
# 0) ver o ambiente + ROMs baixadas
software-fix mtk-status
software-fix mtk-roms                 # pasta: ~/.local/share/software-fix/download/RomFiles

# 1) RESGATE COMPLETO em UMA sessão (flash + correção de boot + verificação):
software-fix mtk-flash --dir <pasta-da-ROM-extraída> --fix-boot --verify
#   → o comando guia o ritual: desconectar o cabo, desligar (Power 20-30s),
#     reconectar SEGURANDO Volume ↓; depois é só aguardar.

# 1b) ATUALIZAR MANTENDO OS DADOS DO CLIENTE (sem formatar):
software-fix mtk-flash --dir <pasta-da-ROM-extraída> --keep-data
#   → usa o scatter oficial *_reserve_userdata* (userdata preservado; sem limpezas).
#     Use em aparelho funcionando que só precisa da ROM mais nova.

# 2) Se o aparelho já foi gravado mas ficou preso (tela preta/PreLoader):
software-fix mtk-fix-boot
```

O `--fix-boot` reproduz o que o tool oficial faz no fastboot (limpar metadata + estado
de slot e fixar o slot A) — sem precisar de fastboot. O `mtk-fix-boot` mostra o
diagnóstico do bloco de slot (misc) em PT-BR.

### Modelos trabalhados (fluxo)

| Modelo | Chip | Fluxo |
| :--- | :--- | :--- |
| Moto G17 (XT2623-1) | MediaTek MT6768 (`lamuc`) | ✅ **Fluxo MTK validado ponta a ponta** |
| Moto G06 | MediaTek (confirmar família) | ⏳ receita/DA/scatter do modelo pendentes |
| Moto G56 | MediaTek Dimensity 7060 | ⏳ receita/DA/scatter do modelo pendentes |
| Moto Edge 60 Fusion | MediaTek Dimensity 7300/7400 | ⏳ receita/DA/scatter do modelo pendentes |
| Moto Edge 70 Fusion | **Qualcomm** Snapdragon 7s Gen 3 (SM7635) | ⚠️ fluxo Qualcomm (trabalho separado) |

Cada família MediaTek precisa da sua receita pública (`Rescue_MTK_*.json` no S3 Lenovo),
do seu **DA assinado** e do seu **scatter** — a mesma receita deste port se aplica.

* **Referência de engenharia reversa** — código descompilado em `src_decompiled/`
  (6.282 `.cs` + 358 `.xaml` + assets; relatório em `docs/RELATORIO_DESCOMPILACAO.md`).

### Limitações conhecidas

* **Modelos Motorola MediaTek recentes** (ex.: **Moto G17/XT2623**, família `lamuc`;
  também G24/G34/G54/G84): o resgate oficial roda em **modo PreLoader** com o
  **SP Flash Tool Console customizado ("LamuC")** e o **DA assinado**
  (`DA_A15_lamuc_FORBID_SIGNED.bin`). O port **replica esse fluxo no Linux** via
  `mtkclient` — **validado ponta a ponta no aparelho em 10/10/2026**: GT/partições lidas,
  flash de 5,88 GB (17 partições) gravado e reconferido por releitura, correção de boot
  via `misc`/`metadata` e **Android de pé** (sem Windows em nenhum passo). Regras de
  campo e receita em **docs/MTK_RESCUE.md**. Aparelhos Qualcomm (ex.: Edge 70 Fusion)
  não usam este fluxo — trabalho separado.
* **Downloads de ROM por modelo**: hoje o `.zip` da ROM é colocado manualmente em
  `~/.local/share/software-fix/download/RomFiles` (`software-fix mtk-roms` lista e
  confere). O download automático por modelo (receita pública → URL do S3 Lenovo) é a
  próxima iteração.

---

## 📂 Comparativo com o Aplicativo Windows Original

| Recurso Original (Windows) | Equivalente no Port Linux | Vantagem do Port Linux |
| :--- | :--- | :--- |
| Interface WPF (.NET Framework 4.7.2) | Avalonia UI (.NET 8 LTS) | Funciona em Linux (X11 / Wayland), macOS e Windows com aceleração gráfica |
| `AdbWinApi.dll` / `adb.exe` | Linux native `fastboot` e `adb` | Maior velocidade de transferência e compatibilidade nativa com o kernel Linux |
| Criptografia DPAPI do Windows | Padrão seguro compatível Linux | Arquivos salvos nos diretórios XDG do usuário sem travar credenciais |
| `LmsaWindowsService.exe` | Daemon não-obrigatório / regras udev | Dispensa serviços em background em execução constante consumindo RAM |
| Resgate Fastboot Motorola | `SoftwareFix.Flash` (motor assíncrono nativo) | Suporte completo a `flashfile.xml` idêntico ao original |
