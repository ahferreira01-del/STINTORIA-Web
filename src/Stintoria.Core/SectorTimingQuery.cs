using System;
using System.Collections.Generic;
using System.Linq;

namespace Stintoria.Core
{
    /// <summary>Um tempo de setor lido do evento esportivo que o motor já registrou.</summary>
    public sealed class RecordedSectorTime
    {
        public int UpdateSequence { get; }
        public int Lap { get; }
        public int Sector { get; }
        public long DurationMs { get; }
        public long ElapsedMs { get; }
        public Pace Pace { get; }

        internal RecordedSectorTime(int sequence, long durationMs, long elapsedMs, Pace pace)
        {
            UpdateSequence = sequence;
            Lap = (sequence - 1) / 3 + 1;
            Sector = (sequence - 1) % 3 + 1;
            DurationMs = durationMs;
            ElapsedMs = elapsedMs;
            Pace = pace;
        }
    }

    /// <summary>Histórico de tempos de um piloto; indisponível significa sem estimativas.</summary>
    public sealed class DriverSectorTimingHistory
    {
        public string DriverId { get; }
        public bool Available { get; }
        public string? Warning { get; }
        public IReadOnlyList<RecordedSectorTime> Sectors { get; }

        internal DriverSectorTimingHistory(string driverId, bool available, string? warning,
            RecordedSectorTime[] sectors)
        {
            DriverId = driverId;
            Available = available;
            Warning = warning;
            Sectors = Array.AsReadOnly(sectors);
        }
    }

    /// <summary>
    /// Consulta os eventos de setor existentes. Não simula, não lê a classificação como
    /// diferencial e só publica uma sequência completa cuja soma bate com o tempo oficial.
    /// </summary>
    public static class SectorTimingQuery
    {
        private const string UnsupportedVersionWarning =
            "Versão de eventos não suportada; tempos por setor indisponíveis.";
        private const string InvalidHistoryWarning =
            "Tempos por setor indisponíveis: registros ausentes, duplicados, inválidos ou divergentes do acumulado oficial.";

        public static IReadOnlyList<DriverSectorTimingHistory> ForState(
            RaceState state,
            IReadOnlyList<Driver> roster,
            string eventVersion = SegmentReportQuery.SupportedEventVersion)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (roster == null) throw new ArgumentNullException(nameof(roster));
            if (state.CompletedSectors < 0 || state.CompletedSectors > state.TotalLaps * 3)
                throw new ArgumentException("Progresso de setores inválido.", nameof(state));

            var driversById = new Dictionary<string, Driver>(StringComparer.Ordinal);
            foreach (var driver in roster)
                if (!driversById.TryAdd(driver.Id, driver))
                    throw new ArgumentException("Elenco com IDs de piloto duplicados.", nameof(roster));
            if (driversById.Count != 20 || state.Classification.Count != 20 ||
                state.Classification.Select(row => row.DriverId).Distinct(StringComparer.Ordinal).Count() != 20 ||
                state.Classification.Any(row => !driversById.ContainsKey(row.DriverId)))
                throw new ArgumentException("Histórico Kart incompatível com o elenco oficial de 20 pilotos.", nameof(state));

            var orderedDrivers = roster.OrderBy(driver => driver.Id, StringComparer.Ordinal).ToArray();
            var sectorEvents = state.Events.Where(item => item.Code == "setor").ToArray();
            var hasForeignOrFutureEvent = sectorEvents.Any(item =>
                !driversById.ContainsKey(item.DriverId) || item.Step < 1 || item.Step > state.CompletedSectors);
            var supportedVersion = eventVersion == SegmentReportQuery.SupportedEventVersion;
            var resultById = state.Classification.ToDictionary(row => row.DriverId, StringComparer.Ordinal);
            var histories = new List<DriverSectorTimingHistory>(orderedDrivers.Length);

            foreach (var driver in orderedDrivers)
            {
                if (!supportedVersion)
                {
                    histories.Add(Unavailable(driver.Id, UnsupportedVersionWarning));
                    continue;
                }
                if (hasForeignOrFutureEvent)
                {
                    histories.Add(Unavailable(driver.Id, InvalidHistoryWarning));
                    continue;
                }
                if (state.CompletedSectors == 0)
                {
                    histories.Add(new DriverSectorTimingHistory(driver.Id, true, null,
                        Array.Empty<RecordedSectorTime>()));
                    continue;
                }

                var recorded = sectorEvents.Where(item => item.DriverId == driver.Id)
                    .OrderBy(item => item.Step).ToArray();
                if (recorded.Length != state.CompletedSectors ||
                    recorded.GroupBy(item => item.Step).Any(group => group.Count() != 1) ||
                    !recorded.Select(item => item.Step).SequenceEqual(Enumerable.Range(1, state.CompletedSectors)))
                {
                    histories.Add(Unavailable(driver.Id, InvalidHistoryWarning));
                    continue;
                }

                var times = new List<RecordedSectorTime>(state.CompletedSectors);
                long accumulated = 0;
                var valid = true;
                foreach (var item in recorded)
                {
                    if (!SectorEventReader.TryRead(item.Explanation, out var parsed))
                    {
                        valid = false;
                        break;
                    }
                    try { accumulated = checked(accumulated + parsed.Time); }
                    catch (OverflowException) { valid = false; break; }
                    times.Add(new RecordedSectorTime(item.Step, parsed.Time, accumulated, parsed.Pace));
                }

                var official = resultById[driver.Id];
                if (!valid || accumulated != official.ElapsedMs || times[^1].Pace != official.Pace)
                {
                    histories.Add(Unavailable(driver.Id, InvalidHistoryWarning));
                    continue;
                }

                histories.Add(new DriverSectorTimingHistory(driver.Id, true, null, times.ToArray()));
            }
            return Array.AsReadOnly(histories.ToArray());
        }

        private static DriverSectorTimingHistory Unavailable(string driverId, string warning) =>
            new DriverSectorTimingHistory(driverId, false, warning, Array.Empty<RecordedSectorTime>());
    }
}
