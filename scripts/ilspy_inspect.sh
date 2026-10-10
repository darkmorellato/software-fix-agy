#!/usr/bin/env bash
# Utilitário de Inspeção Rápida com ILSpy para Software Fix
export PATH=/home/dark/.dotnet:/home/dark/.dotnet/tools:/home/dark/.local/bin:$PATH
export DOTNET_ROOT=/home/dark/.dotnet

BASE_BIN="/run/media/dark/1224A54224A529A5/Program Files/Software Fix"

if [ -z "$1" ]; then
    echo "Uso: ./scripts/ilspy_inspect.sh <NomeDaClasseOuTipo> [NomeDoAssembly.dll]"
    echo "Exemplo 1: ./scripts/ilspy_inspect.sh lenovo.mbg.service.common.webservices.WebApiServices.WarrantyService lenovo.mbg.service.common.webservices.dll"
    echo "Exemplo 2: ./scripts/ilspy_inspect.sh --list lenovo.mbg.service.framework.smartdevice.dll"
    exit 1
fi

if [ "$1" == "--list" ]; then
    DLL="$2"
    echo "=== Listando classes em $DLL ==="
    ilspycmd -l c "$BASE_BIN/$DLL"
    exit 0
fi

TYPE="$1"
DLL="${2:-lenovo.mbg.service.common.webservices.dll}"

echo "=== Descompilando $TYPE de $DLL ==="
ilspycmd -t "$TYPE" "$BASE_BIN/$DLL"
