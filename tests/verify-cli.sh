#!/usr/bin/env bash
# Execute da raiz: bash tests/verify-cli.sh (após dotnet build da CLI).
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
cli=(dotnet src/Stintoria.Cli/bin/Debug/net8.0/Stintoria.Cli.dll)
work="$(mktemp -d /tmp/stintoria-cli-XXXXXX)"
trap 'rm -rf "$work"' EXIT
fail() { echo "FALHA: $*" >&2; exit 1; }
rejected() {
  local code=0
  "${cli[@]}" "$@" > "$work/rejeicao.txt" 2>&1 || code=$?
  [[ "$code" == 1 ]] || fail "entrada inválida não retornou código 1"
  grep -q '^Erro:' "$work/rejeicao.txt" || fail "erro sem explicação em português"
}

"${cli[@]}" campeonato novo --arquivo "$work/continuo.json" --seed 42 > /dev/null
"${cli[@]}" campeonato novo --arquivo "$work/parcial.json" --seed 42 > /dev/null
for round in {1..8}; do
  "${cli[@]}" campeonato correr --arquivo "$work/continuo.json" --etapa "$round" \
    --comandos examples/comandos-kart.json > "$work/ultima.txt"
done
for round in {1..3}; do
  "${cli[@]}" campeonato correr --arquivo "$work/parcial.json" --etapa "$round" \
    --comandos examples/comandos-kart.json > /dev/null
done
cp "$work/parcial.json" "$work/antes.json"
rejected campeonato correr --arquivo "$work/parcial.json" --etapa 3
rejected campeonato correr --arquivo "$work/parcial.json" --etapa 5
rejected campeonato correr --arquivo "$work/parcial.json" --etapa 4 --ritmo turbo
cmp "$work/antes.json" "$work/parcial.json"
"${cli[@]}" campeonato exportar --arquivo "$work/parcial.json" --destino "$work/exportado.json"
"${cli[@]}" campeonato importar --origem "$work/exportado.json" --arquivo "$work/retomado.json" > /dev/null
cmp "$work/parcial.json" "$work/exportado.json"
cmp "$work/parcial.json" "$work/retomado.json"
rejected campeonato novo --arquivo "$work/parcial.json"
rejected campeonato importar --origem "$work/exportado.json" --arquivo "$work/parcial.json"
for round in {4..8}; do
  "${cli[@]}" campeonato correr --arquivo "$work/retomado.json" --etapa "$round" \
    --comandos examples/comandos-kart.json > /dev/null
done
cmp "$work/continuo.json" "$work/retomado.json"
cmp "$work/antes.json" "$work/parcial.json"
rejected campeonato correr --arquivo "$work/retomado.json" --etapa 9
"${cli[@]}" campeonato etapa --arquivo "$work/retomado.json" --numero 1 --eventos > "$work/eventos.txt"
grep -q '^Setor ' "$work/eventos.txt" || fail "eventos ausentes"
printf '{incompleto' > "$work/corrompido.json"
rejected campeonato importar --origem "$work/corrompido.json" --arquivo "$work/invalido.json"
[[ ! -e "$work/invalido.json" ]] || fail "importação corrompida criou destino"
rejected campeonato tabela --arquivo "$work/ausente.json"
rejected campeonato correr --arquivo "$work/parcial.json" --etapa
"${cli[@]}" campeonato tabela --arquivo "$work/retomado.json"
echo
echo "SHA-256 do campeonato contínuo e do retomado:"
sha256sum "$work/continuo.json" "$work/retomado.json"
echo "Regressão da corrida avulsa (seed 42 e roteiro original):"
hash="$("${cli[@]}" --seed 42 --comandos examples/comandos-kart.json --json | sha256sum | cut -d ' ' -f 1)"
[[ "$hash" == "6a85a54871317d566edba3a11def654860581ca61d39d46b04c128bcb4e90305" ]] ||
  fail "a saída original da corrida foi alterada"
echo "$hash"
echo "OK: CLI completa, retomada/exportação/importação, rejeições, eventos e regressão original."
