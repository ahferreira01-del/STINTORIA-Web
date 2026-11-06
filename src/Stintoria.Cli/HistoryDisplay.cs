using System.Globalization;
using Stintoria.Core;

internal static class HistoryDisplay
{
    internal static void Browse(ChampionshipHistory history)
    {
        Summary(history);
        var selected = 1;
        while (true)
        {
            Console.Write("Histórico (1-8, a anterior, p próxima, r resumo, e evolução, 0 voltar): ");
            var input = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (input == null || input == "0") return;
            if (input == "r") { Summary(history); continue; }
            if (input == "e") { Evolution(history); continue; }
            if (input == "t" || input.StartsWith("t ", StringComparison.Ordinal))
            {
                var id = input == "t" ? history.ProtagonistId : input.Substring(2).Trim();
                if (id == null) Console.WriteLine("Protagonista não registrado.");
                else {
                    try { SegmentReportDisplay.Show(SegmentReportQuery.ForRound(history.GetRound(selected), id)); }
                    catch (ArgumentException ex) { Console.WriteLine("Consulta rejeitada: " + ex.Message); }
                }
                continue;
            }
            var number = input == "a" ? selected - 1 : input == "p" ? selected + 1 :
                int.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : -1;
            if (number < 1 || number > history.TotalRounds)
            { Console.WriteLine("Etapa inválida. Escolha de 1 a 8; navegação limitada ao calendário."); continue; }
            selected = number;
            Round(history.GetRound(selected));
        }
    }

    private static string Mark(HistoryStanding row, bool team)
        => (row.IsPlayerAssociated ? team ? "[SUA EQUIPE] " : "[VOCÊ] " : "") + row.Name + $" ({row.Id})";
    private static string Status(HistoryRound round) => round.Status switch {
        HistoryRoundStatus.Concluida => "disputada", HistoryRoundStatus.Preparada => "preparada, ainda não disputada",
        _ => "não disputada"
    };

    private static void Summary(ChampionshipHistory history)
    {
        Console.WriteLine($"\nRESUMO DO CAMPEONATO — {history.CompletedRounds}/{history.TotalRounds} etapas disputadas");
        Console.WriteLine("Digite t para relatório observado por trechos da etapa selecionada; t p01 consulta um rival pelo ID.");
        foreach (var round in history.Rounds)
        {
            Console.Write($"{round.Number}. {round.CircuitName}: {Status(round)}");
            if (round.Status == HistoryRoundStatus.Concluida)
                Console.Write($" | Vencedor: {round.Classification[0].DriverName}");
            Console.WriteLine();
        }
        if (history.ProtagonistId == null)
            Console.WriteLine("Protagonista não registrado neste campeonato.");
        if (history.CompletedRounds == 0) Console.WriteLine("Nenhum resultado oficial; sem líder ou campeão.");
        else
        {
            Console.WriteLine((history.Finished ? "Campeões: " : "Líderes provisórios: ") +
                Mark(history.DriverStandings[0], false) + " / " + Mark(history.TeamStandings[0], true));
            Tables(history.DriverStandings, history.TeamStandings);
        }
    }

    private static void Round(HistoryRound round)
    {
        Console.WriteLine($"\nHISTÓRICO — ETAPA {round.Number}/8 — {round.CircuitName} — {Status(round)}");
        if (round.Preparation != null)
        {
            var p = round.Preparation;
            Console.WriteLine($"Preparação registrada: {(p.Training == Training.Tecnico ? "treino técnico" : "descanso")}" +
                $" | ritmo {p.Pace.ToString().ToLowerInvariant()} | habilidade {p.SkillApplied} | créditos restantes {p.CreditsRemaining}");
            if (p.Strategy != null) StrategyDisplay.Show(p.Strategy);
            foreach (var ai in p.Opponents)
                Console.WriteLine($"PLANO IA {ai.DriverId}: {ai.Strategy.Beginning}/{ai.Strategy.Middle}/{ai.Strategy.Final}" +
                    $" (setores 1/13/25). Decisão pré-corrida: {ai.Reason}");
        }
        else Console.WriteLine("Treinamento e preparação de carreira não registrados.");
        if (round.Status != HistoryRoundStatus.Concluida)
        { Console.WriteLine("Sem resultado, pontos ou acontecimentos de corrida registrados para esta etapa."); return; }
        Console.WriteLine("CLASSIFICAÇÃO FINAL DA CORRIDA — tempo | pontos");
        foreach (var row in round.Classification)
            Console.WriteLine($"{row.Position,2}. {(row.IsProtagonist ? "[VOCÊ] " : "")}{row.DriverName} ({row.DriverId})" +
                $" | {row.TeamName} | {row.ElapsedMs} ms | {row.Points} pontos");
        Tables(round.DriverStandings, round.TeamStandings);
        Console.WriteLine("DECISÕES REGISTRADAS");
        if (round.Decisions.Count == 0) Console.WriteLine("Nenhuma decisão explícita registrada; não se infere uma escolha.");
        foreach (var decision in round.Decisions)
        {
            var driver = round.Classification.Single(r => r.DriverId == decision.DriverId);
            Console.WriteLine($"Setor {decision.Sector}: {(driver.IsProtagonist ? "[VOCÊ] " : "")}{driver.DriverName}" +
                $" ({driver.DriverId}) — {decision.Pace.ToString().ToLowerInvariant()}");
        }
        Console.WriteLine($"ACONTECIMENTOS — {round.Events.Count} eventos registrados; destaques da prova e do protagonista:");
        if (round.Highlights.Count == 0) Console.WriteLine("Nenhum destaque registrado.");
        foreach (var e in round.Highlights) Console.WriteLine($"Setor {e.Step}: {e.Explanation}");
    }

    private static void Tables(IReadOnlyList<HistoryStanding> drivers, IReadOnlyList<HistoryStanding> teams)
    {
        foreach (var team in new[] { false, true })
        {
            Console.WriteLine($"PONTOS ACUMULADOS DE {(team ? "EQUIPES" : "PILOTOS")} — total | última etapa | vitórias");
            foreach (var row in team ? teams : drivers)
                Console.WriteLine($"{row.Position,2}. {Mark(row, team)} | {row.Points} | +{row.RoundPoints} | {row.Wins}");
        }
        Console.WriteLine("Desempate oficial: pontos; quantidade de 1ºs a 20ºs; posições mais recentes; ID.");
    }

    private static void Evolution(ChampionshipHistory history)
    {
        Console.WriteLine("EVOLUÇÃO DOS PONTOS — E1 | E2 | E3 | E4 | E5 | E6 | E7 | E8 (- = não disputada)");
        foreach (var team in new[] { false, true })
        {
            Console.WriteLine(team ? "EQUIPES" : "PILOTOS");
            foreach (var row in team ? history.TeamStandings : history.DriverStandings)
            {
                var columns = history.Rounds.Select(round => round.Status != HistoryRoundStatus.Concluida ? "-" :
                    (team ? round.TeamStandings : round.DriverStandings).Single(r => r.Id == row.Id)
                        .Points.ToString(CultureInfo.InvariantCulture));
                Console.WriteLine($"{Mark(row, team)} | {string.Join(" | ", columns)}");
            }
        }
    }
}
