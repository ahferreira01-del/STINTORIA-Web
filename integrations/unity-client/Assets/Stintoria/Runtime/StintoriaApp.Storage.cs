using System;
using Stintoria.ClientBridge;
using UnityEngine;

namespace Stintoria.UnityClient
{
    public sealed partial class StintoriaApp
    {
        private string exportText = "", importText = "", importSummary = "";
        private byte[] importBytes;
        private int importSlot;
        private bool confirmImport;
        private void Storage()
        {
            GUILayout.Label("CÓPIAS LOCAIS — sem backend ou conta");
            GUILayout.Label("O navegador pode bloquear/apagar dados, especialmente em modo privado. A/B não substitui uma cópia externa.");
            if (browserStore != null) {
                if (GUILayout.Button("Reler armazenamento e slots")) ActionAsync(async () => {
                    await browserStore.RefreshAsync(); RefreshSlots();
                    message = "Slots relidos; carregue o slot para sincronizar a carreira antes de avançar.";
                });
                if (browserStore.RecoveryCheckpoint != null && GUILayout.Button("Exportar proposta NÃO confirmada"))
                    ActionAsync(async () => {
                        var bytes = browserStore.RecoveryCheckpoint; exportText = Convert.ToBase64String(bytes);
                        await webStorage.DownloadAsync(bytes);
                        message = "Download da proposta não confirmada solicitado. Confira o arquivo; isto não confirma gravação no slot.";
                    });
                if (GUILayout.Button("Mostrar cópias brutas A/B do slot selecionado para recuperação"))
                    Action(() => {
                        exportText = "A: " + browserStore.ExportRawBankEncoded(importSlot + 1, true) +
                            "\nB: " + browserStore.ExportRawBankEncoded(importSlot + 1, false);
                        message = "Envelopes brutos, não checkpoints .stint. Preserve-os para diagnóstico; nenhum slot foi alterado.";
                    });
            }
            if (client.HasCareer && GUILayout.Button("Exportar checkpoint da sessão atual"))
                ActionAsync(async () => {
                    var bytes = client.ExportCheckpoint(); exportText = Convert.ToBase64String(bytes);
                    if (webStorage != null) await webStorage.DownloadAsync(bytes);
                    message = "Cópia disponibilizada abaixo" + (webStorage != null ? " e download solicitado" : "") +
                        ". Confira/guarde externamente; download concluído no disco não é verificável por esta UI.";
                });
            GUILayout.Label("Exportação Base64 — fallback manual: selecione/copie e guarde fora do jogo.");
            exportText = GUILayout.TextArea(exportText, 180000);
            if (browserStore != null && browserStore.RecoveryCheckpoint != null && GUILayout.Button("Mostrar proposta não confirmada em Base64"))
                exportText = Convert.ToBase64String(browserStore.RecoveryCheckpoint);
            GUILayout.Label("IMPORTAR .stint v1/v2 — apenas slot vazio; arquivo de origem nunca é modificado.");
            if (webStorage != null && GUILayout.Button("Selecionar arquivo .stint"))
                ActionAsync(async () => {
                    importBytes = null; importSummary = ""; confirmImport = false;
                    importText = Convert.ToBase64String(await webStorage.ChooseAsync());
                });
            GUILayout.Label("Ou cole o checkpoint em Base64:");
            var text = GUILayout.TextArea(importText, 88000);
            if (text != importText) { importText = text; importBytes = null; importSummary = ""; confirmImport = false; }
            var selected = GUILayout.SelectionGrid(importSlot, new[] { "Slot 1", "Slot 2", "Slot 3" }, 3);
            if (selected != importSlot) { importSlot = selected; confirmImport = false; }
            if (GUILayout.Button("Validar e pré-visualizar importação")) Action(() => {
                if (importText.Length > 88000) throw new ArgumentException("Texto acima do limite.");
                var bytes = Convert.FromBase64String(importText); var copy = CareerBridge.LoadCheckpoint(bytes);
                var overview = copy.ReadOverview(); var race = copy.ReadActiveRace();
                importBytes = bytes;
                importSummary = overview.Profile.Name + " [jogador], " + overview.Profile.Age + " anos | " +
                    overview.Profile.Nationality + " | equipe " + overview.Profile.TeamId +
                    " | seed " + overview.Seed + " | etapas " + copy.ReadHistory().CompletedRounds + "/8" +
                    (race == null ? " | sem prova ativa" : " | prova " + race.Round + ": " + race.State.CompletedSectors + "/36 setores");
                confirmImport = false;
            });
            if (importBytes != null) {
                GUILayout.Label(importSummary + " | DESTINO: slot " + (importSlot + 1));
                confirmImport = GUILayout.Toggle(confirmImport, "Confiro identidade/progresso/destino e confirmo importar para slot vazio");
                if (GUILayout.Button("Confirmar importação e aguardar gravação")) ActionAsync(async () => {
                    if (browserStore != null) await browserStore.RefreshAsync();
                    await client.ImportAsync(importSlot + 1, importBytes, confirmImport);
                    RefreshSlots(); Go(Page.Hub);
                });
            }
            if (GUILayout.Button("Menu inicial")) Go(Page.Home);
        }
    }
}
