using System;
using System.Collections.Generic;
using System.Linq;

namespace Stintoria.Core
{
    /// <summary>Lê os eventos existentes. Não reexecuta corrida nem atribui causas contrafactuais.</summary>
    public static class SegmentReportQuery
    {
        public const string SupportedEventVersion = SectorEventReader.SupportedEventVersion;

        public static SegmentReport ForRound(HistoryRound round, string driverId,
            string eventVersion = SupportedEventVersion)
        {
            if (round == null) throw new ArgumentNullException(nameof(round));
            Guard.Text(driverId, nameof(driverId));
            if (round.Status != HistoryRoundStatus.Concluida)
                return new SegmentReport(round.Number, driverId, null, false, false, null,
                    Array.Empty<ObservedSegment>(), new[] { "Etapa não disputada: não há observações de corrida." });
            var driver = round.Classification.SingleOrDefault(d => d.DriverId == driverId)
                ?? throw new ArgumentException("Piloto não consta no resultado desta etapa.");
            var plan = driver.IsProtagonist ? round.Preparation?.Strategy :
                round.Preparation?.Opponents.SingleOrDefault(p => p.DriverId == driverId)?.Strategy;
            var warnings = new List<string>();
            if (plan == null) warnings.Add("Sem plano de três trechos registrado: divisão apenas analítica em 1..12 / 13..24 / 25..36.");
            if (eventVersion != SupportedEventVersion) warnings.Add("Versão de eventos não suportada; tempos e desgaste por setor indisponíveis.");
            var observations = new Dictionary<int, (long Time, int Wear, Pace Pace)>();
            for (var sector = 1; sector <= 36; sector++)
            {
                var recorded = round.Events.Where(e => e.DriverId == driverId && e.Code == "setor" && e.Step == sector).ToArray();
                if (recorded.Length == 1 && eventVersion == SupportedEventVersion &&
                    SectorEventReader.TryRead(recorded[0].Explanation, out var value))
                    observations.Add(sector, value);
                else warnings.Add("Há setores ausentes, duplicados ou em formato não reconhecido; valores indisponíveis não foram estimados.");
            }
            var sectors = new List<ObservedSector>();
            for (var sector = 1; sector <= 36; sector++)
            {
                var chosen = round.Decisions.Where(d => d.DriverId == driverId && d.Sector <= sector)
                    .OrderBy(d => d.Sector).LastOrDefault();
                var present = observations.TryGetValue(sector, out var value);
                int? after = observations.TryGetValue(sector + 1, out var next) ? next.Wear :
                    sector == 36 ? driver.TireWear : (int?)null;
                sectors.Add(new ObservedSector(sector, chosen?.Pace, present ? value.Pace : (Pace?)null,
                    present ? value.Time : (long?)null, present ? value.Wear : (int?)null, after));
            }
            if (observations.Count == 36 && observations.Values.Sum(v => v.Time) != driver.ElapsedMs)
                warnings.Add("Soma dos tempos observados diverge do resultado oficial; nenhuma correção foi aplicada.");
            var middle = plan?.MiddleStartSector ?? 13; var final = plan?.FinalStartSector ?? 25;
            ObservedSegment Part(string name, int first, int last) => new ObservedSegment(name, first, last,
                sectors.Where(s => s.Sector >= first && s.Sector <= last).ToArray(),
                round.Events.Where(e => e.Step >= first && e.Step <= last && e.Code != "setor" &&
                    (e.DriverId == "" || e.DriverId == driverId))
                    .Select(e => new RaceEvent(e.Step, e.DriverId, e.Code, e.Explanation)).ToArray());
            return new SegmentReport(round.Number, driverId, driver.DriverName, true, plan != null, driver.ElapsedMs,
                new[] { Part("Início", 1, middle - 1), Part("Meio", middle, final - 1), Part("Final", final, 36) }, warnings);
        }

    }
}
