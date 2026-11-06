using System;
using System.Linq;
using Stintoria.Core;
using UnityEngine;

namespace Stintoria.UnityClient
{
    public sealed partial class StintoriaApp
    {
        private void Review()
        {
            GUILayout.Label("REVISÃO DA CORRIDA JÁ CALCULADA E SALVA — não é corrida ao vivo");
            var frame = client.Review(selectedRound, reviewSector);
            GUILayout.Label("Etapa " + selectedRound + " | setor global " + reviewSector + "/36");
            GUILayout.Label("MAPA ESTRATÉGICO ESQUEMÁTICO — fases, não posição física na pista");
            var plan = client.History().GetRound(selectedRound).Preparation?.Strategy;
            var middle = plan?.MiddleStartSector ?? 13;
            var finalStartSector = plan?.FinalStartSector ?? 25;
            var current = reviewSector < middle ? 0 : reviewSector < finalStartSector ? 1 : 2;
            var phases = new[] {"INÍCIO 1–" + (middle - 1), "MEIO " + middle + "–" + (finalStartSector - 1), "FINAL " + finalStartSector + "–36"};
            GUILayout.BeginHorizontal();
            for (var i = 0; i < 3; i++) {
                GUI.contentColor = current == i ? Gold : Color.white;
                GUILayout.Label(phases[i], GUI.skin.box);
            }
            GUI.contentColor = Color.white; GUILayout.EndHorizontal();
            GUILayout.Label(plan == null ? "Sem plano legado registrado: mapa em terços analíticos." : "Limites do plano registrado do jogador.");
            if (!frame.CompleteObservations) GUILayout.Label("Observações incompletas: posições não foram estimadas.");
            foreach (var row in frame.Drivers)
                GUILayout.Label((row.Position?.ToString() ?? "?") + ". " + (row.IsProtagonist ? "[VOCÊ] " : "") + row.Name +
                    " | " + (row.ElapsedMs?.ToString() ?? "indisponível") + " ms | pneus " + (row.Wear?.ToString() ?? "?") + " | " + row.Pace);
            GUILayout.BeginHorizontal();
            if (reviewSector > 1 && GUILayout.Button("Setor anterior")) reviewSector--;
            if (reviewSector < 36 && GUILayout.Button("Próximo setor")) reviewSector++;
            if (GUILayout.Button("Bandeirada registrada")) reviewSector = 36;
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Resultado e histórico")) Go(Page.History);
            if (GUILayout.Button("Central da carreira")) Go(Page.Hub);
        }
        private void Tables()
        {
            var h = client.History();
            GUILayout.Label(h.Finished ? "CLASSIFICAÇÃO FINAL DO CAMPEONATO" : "CLASSIFICAÇÃO PROVISÓRIA");
            Table("PILOTOS", h.DriverStandings);
            Table("EQUIPES", h.TeamStandings);
            if (h.Finished) GUILayout.Label("Campeões: " + h.DriverStandings[0].Name + " / " + h.TeamStandings[0].Name);
            GUILayout.Label("Desempates oficiais do núcleo: pontos, posições de chegada, resultados recentes e ID.");
            if (GUILayout.Button("Central da carreira")) Go(Page.Hub);
        }
        private static void Table(string title, System.Collections.Generic.IReadOnlyList<HistoryStanding> rows)
        {
            GUILayout.Label(title);
            foreach (var row in rows)
                GUILayout.Label(row.Position + ". " + (row.IsPlayerAssociated ? "[SUA CARREIRA] " : "") + row.Name +
                    " | " + row.Points + " pontos | " + row.Wins + " vitórias");
        }
        private void History()
        {
            var h = client.History();
            GUILayout.Label("RESULTADOS E HISTÓRICO — etapa selecionada " + selectedRound);
            GUILayout.BeginHorizontal();
            for (var i = 1; i <= 8; i++) if (GUILayout.Button(i.ToString())) selectedRound = i;
            GUILayout.EndHorizontal();
            var r = h.GetRound(selectedRound);
            GUILayout.Label(r.CircuitName + " — " + r.Status);
            if (r.Preparation?.Strategy != null) {
                var p = r.Preparation.Strategy;
                GUILayout.Label("Plano registrado: 1.." + (p.MiddleStartSector - 1) + " " + p.Beginning + " / " +
                    p.MiddleStartSector + ".." + (p.FinalStartSector - 1) + " " + p.Middle + " / " + p.FinalStartSector + "..36 " + p.Final);
            }
            if (r.Status != HistoryRoundStatus.Concluida) GUILayout.Label("Nenhum resultado de corrida registrado nesta etapa.");
            else {
                foreach (var row in r.Classification)
                    GUILayout.Label(row.Position + ". " + (row.IsProtagonist ? "[VOCÊ] " : "") + row.DriverName +
                        " | " + row.TeamName + " | " + row.ElapsedMs + " ms | +" + row.Points + " pontos");
                Table("PILOTOS APÓS ESTA ETAPA", r.DriverStandings);
                Table("EQUIPES APÓS ESTA ETAPA", r.TeamStandings);
                GUILayout.Label("DECISÕES REGISTRADAS");
                foreach (var decision in r.Decisions)
                    GUILayout.Label("Setor " + decision.Sector + ": " + decision.DriverId + " → " + decision.Pace);
                foreach (var ai in r.Preparation?.Opponents ?? Array.Empty<TacticalPlan>())
                    GUILayout.Label(ai.DriverId + ": " + ai.Reason);
                GUILayout.Label("ACONTECIMENTOS REGISTRADOS DO JOGADOR E DA PROVA");
                foreach (var e in r.Highlights) GUILayout.Label("Setor " + e.Step + ": " + e.Explanation);
                if (GUILayout.Button("Rever pelos tempos registrados")) { reviewSector = 1; Go(Page.Review); }
                if (GUILayout.Button("Resumo observado por trechos")) Action(() => {
                    var report = client.Report(selectedRound, "jogador");
                    message = string.Join("\n", report.Segments.Select(s => s.Name + " " + s.FirstSector + ".." + s.LastSector +
                        ": " + (s.DurationMs?.ToString() ?? "indisponível") + " ms; desgaste " + s.WearBefore + " → " + s.WearAfter)
                        .Concat(report.Warnings));
                });
            }
            if (GUILayout.Button("Central da carreira")) Go(Page.Hub);
        }
    }
}
