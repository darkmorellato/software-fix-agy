# 🧩 Guia de Instalação — Software Fix (Linux Edition)

Instalação do port **Lenovo / Motorola Software Fix 7.6.2.10** para **qualquer
distribuição Linux x86_64** — Ubuntu, Debian, Mint, Pop!\_OS, Fedora, Nobara,
RHEL, Arch, Manjaro, **CachyOS**, EndeavourOS, openSUSE, e outras.

> **Três caminhos — escolha o seu:**
>
> | Caminho | Para quem | O que faz |
> | :--- | :--- | :--- |
> | **A. Script automático** (recomendado) | Todo mundo | Compila e instala no seu perfil (`~/.local`) — **sem root**¹ |
> | **B. Manual passo a passo** | Quem quer controle total | Você executa cada etapa |
> | **C. Sem instalar** | Testes rápidos | Compila e roda direto de `dist/` |
>
> ¹ Root só é necessário para instalar as **regras USB** (udev), opcional mas recomendado.

---

## ⚡ Instalação rápida (3 comandos)

Pré-requisito único: **.NET 8 SDK** instalado (veja §1.1 para a sua distro).

```bash
git clone https://github.com/darkmorellato/software-fix-agy.git
cd software-fix-agy
./scripts/install-user.sh          # compila + instala no ~/.local
```

Opcional (recomendado) — permissões USB sem `sudo`:

```bash
./scripts/install-user.sh --with-udev
```

Pronto! Abra pelo menu de aplicativos (**"Software Fix"**) ou rode:

```bash
software-fix-ui        # interface gráfica
software-fix --help    # linha de comando
```

---

## 1. Requisitos

### 1.1 .NET 8 SDK (necessário para compilar)

Os executáveis gerados são **self-contained** (o runtime vai embutido), mas a
**compilação** exige o SDK 8.

| Distribuição | Comando |
| :--- | :--- |
| **Ubuntu 24.04+ / Mint 22+ / Pop!\_OS** | `sudo apt update && sudo apt install -y dotnet-sdk-8.0` |
| **Ubuntu 22.04 / Debian 12** | Use o repositório da Microsoft (exemplo Ubuntu 22.04):
`wget -q https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O /tmp/ms.deb`<br>`sudo dpkg -i /tmp/ms.deb && sudo apt update && sudo apt install -y dotnet-sdk-8.0` |
| **Fedora / Nobara / RHEL 9** | `sudo dnf install -y dotnet-sdk-8.0` |
| **Arch / Manjaro / CachyOS / EndeavourOS** | `sudo pacman -S --needed dotnet-sdk` |
| **openSUSE Tumbleweed** | `sudo zypper install -y dotnet-sdk-8.0` |
| **openSUSE Leap 15.6** | `sudo zypper install -y dotnet-sdk-8.0` |
| **Qualquer distro (universal)** | `curl -sSL https://dot.net/v1/dotnet-install.sh \| bash -s -- --channel 8.0`<br>`echo 'export PATH="$HOME/.dotnet:$PATH"' >> ~/.bashrc && export PATH="$HOME/.dotnet:$PATH"` |

Verifique: `dotnet --version` → deve mostrar `8.x`.

### 1.2 Ferramentas Android (`adb` e `fastboot`)

| Distribuição | Comando |
| :--- | :--- |
| **Ubuntu / Debian / Mint** | `sudo apt install -y android-tools-adb android-tools-fastboot` |
| **Fedora / RHEL** | `sudo dnf install -y android-tools` |
| **Arch / Manjaro / CachyOS** | `sudo pacman -S --needed android-tools` |
| **openSUSE** | `sudo zypper install -y android-tools` |

### 1.3 Bibliotecas gráficas (Avalonia UI)

O app usa **Avalonia 11** (X11/Wayland). Instale o conjunto abaixo:

| Distribuição | Comando |
| :--- | :--- |
| **Ubuntu / Debian / Mint** | `sudo apt install -y libice6 libsm6 libfontconfig1 libx11-xcb1 libxrandr2 libxi6 libxtst6 libxcursor1 libgl1 libegl1 libglib2.0-0` |
| **Fedora** | `sudo dnf install -y libICE libSM fontconfig libX11-xcb libXrandr libXi libXtst libXcursor mesa-libGL mesa-libEGL glib2` |
| **Arch / Manjaro / CachyOS** | `sudo pacman -S --needed libice libsm fontconfig libx11 libxrandr libxi libxtst libxcursor mesa glib2` |
| **openSUSE** | `sudo zypper install -y libICE6 libSM6 fontconfig libX11-xcb1 libXrandr2 libXi6 libXtst6 libXcursor1 Mesa-libGL1 Mesa-libEGL1 glib2` |

> Na maioria dos desktops (GNOME/KDE) essas libs já vêm instaladas. O erro
> típico quando falta alguma é `libX11-xcb.so.1: cannot open shared object file`.

### 1.4 Permissões USB (regras udev) — recomendado

Sem as regras, `adb`/`fastboot` exigirão `sudo`. Com elas, tudo funciona com o
usuário normal (regra de ouro do Software Fix):

```bash
cd software-fix-agy
sudo cp udev/51-motorola-lenovo.rules /etc/udev/rules.d/
sudo udevadm control --reload-rules && sudo udevadm trigger
sudo usermod -aG plugdev "$USER"    # se o grupo existir na sua distro
```

Depois **desconecte/reconecte o cabo** (e relogue, se adicionou o grupo).
As regras cobrem: **Motorola** (`22b8`), **Lenovo** (`17ef`), **Qualcomm EDL**
(`05c6:9008`), **MediaTek Preloader/BootROM** (`0e8d`), **Unisoc** (`1782`) e
**Android genérico** (`18d1`).

> **Arch/CachyOS:** não existe grupo `plugdev` — nada a fazer: as regras usam
> `TAG+="uaccess"` (systemd-logind), que já funciona para o usuário da sessão.
>
> **Conflito com ModemManager** (aparelho cai no meio do flash em algumas
> distros):
> ```bash
> echo 'SUBSYSTEM=="usb", ATTRS{idVendor}=="22b8", ENV{ID_MM_DEVICE_IGNORE}="1"' | \
>   sudo tee /etc/udev/rules.d/52-motorola-modemmanager-ignore.rules
> sudo udevadm control --reload-rules && sudo udevadm trigger
> ```

Diagnóstico rápido de tudo: `./scripts/check_device_env.sh`

---

## 2. Instalação passo a passo

### Caminho A — Script automático (recomendado)

```bash
git clone https://github.com/darkmorellato/software-fix-agy.git
cd software-fix-agy
./scripts/install-user.sh            # use --with-udev p/ regras USB
```

O script:
1. Compila os binários self-contained (`scripts/build-linux.sh`);
2. Instala em `~/.local/share/software-fix/`;
3. Cria os comandos `software-fix-ui` e `software-fix` em `~/.local/bin/`;
4. Registra o atalho **"Software Fix"** no menu de aplicativos + ícone;
5. Com `--with-udev`, instala as regras USB do sistema.

Outras opções: `--skip-build` (usa o `dist/` existente) e `--uninstall`.

> **PATH:** garanta que `~/.local/bin` está no seu `PATH` (padrão em
> Ubuntu/Fedora/Arch/openSUSE modernos). Se `software-fix-ui` não for
> encontrado, adicione: `echo 'export PATH="$HOME/.local/bin:$PATH"' >> ~/.bashrc`

### Caminho B — Manual

```bash
# 1) Clonar e compilar
git clone https://github.com/darkmorellato/software-fix-agy.git
cd software-fix-agy
./scripts/build-linux.sh
#   → gera dist/ui/SoftwareFix.UI e dist/cli/SoftwareFix.CLI (self-contained)

# 2) Copiar para o seu perfil
mkdir -p ~/.local/share/software-fix ~/.local/bin
cp dist/ui/SoftwareFix.UI ~/.local/share/software-fix/
cp dist/cli/SoftwareFix.CLI ~/.local/share/software-fix/

# 3) Comandos no PATH
ln -sf ~/.local/share/software-fix/SoftwareFix.UI ~/.local/bin/software-fix-ui
ln -sf ~/.local/share/software-fix/SoftwareFix.CLI ~/.local/bin/software-fix

# 4) Atalho no menu + ícone
mkdir -p ~/.local/share/applications ~/.local/share/icons/hicolor/256x256/apps
cp src/SoftwareFix.UI/Assets/software-fix.png ~/.local/share/icons/hicolor/256x256/apps/
sed "s|Execcaminho|$HOME/.local/bin/software-fix-ui|" <<'EOF' > ~/.local/share/applications/software-fix.desktop
[Desktop Entry]
Name=Software Fix
Comment=Lenovo & Motorola Rescue and Smart Assistant
Execcaminho
Terminal=false
Type=Application
Icon=software-fix
Categories=Utility;Development;
EOF

# 5) Regras USB (ver §1.4)
```

### Caminho C — Sem instalar (execução direta)

```bash
git clone https://github.com/darkmorellato/software-fix-agy.git
cd software-fix-agy
./scripts/build-linux.sh
./dist/ui/SoftwareFix.UI        # GUI
./dist/cli/SoftwareFix.CLI --help
```

---

## 3. Primeiros passos

```bash
software-fix api-test     # valida a conexão com os servidores da Lenovo (ao vivo)
software-fix-ui           # abre a interface gráfica

# com um aparelho conectado:
software-fix scan                     # detecta o dispositivo (ADB/Fastboot)
software-fix login                    # login Motorola/Lenovo (abre o navegador, OAuth2/PKCE)
software-fix match                    # identifica modelo/IMEI e localiza a firmware
software-fix download                 # baixa a ROM oficial
software-fix flash ./flashfile.xml    # executa o resgate (grava partições)
```

Na **GUI**: escolha **Rescue** → conecte o aparelho (IMEI, Wi-Fi ou Fastboot) →
**Download** → **Rescue**. O console de logs (canto superior direito) mostra
tudo em tempo real.

---

## 4. Atualização e desinstalação

```bash
cd software-fix-agy
git pull
./scripts/install-user.sh            # recompila e reinstala por cima

./scripts/install-user.sh --uninstall   # remove do perfil do usuário
```

---

## 5. Solução de problemas

| Sintoma | Causa provável | Solução |
| :--- | :--- | :--- |
| `libX11-xcb.so.1: cannot open shared object file` | Falta lib gráfica | §1.3 |
| Janela não abre / tela preta | Compositor Wayland exótico | Rode em X11: `unset WAYLAND_DISPLAY software-fix-ui` (instale `xwayland` se preciso) |
| `fastboot devices` vazio / "no permissions" | udev não instalado ou cabo | §1.4, depois reconecte; `adb kill-server && adb start-server` |
| Aparelho desconecta no meio do flash | ModemManager / porta USB fraca | Nota do ModemManager (§1.4); use USB direto da placa (sem hub) |
| GPU antiga / travamentos gráficos | Driver | `LIBGL_ALWAYS_SOFTWARE=1 software-fix-ui` |
| `dotnet: command not found` | PATH do dotnet-install | `export PATH="$HOME/.dotnet:$PATH"` (§1.1) |
| `software-fix-ui: command not found` | `~/.local/bin` fora do PATH | Veja a nota do Caminho A |
| Distro imutável (Silverblue/Kinoite/Bazzite) | `/usr` somente leitura | Use Caminho A em `toolbox`/`distrobox` (ou use `--skip-build` com dotnet do host) |
| NixOS | Sem dotnet global | `nix-shell -p dotnet-sdk_8 android-tools` e siga o Caminho C |
| Alpine/musl | .NET self-contained não é suportado em musl | Use uma distro glibc (ou container `distrobox`) |

**Verificação completa do ambiente USB:** `./scripts/check_device_env.sh`

---

## 6. Escopo e compatibilidade

- **Arquitetura:** x86_64 (Linux). ARM64 compila, mas não é validado oficialmente.
- **Testado em:** CachyOS/Arch, kernel 7.x, KDE Plasma (Wayland) — referência de desenvolvimento do projeto.
- **Display:** X11 e Wayland (via Avalonia).
- **Hardware:** qualquer aparelho Motorola/Lenovo com ADB ou Fastboot; modo EDL/MTK coberto pelas regras udev.
