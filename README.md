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

## 🛠️ Requisitos no Linux

1. **.NET 8 SDK ou Runtime**:
   ```bash
   # Ubuntu/Debian:
   sudo apt install -y dotnet-sdk-8.0 android-tools-adb android-tools-fastboot

   # Fedora:
   sudo dnf install -y dotnet-sdk-8.0 android-tools

   # Arch Linux:
   sudo pacman -S dotnet-sdk android-tools
   ```

2. **Regras udev (Permissões de USB sem root)**:
   Para que o Fastboot e o ADB acessem as portas USB do Motorola/Lenovo sem exigir `sudo`:
   ```bash
   sudo cp udev/51-motorola-lenovo.rules /etc/udev/rules.d/
   sudo udevadm control --reload-rules && sudo udevadm trigger
   sudo usermod -aG plugdev $USER
   ```

---

## 📦 Como Compilar e Executar

### 1. Executar a Interface Gráfica (Desktop):
```bash
cd src/SoftwareFix.UI
dotnet run
```

### 2. Executar a Ferramenta CLI:
```bash
cd src/SoftwareFix.CLI
dotnet run -- scan
dotnet run -- match
dotnet run -- flash /caminho/para/flashfile.xml
```

### 3. Gerar Binários Autônomos (Self-Contained) para Linux:
```bash
chmod +x scripts/build-linux.sh
./scripts/build-linux.sh
```
Os executáveis prontos serão gerados na pasta `dist/`:
- `dist/ui/SoftwareFix.UI` (Interface Desktop)
- `dist/cli/SoftwareFix.CLI` (Linha de Comando)

---

## 📂 Comparativo com o Aplicativo Windows Original

| Recurso Original (Windows) | Equivalente no Port Linux | Vantagem do Port Linux |
| :--- | :--- | :--- |
| Interface WPF (.NET Framework 4.7.2) | Avalonia UI (.NET 8 LTS) | Funciona em Linux (X11 / Wayland), macOS e Windows com aceleração gráfica |
| `AdbWinApi.dll` / `adb.exe` | Linux native `fastboot` e `adb` | Maior velocidade de transferência e compatibilidade nativa com o kernel Linux |
| Criptografia DPAPI do Windows | Padrão seguro compatível Linux | Arquivos salvos nos diretórios XDG do usuário sem travar credenciais |
| `LmsaWindowsService.exe` | Daemon não-obrigatório / regras udev | Dispensa serviços em background em execução constante consumindo RAM |
| Resgate Fastboot Motorola | `SoftwareFix.Flash` (motor assíncrono nativo) | Suporte completo a `flashfile.xml` idêntico ao original |
