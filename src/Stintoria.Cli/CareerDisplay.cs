using Stintoria.Core;

internal static class CareerDisplay
{
    private static string Label(CareerSession career, string id)
    {
        var name = career.Drivers.Single(d => d.Id == id).Name;
        return id == CareerProfile.PlayerId ? "[VOCÊ] " + name + " (jogador)" : name;
    }

    internal static void Status(CareerSession career)
    {
        var p = career.Profile;
        Console.WriteLine($"\nCARREIRA — {p.Name} (jogador) | {p.Age} anos | {p.Nationality}");
        var origin = p.Origin switch {
            CareerOrigin.FamiliaHumilde => "família humilde", CareerOrigin.LigadaAoEsporte => "ligada ao esporte",
            CareerOrigin.Empresaria => "empresária", _ => "sim racer"
        };
        Console.WriteLine($"Origem: {origin} | Equipe: {InitialData.Teams.Single(t => t.Id == p.TeamId).Name}");
        Console.WriteLine($"Etapas: {career.Rounds.Count}/8 | Habilidade: {career.Skill} | Créditos de treino: {career.TrainingCredits}/4");
        if (career.GetActiveRace() is { } active)
            Console.WriteLine($"Corrida em andamento: {active.State.CompletedSectors}/36 setores salvos. Opção 9 para retomar.");
        if (career.Finished) Console.WriteLine("Campeonato Kart encerrado.");
        else Console.WriteLine($"Próxima etapa: {career.NextRound}. " +
            (career.Pending == null ? "Preparação pendente." :
                $"Preparação salva: {career.Pending.Training}; ritmo {career.Pending.Pace}."));
        if (career.Pending?.GetStrategy() is RaceStrategy pendingPlan) StrategyDisplay.Show(pendingPlan);
        if (career.Pending?.GetOpponentPlans().Count > 0) Console.WriteLine("IA tática: planos de 19 rivais já registrados.");
    }

    internal static void Tables(CareerSession career)
    {
        Console.WriteLine("CLASSIFICAÇÃO DE PILOTOS — pontos | vitórias");
        var position = 0;
        foreach (var row in career.DriverStandings)
            Console.WriteLine($"{++position,2}. {Label(career, row.Id),-40} {row.Points,3} | {row.Wins}");
        Console.WriteLine("CLASSIFICAÇÃO DE EQUIPES — pontos | vitórias dos pilotos");
        position = 0;
        foreach (var row in career.TeamStandings)
            Console.WriteLine($"{++position,2}. {(row.Id == career.Profile.TeamId ? "[SUA EQUIPE] " : "")}" +
                $"{InitialData.Teams.Single(t => t.Id == row.Id).Name} | {row.Points} | {row.Wins}");
        Console.WriteLine("Desempate: pontos; quantidade de 1ºs a 20ºs; posições mais recentes; ID.");
        if (career.Finished)
            Console.WriteLine("Campeões: " + Label(career, career.DriverStandings[0].Id) + " / " +
                InitialData.Teams.Single(t => t.Id == career.TeamStandings[0].Id).Name + ".");
    }

    internal static void LastResult(CareerSession career)
    {
        if (career.Rounds.Count == 0) { Console.WriteLine("Nenhuma etapa disputada."); return; }
        var round = career.Rounds.Last();
        var result = round.Race.Result;
        Console.WriteLine($"RESULTADO DA ETAPA {round.Race.Number}/8 — " +
            InitialData.Circuits.Single(c => c.Id == result.CircuitId).Name);
        Console.WriteLine($"Treino: {round.Preparation.Training} | Habilidade aplicada: {round.Preparation.SkillApplied}" +
            $" | Ritmo: {round.Preparation.Pace}");
        if (round.Preparation.GetStrategy() is RaceStrategy plan) StrategyDisplay.Show(plan);
        foreach (var row in result.Classification)
            Console.WriteLine($"{row.Position,2}. {Label(career, row.DriverId),-40} {row.ElapsedMs} ms | {row.Points} pontos");
        Console.WriteLine($"{result.Events.Count} eventos completos no save. Destaques do jogador e da prova:");
        foreach (var e in result.Events.Where(e => (e.DriverId == CareerProfile.PlayerId && e.Code != "setor") || e.DriverId == ""))
            Console.WriteLine($"Setor {e.Step}: {e.Explanation}");
    }
}
