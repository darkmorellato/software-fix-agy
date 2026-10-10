---
version: alpha
name: Software Fix Linux
description: >-
  Port fiel do Lenovo / Motorola Software Fix 7.6.2.10 (tema v6) para
  Avalonia/.NET 8. Paleta, tipografia e componentes extraídos dos XAML oficiais
  (lenovo.themes.generic) e aplicados em src/SoftwareFix.UI.
colors:
  # --- Identidade primária (tema v6) ---
  primary: "#005D7F"          # azul-marinho Lenovo — botões, abas ativas, links (139x no tema)
  primary-hover: "#007DA6"
  primary-pressed: "#004E6B"
  deep: "#001429"             # fundo escuro principal (top bar, títulos) (110x)
  panel: "#10253B"            # painel escuro secundário (badges, chips)
  console: "#101926"          # superfície do console de logs
  steel: "#345375"            # azul aço secundário (99x)
  accent: "#43B5E2"           # azul claro de destaque (98x)
  accent-light: "#6EC2ED"
  # --- Texto ---
  muted: "#9FAEBF"            # texto secundário sobre escuro (65x)
  slate: "#6D86AB"            # texto de botão fantasma
  ink: "#1E293B"              # texto de valores sobre superfície clara
  dark-text: "#494949"
  # --- Superfícies claras ---
  surface: "#F8FAFC"
  surface-border: "#E2E8F0"
  white: "#FFFFFF"
  # --- Superfícies escuras interativas ---
  tab-hover: "#0A223B"
  tab-pressed: "#0F2A45"
  tab-active-hover: "#00719B"
  ghost-hover: "#132B45"
  ghost-pressed: "#0C1B2E"
  card-bg: "#0C233B"
  card-hover: "#133152"
  card-pressed: "#0A1E33"
  field: "#061A2D"
  # --- Tabelas e sidebar (padrões Hardware Test / My Device) ---
  surface-alt: "#F1F4F8"      # painéis de conteúdo (Data Transfer)
  surface-subtle: "#F5F8FB"   # linha alternada das tabelas (zebra)
  table-header: "#ECEFF3"     # cabeçalho de tabela
  chip-bg: "#E4F0F7"          # chip circular de ícone
  hover-tint: "#EAF2F8"       # hover de itens de sidebar
  row-title: "#334155"        # texto de linhas de tabela / itens de sidebar
  # --- Semânticas ---
  success: "#40A995"
  danger: "#FF4042"
  warning: "#FAAD14"
  info-soft: "#CEEAF9"
  lenovo-red: "#E2231A"
typography:
  display:
    fontFamily: Montserrat
    fontSize: 36px
    fontWeight: 700
    lineHeight: 1.15
  h1:
    fontFamily: Montserrat
    fontSize: 22px
    fontWeight: 700
    lineHeight: 1.25
  h2:
    fontFamily: Montserrat
    fontSize: 18px
    fontWeight: 700
    lineHeight: 1.3
  h3:
    fontFamily: Montserrat
    fontSize: 16px
    fontWeight: 600
    lineHeight: 1.35
  body-lg:
    fontFamily: Montserrat
    fontSize: 14px
    fontWeight: 400
    lineHeight: 1.45
  body:
    fontFamily: Montserrat
    fontSize: 13px
    fontWeight: 400
    lineHeight: 1.45
  body-sm:
    fontFamily: Montserrat
    fontSize: 12px
    fontWeight: 400
    lineHeight: 1.4
  caption:
    fontFamily: Montserrat
    fontSize: 11px
    fontWeight: 400
    lineHeight: 1.35
  label:
    fontFamily: Montserrat
    fontSize: 10px
    fontWeight: 600
    lineHeight: 1.3
  button:
    fontFamily: Montserrat
    fontSize: 14px
    fontWeight: 700
    lineHeight: 1
rounded:
  xs: 4px
  sm: 6px
  md: 8px
  lg: 12px
  xl: 14px
  2xl: 16px
  pill: 18px
  full: 9999px
spacing:
  xs: 4px
  sm: 8px
  md: 12px
  lg: 16px
  xl: 20px
  2xl: 24px
  3xl: 40px
components:
  # --- Navegação superior ---
  top-tab:
    backgroundColor: "{colors.deep}"
    textColor: "{colors.muted}"
    typography: "{typography.body-sm}"
    height: 60px
    padding: 14px
  top-tab-hover:
    backgroundColor: "{colors.tab-hover}"
    textColor: "{colors.white}"
  top-tab-pressed:
    backgroundColor: "{colors.tab-pressed}"
    textColor: "{colors.white}"
  top-tab-active:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.white}"
  top-tab-active-hover:
    backgroundColor: "{colors.tab-active-hover}"
    textColor: "{colors.white}"
  # --- Ações ---
  button-primary:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.white}"
    typography: "{typography.button}"
    rounded: "{rounded.pill}"
    padding: 28px
  button-primary-hover:
    backgroundColor: "{colors.primary-hover}"
    textColor: "{colors.white}"
  button-primary-pressed:
    backgroundColor: "{colors.primary-pressed}"
    textColor: "{colors.white}"
  button-secondary:
    textColor: "{colors.slate}"
    typography: "{typography.body-sm}"
    rounded: "{rounded.xl}"
    padding: 12px
  button-secondary-hover:
    backgroundColor: "{colors.ghost-hover}"
    textColor: "{colors.white}"
  button-secondary-pressed:
    backgroundColor: "{colors.ghost-pressed}"
    textColor: "{colors.white}"
  # --- Cards ---
  card-dark:
    backgroundColor: "{colors.card-bg}"
    textColor: "{colors.white}"
    rounded: "{rounded.lg}"
    padding: 20px
  card-dark-hover:
    backgroundColor: "{colors.card-hover}"
    textColor: "{colors.white}"
  card-dark-pressed:
    backgroundColor: "{colors.card-pressed}"
    textColor: "{colors.white}"
  card-light:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.deep}"
    rounded: "{rounded.2xl}"
    padding: 20px
  # --- Elementos utilitários ---
  badge-linux:
    backgroundColor: "{colors.panel}"
    textColor: "{colors.success}"
    typography: "{typography.label}"
    rounded: "{rounded.xs}"
    padding: 6px
  status-pill:
    backgroundColor: "{colors.white}"
    typography: "{typography.body-sm}"
    rounded: "{rounded.sm}"
    padding: 10px
  console-view:
    backgroundColor: "{colors.console}"
    textColor: "{colors.muted}"
    typography: "{typography.body-sm}"
  text-field-dark:
    backgroundColor: "{colors.field}"
    textColor: "{colors.white}"
    typography: "{typography.body-sm}"
    rounded: "{rounded.md}"
  # --- Conteúdo e estados ---
  spec-value:
    textColor: "{colors.ink}"
    typography: "{typography.body-sm}"
  divider:
    backgroundColor: "{colors.surface-border}"
    height: 1px
  status-error:
    textColor: "{colors.danger}"
    typography: "{typography.body-sm}"
  status-warning:
    textColor: "{colors.warning}"
    typography: "{typography.body-sm}"
  # --- Tabelas (Hardware Test) ---
  table-head:
    backgroundColor: "{colors.table-header}"
    textColor: "{colors.row-title}"
    typography: "{typography.body-sm}"
    height: 40px
  table-row:
    backgroundColor: "{colors.white}"
    textColor: "{colors.row-title}"
    typography: "{typography.body-sm}"
    height: 46px
  table-row-alt:
    backgroundColor: "{colors.surface-subtle}"
    textColor: "{colors.row-title}"
  icon-chip:
    backgroundColor: "{colors.chip-bg}"
    textColor: "{colors.primary}"
    rounded: "{rounded.full}"
    size: 28px
  # --- Sidebar de categorias (My Device) ---
  sidebar-item:
    textColor: "{colors.row-title}"
    typography: "{typography.body}"
    rounded: "{rounded.pill}"
    padding: 14px
  sidebar-item-hover:
    backgroundColor: "{colors.hover-tint}"
    textColor: "{colors.primary}"
  sidebar-item-active:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.white}"
  # --- Painéis e rodapé ---
  transfer-panel:
    backgroundColor: "{colors.surface-alt}"
    rounded: "{rounded.xl}"
    padding: 18px
  feedback-button:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.white}"
    typography: "{typography.caption}"
    rounded: "{rounded.full}"
    padding: 16px
---

## Overview

O Software Fix Linux é o port fiel do utilitário oficial **Lenovo / Motorola
Software Fix 7.6.2.10** para Linux (Avalonia UI + .NET 8). A identidade visual
vem 100% do tema original **v6** descompilado (`lenovo.themes.generic`):
superfícies claras para o conteúdo, uma barra superior azul-marinho profunda e
um único azul de ação (`primary`) conduzindo tudo que é interativo.

Princípios:

- **Fidelidade primeiro:** todos os valores deste arquivo foram extraídos dos
  XAML oficiais e conferidos contra o app original — esta é a fonte única de
  verdade para novas telas e para os agentes que gerarem UI.
- **Contraste garantido nos estados interativos:** hover/pressed dos botões
  usam fundo escuro + texto branco (o tema padrão do Avalonia pintava o fundo
  por cima e sumia com o texto — ver `docs/RELATORIO_FINAL_FASES.md`).
- **Claro e escuro convivem:** o chrome (top bar, modais, console) é escuro;
  as áreas de trabalho (cards, tabelas, formulários) são claras.

## Colors

- **Primary (#005D7F):** azul-marinho Lenovo — a única cor de ação de alta
  ênfase (botões pill, aba ativa). Presente 139× no tema original.
- **Deep (#001429):** fundo escuro institucional — top bar, títulos e texto
  principal sobre superfícies claras.
- **Muted (#9FAEBF):** texto secundário sobre fundos escuros (ícones de abas
  inativas, console). Sobre fundos claros, prefira `slate`/`ink` (ver §Do's).
- **Slate (#6D86AB) / Ink (#1E293B):** textos de conteúdo sobre superfícies
  claras (`surface`).
- **Success / Danger / Warning:** semânticas de diagnóstico (bateria ok,
  falha de flash, avisos de dados). `success` em `panel` passa WCAG AA (5.4:1).
- **Lenovo Red (#E2231A):** exclusivo da marca Lenovo; não usar para ações.
- **Reserva do tema v6:** `steel (#345375)`, `accent (#43B5E2)`,
  `accent-light (#6EC2ED)`, `info-soft (#CEEAF9)` e `dark-text (#494949)` ficam
  definidas para paridade integral com o original. Hoje nenhum componente as
  consome — o linter as marca como `orphaned-tokens` por design.

## Typography

Montserrat é a direção tipográfica do produto (assets oficiais em
`src/SoftwareFix.UI/Assets/official/font/`). A escala observada no app vai de
`micro` (9px) a `display` (36px); `body-sm` (12px) e `body-lg` (14px) dominam
os painéis. A UI do port usa o fallback do sistema até a adoção global da fonte
registrada no Avalonia.

## Layout

Grade base de 8px: `xs 4 · sm 8 · md 12 · lg 16 · xl 20 · 2xl 24 · 3xl 40`.
As telas usam margem externa de `3xl` (40px), cards lado a lado em 2 colunas
iguais com `lg` de respiro interno e listas verticais com `sm` entre itens. A
barra superior tem 64px (abas de 60px); a janela padrão é 1180×760.

## Elevation & Depth

- Chrome escuro (top bar, console, modais) é **plano** — profundidade vem da
  cor, não de sombra.
- Cards claros elevam com sombra suave: `0 4 12 rgba(0,0,0,0.15)`.
- Estados de sucesso usam glow colorido: `0 8 20 rgba(64,169,149,0.25)`.
- Modais fecham com scrim `rgba(0,0,0,0.5)`.

## Shapes

Cantos arredondados em três famílias: utilitários (`xs` 4 – `md` 8), cards
(`lg` 12 – `2xl` 16) e pills de ação (`pill` 18). Círculos (avatares, ícones
de status) usam `full`. Botões secundários usam `xl` (14px).

## Components

`button-primary` é a única ação de alta ênfase por tela — usada em "Iniciar
Backup", "Executar Todos os Testes" e "Resgatar agora". `button-secondary` é o
padrão para ações neutras ("Restaurar Arquivos", "Testar", "Acessar") e usa
texto `slate` sobre a superfície do card (sua cor de fundo real depende do
contexto). Abas da top bar acendem para `top-tab-active` (teal) e escurecem
para `top-tab-hover` sem perder legibilidade. `card-dark` cobre os cartões da
tela inicial; `card-light` as áreas de trabalho. O console de logs
(`console-view`) é a única superfície em que `muted` é o texto principal.

## Do's and Don'ts

**Do**

- Use referências de token (`{colors.primary}`) em vez de reescrever hex.
- Mantenha o texto em `{colors.white}` sempre que o fundo do hover/pressed for
  escuro — é o contraste 13:1+ que o app exibe hoje.
- Empregue `top-tab-active` apenas para a aba corrente; hover e ativo nunca se
  confundem (teal sólido vs. escurecido).

**Don't**

- Não use `muted` (#9FAEBF) nem `slate` (#6D86AB) como texto pequeno sobre
  superfícies claras: o contraste fica abaixo de 4.5:1 (débito herdado do app
  oficial). Para conteúdo em `surface`/`white`, use `ink` ou `deep`.
- Não introduza novas cores de ação: qualquer acento fora de `primary`,
  `primary-hover` e `primary-pressed` quebra a identidade v6.
- Não aplique sombras no chrome escuro nem bordas duras nos cards escuros.
