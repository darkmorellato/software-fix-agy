# Design Tokens — Software Fix (tema v6) v1.0

Tokens extraídos dos XAML descompilados (`src_decompiled/lenovo.themes.generic/**/*.xaml`
e `src_decompiled/Software_Fix/**/*.xaml`) e aplicados no tema Avalonia do port
(`src/SoftwareFix.UI/App.axaml`).

## Paleta oficial (por frequência no tema)

| Hex | Uso | Ocorrências (tema) |
| :--- | :--- | ---: |
| `#005D7F` | **Primário** — azul-marinho Lenovo (botões, abas ativas, links) | 139 |
| `#001429` | Fundo escuro (top bar, títulos) | 110 |
| `#345375` | Azul aço secundário | 99 |
| `#43B5E2` | Azul claro de destaque (accent) | 98 |
| `#9FAEBF` | Texto secundário / ícones inativos | 65 |
| `#D4DCE3` | Bordas claras (superfícies brancas) | 49 |
| `#6EC2ED` | Azul claro (hover/pontos) | 29 |
| `#494949` | Texto escuro em superfícies claras | 29 |
| `#6D86AB` | Texto cinza-azulado | 24 |
| `#AAAAAA` | Texto cinza neutro | 23 |
| `#FF4042` | **Erro / perigo** | 19 |
| `#404043` | Cinza escuro de apoio | 14 |
| `#FF0000` | Vermelho puro (alertas fortes) | 13 |
| `#D1D1D1`, `#D3D3D3` | Cinzas de borda alternativos | 13/11 |
| `#CEEAF9` | Azul suave informativo | 13 |
| `#40A995` | **Sucesso / verde** | 12 |
| `#272727` | Fundo escuro neutro | 12 |
| `#808080` | Cinza médio | 11 |
| `#00132A` | Fundo escuro alternativo (v6) | 10 |
| `#FAAD14` | **Aviso / amarelo** | 9 |
| `#FFFFFF` / `#00FFFFFF` | Branco / transparente | 273 / 222 |

## Tipografia

| Item | Valor observado no tema | Aplicação no port |
| :--- | :--- | :--- |
| Família principal | `Montserrat` (blocos v6) + fonte do sistema | Segue o padrão Fluent/Avalonia (Montserrat como opção) |
| Família CJK | `Microsoft Yahei` | N/A no Linux (fallback do sistema) |
| Tamanhos | 14 (base), 12, 16, 10, 9, 15, 8, 18, 13, 11, 22, 20, 24, 36 | Mesma escala usada no MainWindow.axaml |

## Dimensões de referência (janela oficial)

* Título das telas: `22px bold`; corpo: `12–14px`; legendas: `9–11px`.
* Barra superior: altura 60px (abas), 64px (barra total no port).
* Botões "pill" de ação: `CornerRadius=18`, padding `28,9`, texto branco sobre `#005D7F`.

## Mapeamento aplicado no Avalonia (`App.axaml`)

| Recurso | Valor | Token de origem |
| :--- | :--- | :--- |
| `TopBarBgColor` | `#001429` | Fundo escuro oficial |
| `ActiveTabTealColor` / `ActionPillTealColor` | `#005D7F` | Primário |
| `GreenSuccessColor` | `#40A995` | Verde oficial |
| `TextMutedBlueBrush` | `#9FAEBF` | Texto secundário |
| `V6AccentColor` | `#43B5E2` | Accent |
| `V6AccentLightColor` | `#6EC2ED` | Accent claro |
| `V6DangerColor` | `#FF4042` | Erro |
| `V6WarningColor` | `#FAAD14` | Aviso |
| `V6SteelColor` | `#345375` | Azul aço |
| `V6BorderColor` | `#D4DCE3` | Bordas |

> Os 358 arquivos `.xaml` descompilados (em `src_decompiled/`) contêm os estilos
> completos do tema (chaves `v6_*`, brushes, templates) e servem de referência
> direta para qualquer detalhe adicional de estilo.

## Componentes principais (paridade de telas)

| Componente oficial | Implementação no port (MainWindow.axaml) |
| :--- | :--- |
| Barra superior + abas (Rescue, Backup&Restore, Hardware Test, Data Transfer, My Device, Toolbox, Support) | ✔ Idêntica (7 abas + seletor de dispositivos + utilitários) |
| Status/conexão do aparelho | ✔ Cards de dispositivo (My Device / Rescue) com SKU, IMEI, SN, bateria, garantia |
| Seletor de modelo (busca) | ✔ Modal "SELEÇÃO MANUAL DE MODELO" com busca por mercado/modelo |
| Busca por IMEI/SN | ✔ Modal "ENTRADA MANUAL DE IMEI" |
| Progresso de download/gravação | ✔ ProgressBar + console de logs em tempo real |
| Diálogos de confirmação (wipe/bootloader) | ✔ Modal "CONFIRMAÇÃO DE RESGATE E OPÇÃO DE DADOS (WIPE / KEEP)" + guia Fastboot |
| Tela de sucesso (Rescue completed) | ✔ Com celebração e limpeza de firmware |
