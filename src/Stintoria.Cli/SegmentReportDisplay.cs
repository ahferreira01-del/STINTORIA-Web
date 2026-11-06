using Stintoria.Core;

internal static class SegmentReportDisplay
{
    internal static void Show(SegmentReport report)
    {
        Console.WriteLine($"RELATÓRIO OBSERVADO POR TRECHOS — etapa {report.Round} — {report.DriverName ?? report.DriverId}");
        foreach (var warning in report.Warnings) Console.WriteLine("Aviso: " + warning);
        foreach (var part in report.Segments)
        {
            Console.WriteLine($"{part.Name}: setores {part.FirstSector}..{part.LastSector} | tempo {Value(part.DurationMs)} ms" +
                $" | desgaste {Value(part.WearBefore)} → {Value(part.WearAfter)}");
            foreach (var sector in part.Sectors)
                Console.WriteLine($"  Setor {sector.Sector} (volta {sector.Lap}/{sector.CircuitSector}): {Value(sector.DurationMs)} ms" +
                    $" | escolhido {sector.ChosenPace?.ToString() ?? "não registrado"} | observado {sector.ObservedPace?.ToString() ?? "indisponível"}" +
                    $" | desgaste {Value(sector.WearBefore)} → {Value(sector.WearAfter)}");
            foreach (var e in part.Events) Console.WriteLine($"  Evento no setor {e.Step}: {e.Explanation}");
        }
        Console.WriteLine("Desgaste após setor: observado no início do seguinte ou no resultado final." +
            " Sem estimar tempo alternativo, vantagem do plano ou causas não registradas.");
    }
    private static string Value<T>(T? number) where T : struct => number?.ToString() ?? "indisponível";
}
