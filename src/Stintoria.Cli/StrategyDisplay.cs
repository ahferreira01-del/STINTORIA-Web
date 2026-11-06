using Stintoria.Core;

internal static class StrategyDisplay
{
    internal static void Show(RaceStrategy plan)
    {
        Console.WriteLine("PLANO DE TRÊS TRECHOS — comandos aplicados antes do cálculo do setor indicado:");
        Segment("Início", 1, plan.MiddleStartSector - 1, plan.Beginning);
        Segment("Meio", plan.MiddleStartSector, plan.FinalStartSector - 1, plan.Middle);
        Segment("Final", plan.FinalStartSector, RaceStrategy.TotalSectors, plan.Final);
        Console.WriteLine("Previsão qualitativa: chuva, habilidade, pneus acumulados e erros também afetam o resultado." +
            " Atacar não garante ganho total ou vitória. Mesmas regras para jogador e rivais.");
    }

    private static void Segment(string name, int first, int last, Pace pace)
    {
        var effect = pace switch {
            Pace.Conservar => "mais lento no termo de ritmo; menor desgaste e risco de erro",
            Pace.Atacar => "mais rápido no termo de ritmo; maior desgaste e risco de erro",
            _ => "ritmo, desgaste e risco intermediários"
        };
        Console.WriteLine($"{name}: setores {first}..{last} — {pace.ToString().ToUpperInvariant()}: {effect}.");
    }
}
