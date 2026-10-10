#!/usr/bin/env python3
"""Tradução PT-BR da interface (MainWindow.axaml) — substituições exatas e verificadas."""
import sys

P = 'src/SoftwareFix.UI/Views/MainWindow.axaml'
src = open(P, encoding='utf-8').read()
orig = src

pairs = [
    # Abas superiores
    ('Text="Rescue" FontWeight="Bold" FontSize="13"', 'Text="Resgate" FontWeight="Bold" FontSize="13"'),
    ('Text="Backup &amp; Restore"', 'Text="Backup e Restauração"'),
    ('Text="Hardware Test" FontSize="12"', 'Text="Teste de Hardware" FontSize="12"'),
    ('Text="Data Transfer" FontSize="12"', 'Text="Transferência de Dados" FontSize="12"'),
    ('Text="My Device" FontSize="12"', 'Text="Meu Aparelho" FontSize="12"'),
    ('Text="Toolbox" FontSize="12"', 'Text="Ferramentas" FontSize="12"'),
    ('Text="Support" FontSize="12"', 'Text="Suporte" FontSize="12"'),
    ('Text="My Device" FontSize="22"', 'Text="Meu Aparelho" FontSize="22"'),
    ('Text="Toolbox" FontSize="22"', 'Text="Ferramentas" FontSize="22"'),
    # Tela inicial (Welcome)
    ('Text="Welcome to Software Fix"', 'Text="Bem-vindo ao Software Fix"'),
    ('Text="The easy way to update the software on your phone"', 'Text="A forma fácil de atualizar o software do seu celular"'),
    ('Text="Input IMEI"', 'Text="Digitar IMEI"'),
    ('Text="Manually enter the IMEI for&#x0a;your device"', 'Text="Informe manualmente o IMEI&#x0a;do seu aparelho"'),
    ('Text="Recommended"', 'Text="Recomendado"'),
    ('Text="WiFi Connection"', 'Text="Conexão Wi-Fi"'),
    ('Text="Connect your phone using&#x0a;Wi-Fi to read the IMEI"', 'Text="Conecte o celular via&#x0a;Wi-Fi para ler o IMEI"'),
    ('Text="Fastboot Connection..."', 'Text="Conexão Fastboot"'),
    ('Text="Follow the step-by-step guide&#x0a;to connect your device using a&#x0a;USB cable to match firmware"',
     'Text="Siga o guia passo a passo para&#x0a;conectar o aparelho via cabo&#x0a;USB e localizar o firmware"'),
    ('Text="Model Selection"', 'Text="Seleção de Modelo"'),
    ('Text="Manually select your phone&#x0a;from the list of models"', 'Text="Escolha manualmente o seu&#x0a;celular na lista de modelos"'),
    ('Text="🏠 Home"', 'Text="🏠 Início"'),
    # Aparelho conectado
    ('Text="Warranty &amp; Services"', 'Text="Garantia e Serviços"'),
    ('Text="Start date :"', 'Text="Início :"'),
    ('Text="End date :"', 'Text="Término :"'),
    ('Text="Days remaining :"', 'Text="Dias restantes :"'),
    ("StringFormat='Model Name: {0}'", "StringFormat='Modelo: {0}'"),
    ("StringFormat='Sales Model: {0}'", "StringFormat='Modelo comercial: {0}'"),
    ("StringFormat='SN: {0}'", "StringFormat='Nº de série: {0}'"),
    ("StringFormat='S/N: {0}'", "StringFormat='Nº de série: {0}'"),
    ("StringFormat='Ro.carrier: {0}'", "StringFormat='Operadora: {0}'"),
    ("StringFormat='Android version: {0}'", "StringFormat='Versão do Android: {0}'"),
    ("StringFormat='Current version: {0}'", "StringFormat='Versão atual: {0}'"),
    ('Text="Target firmware:"', 'Text="Firmware de destino:"'),
    ("StringFormat='Firmware version: {0}'", "StringFormat='Versão do firmware: {0}'"),
    ('Text="Downloaded File:"', 'Text="Arquivo baixado:"'),
    ('<TextBlock Text="Download" VerticalAlignment="Center" />', '<TextBlock Text="Baixar" VerticalAlignment="Center" />'),
    ('<TextBlock Text="Rescue" HorizontalAlignment="Center" VerticalAlignment="Center" />',
     '<TextBlock Text="Resgatar" HorizontalAlignment="Center" VerticalAlignment="Center" />'),
    ('Text="Please note:"', 'Text="Observações importantes:"'),
    ('Text="1. Please check the downloaded firmware matches your device model."',
     'Text="1. Confira se o firmware baixado corresponde ao modelo do seu aparelho."'),
    ('Text="2. Device rescue is a risk-taking behavior, we recommend you backup user data firstly before rescue. (For Enterprise device, rescue will not remove device data)"',
     'Text="2. O resgate de software envolve riscos — recomendamos fazer backup dos dados antes. (Em aparelhos corporativos, o resgate não remove os dados.)"'),
    ('Text="3. The software update does not remove an existing Google account from the device for security reasons."',
     'Text="3. Por segurança, a atualização não remove uma conta Google já existente no aparelho."'),
    ('Text="← Back"', 'Text="← Voltar"'),
    # Resgate concluído
    ('Text="Rescue completed."', 'Text="Resgate concluído."'),
    ('Text="Congratulations! Your device has successfully completed the Rescue software update."',
     'Text="Parabéns! O seu aparelho concluiu com sucesso a atualização de software."'),
    # Menu lateral (Meu Aparelho)
    ('<TextBlock Text="Home" VerticalAlignment="Center" />', '<TextBlock Text="Início" VerticalAlignment="Center" />'),
    ('<TextBlock Text="Apps" VerticalAlignment="Center" />', '<TextBlock Text="Aplicativos" VerticalAlignment="Center" />'),
    ('<TextBlock Text="Pictures" VerticalAlignment="Center" />', '<TextBlock Text="Fotos" VerticalAlignment="Center" />'),
    ('<TextBlock Text="Music" VerticalAlignment="Center" />', '<TextBlock Text="Músicas" VerticalAlignment="Center" />'),
    ('<TextBlock Text="Videos" VerticalAlignment="Center" />', '<TextBlock Text="Vídeos" VerticalAlignment="Center" />'),
    ('<TextBlock Text="Contacts" VerticalAlignment="Center" />', '<TextBlock Text="Contatos" VerticalAlignment="Center" />'),
    ('<TextBlock Text="File" VerticalAlignment="Center" />', '<TextBlock Text="Arquivos" VerticalAlignment="Center" />'),
    ('<TextBlock Text="One-click clone" VerticalAlignment="Center" TextWrapping="Wrap" />',
     '<TextBlock Text="Clonagem com 1 clique" VerticalAlignment="Center" TextWrapping="Wrap" />'),
    # Transferência de dados (linhas)
    ('Text="Contacts" FontSize="13" Foreground="#334155" VerticalAlignment="Center" />',
     'Text="Contatos" FontSize="13" Foreground="#334155" VerticalAlignment="Center" />'),
    ('Text="Pictures" FontSize="13" Foreground="#334155" VerticalAlignment="Center" />',
     'Text="Fotos" FontSize="13" Foreground="#334155" VerticalAlignment="Center" />'),
    ('Text="Music" FontSize="13" Foreground="#334155" VerticalAlignment="Center" />',
     'Text="Músicas" FontSize="13" Foreground="#334155" VerticalAlignment="Center" />'),
    ('Text="Videos" FontSize="13" Foreground="#334155" VerticalAlignment="Center" />',
     'Text="Vídeos" FontSize="13" Foreground="#334155" VerticalAlignment="Center" />'),
    # Tabela de teste de hardware
    ('Text="Test item"', 'Text="Item de teste"'),
    ('Text="Result" FontSize="13"', 'Text="Resultado" FontSize="13"'),
]

fails = []
for old, new in pairs:
    n = src.count(old)
    if n == 0:
        fails.append(old)
        continue
    src = src.replace(old, new)

if fails:
    print('NÃO ENCONTRADAS (verificar manualmente):')
    for f in fails:
        print('  -', f)
    sys.exit(2)

open(P, 'w', encoding='utf-8').write(src)
print(f'OK: {len(pairs)} substituições aplicadas ({len(orig)} -> {len(src)} chars)')
