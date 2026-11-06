#!/usr/bin/env bash
# Gera somente assemblies gerenciados para importar em um editor Unity externo.
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet build src/Stintoria.ClientBridge/Stintoria.ClientBridge.csproj --configuration Release --nologo
destination=generated-artifacts/unity/Assets/Stintoria
mkdir -p "$destination/Plugins"
cp src/Stintoria.Core/bin/Release/netstandard2.1/Stintoria.Core.dll "$destination/Plugins/"
cp src/Stintoria.ClientBridge/bin/Release/netstandard2.1/Stintoria.ClientBridge.dll "$destination/Plugins/"
cp integrations/unity/link.xml "$destination/link.xml"
cp docs/integracao-unity.md "$destination/INTEGRACAO.md"
cp -R integrations/unity-client/Assets/Stintoria/Runtime "$destination/"
cp -R integrations/unity-client/Assets/Stintoria/Editor "$destination/"
cp -R integrations/unity-client/Assets/Stintoria/Plugins/WebGL "$destination/Plugins/"
cp docs/cliente-unity-fonte.md "$destination/CLIENTE-FONTE.md"
cp docs/migracao-terminal-unity.md "$destination/migracao-terminal-unity.md"
cp docs/unity-corrida-participativa.md "$destination/unity-corrida-participativa.md"
cp docs/persistencia-webgl.md "$destination/persistencia-webgl.md"
sha256sum "$destination"/Plugins/*.dll > generated-artifacts/verification/unity-assemblies.sha256
printf 'DLLs e fontes de cliente Unity preparados em %s. NÃO é cena gerada ou build Unity/Web.\n' "$destination"
