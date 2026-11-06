using System;
using System.Linq;
using Stintoria.Core;
using UnityEngine;

namespace Stintoria.UnityClient
{
    public sealed partial class StintoriaApp
    {
        private int livePace, sectorBlock;
        private void ParticipatoryRace()
        {
            var race = client.ActiveRace();
            GUILayout.Label("CORRIDA PARTICIPATIVA — setores reais, sem calcular o futuro");
            if (race == null) {
                GUILayout.Label("Sem sessão ativa. Consulte o resultado salvo ou inicie uma etapa preparada.");
                if (GUILayout.Button("Central da carreira")) Go(Page.Hub);
                return;
            }
            var state = race.State;
            var circuit = InitialData.Circuits.Single(c => c.Id == state.CircuitId);
            var player = state.Classification.Single(d => d.DriverId == CareerProfile.PlayerId);
            GUILayout.Label(circuit.Name + " | etapa " + race.Round + " | percorridos " + state.CompletedSectors + "/36 setores" +
                " | voltas completas " + state.CompletedLaps + "/12");
            GUILayout.Label("Próximo: volta " + race.NextLap + ", setor " + race.NextCircuitSector +
                " da pista (global " + race.NextSector + ").");
            GUILayout.Label("[VOCÊ] posição por tempo " + player.Position + " | ritmo atual " + player.Pace +
                " | pneus: desgaste " + player.TireWear + "/100 | clima observado " + state.Weather);
            if (state.CompletedSectors == 0) GUILayout.Label("Largada: ainda não há tempos de setor; ordem inicial por ID, não resultado.");
            GUILayout.Label("CLASSIFICAÇÃO POR TEMPO ACUMULADO — não representa posição física");
            foreach (var row in state.Classification) {
                var driver = race.Drivers.Single(d => d.Id == row.DriverId);
                GUILayout.Label(row.Position + ". " + (row.DriverId == CareerProfile.PlayerId ? "[VOCÊ] " : "") +
                    driver.Name + " | " + row.ElapsedMs + " ms | desgaste " + row.TireWear + "/100");
            }
            GUILayout.Label("ORDEM PARA O PRÓXIMO SETOR");
            livePace = GUILayout.SelectionGrid(livePace,
                new[] {"Seguir ordens/plano", "Conservar", "Equilibrado", "Atacar"}, 2);
            GUILayout.Label("Conservar: +350 ms de ritmo e desgaste +1; equilibrado: 0 e +2; atacar: -300 ms e +3." +
                " Componentes do motor, não garantia de tempo final.");
            GUILayout.Label("Ordem explícita vale no próximo setor até nova ordem ou fronteira futura do plano." +
                " No mesmo setor, sua ordem vence o plano. Não há comando retroativo nem box nesta versão.");
            sectorBlock = GUILayout.SelectionGrid(sectorBlock, new[] {"1 setor", "2 setores", "3 setores"}, 3);
            var count = sectorBlock + 1;
            var oldEnabled = GUI.enabled;
            GUI.enabled = oldEnabled && state.CompletedSectors + count <= 36;
            if (GUILayout.Button("Confirmar ordem, avançar e salvar " + count + " setor(es)"))
                ActionAsync(async () => {
                    await client.AdvanceRaceAsync(race.Round, race.NextSector, count, livePace == 0 ? (Pace?)null : (Pace)(livePace - 1));
                    livePace = 0;
                    if (client.ActiveRace() == null) {
                        selectedRound = race.Round;
                        message = "Bandeirada: resultado e pontos salvos uma única vez.";
                        Go(Page.History);
                    } else message = "Setores e decisões confirmados no checkpoint. Você pode fechar e retomar.";
                });
            GUI.enabled = oldEnabled;
            if (state.CompletedSectors + count > 36) GUILayout.Label("Escolha um bloco menor: não pode ultrapassar o setor 36.");
            if (GUILayout.Button("Salvar sessão sem avançar")) ActionAsync(async () => {
                await client.SaveAsync(); message = "Gravação da sessão confirmada no slot " + client.ActiveSlot + ". Nenhum setor avançado.";
            });
            GUILayout.Label("DECISÕES AO VIVO JÁ APLICADAS");
            foreach (var decision in race.Decisions) GUILayout.Label("Setor " + decision.Sector + ": " + decision.Pace);
            GUILayout.Label("EVENTOS OBSERVADOS DO JOGADOR E CLIMA — sem previsões");
            foreach (var e in state.Events.Where(e => e.DriverId == CareerProfile.PlayerId || e.DriverId == ""))
                GUILayout.Label("Setor " + e.Step + ": " + e.Explanation);
            if (GUILayout.Button("Central da carreira — sessão já salva")) Go(Page.Hub);
            if (GUILayout.Button("Menu inicial — sessão já salva")) Go(Page.Home);
        }
    }
}
