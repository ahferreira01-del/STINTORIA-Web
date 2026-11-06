using Stintoria.Core;
using Stintoria.Local;

internal static class ParticipatoryCli
{
    internal static CareerSession Play(string path, CareerSession career)
    {
        if (career.GetActiveRace() == null) career = CareerStore.StartRace(path, career.NextRound);
        Console.WriteLine("CORRIDA PARTICIPATIVA — checkpoint entre setores; sem pontos antes da bandeirada.");
        Console.WriteLine("avancar 1|2|3 [conservar|equilibrado|atacar] | estado | sair");
        Console.WriteLine("Ritmo opcional aplica-se no PRÓXIMO setor e persiste até nova ordem ou início de outro trecho planejado." +
            "\nConservar: +350 ms/base, desgaste +1; equilibrado: 0, +2; atacar: -300 ms/base, +3." +
            "\nSão componentes do motor, não promessa de resultado. Nenhuma ação de box suportada nesta entrega.");
        while (career.GetActiveRace() is { } race)
        {
            Show(race.State);
            Console.Write("Corrida> ");
            var line = Console.ReadLine();
            if (line == null || line.Trim() == "sair") {
                Console.WriteLine("Corrida em andamento salva. Retome com opção 9."); return career;
            }
            if (line.Trim() == "estado") continue;
            var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts.Length > 3 || parts[0] != "avancar" ||
                !int.TryParse(parts[1], out var count) || count < 1 || count > 3) {
                Console.WriteLine("Entrada inválida: use avancar 1|2|3 [conservar|equilibrado|atacar]."); continue;
            }
            Pace? pace = null;
            if (parts.Length == 3) {
                pace = parts[2].ToLowerInvariant() switch {
                    "conservar" => Pace.Conservar, "equilibrado" => Pace.Equilibrado, "atacar" => Pace.Atacar, _ => null };
                if (!pace.HasValue) { Console.WriteLine("Ritmo inválido. Nenhum setor avançado."); continue; }
            }
            try {
                var before = race.State.CompletedSectors;
                career = CareerStore.AdvanceRace(path, career.NextRound, before + 1, count, pace);
                var state = career.GetActiveRace()?.State ?? career.Rounds.Last().Race.Result;
                foreach (var e in state.Events.Where(e => e.Step > before &&
                    (e.DriverId == CareerProfile.PlayerId || e.DriverId == "")))
                    Console.WriteLine($"Setor {e.Step}: {e.Explanation}");
                Console.WriteLine("Checkpoint salvo.");
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is IOException ||
                ex is UnauthorizedAccessException) {
                Console.WriteLine("Comando não confirmado: " + ex.Message + " Recarregando checkpoint.");
                career = CareerStore.Load(path);
            }
        }
        Console.WriteLine("Bandeirada: etapa concluída e pontos salvos uma única vez.");
        CareerDisplay.LastResult(career);
        CareerDisplay.Tables(career);
        return career;
    }

    private static void Show(RaceState state)
    {
        var player = state.Classification.Single(d => d.DriverId == CareerProfile.PlayerId);
        Console.WriteLine($"Percorridos {state.CompletedSectors}/36 setores | clima observado: {state.Weather}" +
            $" | posição por tempo: {player.Position} | pneus: {player.TireWear}/100 de desgaste | ritmo: {player.Pace}");
        Console.WriteLine("Tempos acumulados observados (não posições físicas):");
        foreach (var row in state.Classification)
            Console.WriteLine($"{row.Position}. {row.DriverId} | {row.ElapsedMs} ms" +
                (row.DriverId == CareerProfile.PlayerId ? " [VOCÊ]" : ""));
    }
}
