using System;
using System.Collections.Generic;
using System.Linq;

namespace Stintoria.Core
{
    /// <summary>Sem relógio, IO, engine gráfica, rede ou System.Random. Uma chamada = um setor.</summary>
    public sealed class RaceSimulation
    {
        private sealed class Entry
        {
            internal readonly Driver Driver;
            internal long Time;
            internal int Wear;
            internal Pace Pace = Pace.Equilibrado;
            internal Entry(Driver driver) { Driver = driver; }
        }
        private readonly Entry[] entries;
        private readonly Circuit circuit;
        private readonly SeededRandom random;
        private readonly uint seed;
        private readonly int laps;
        private readonly List<RaceEvent> events = new List<RaceEvent>();
        private int step;
        private Weather weather;

        public RaceSimulation(IReadOnlyList<Driver> drivers, IReadOnlyList<Team> teams,
            Circuit circuit, uint seed, int laps = KartRules.DefaultLaps, Weather initialWeather = Weather.Seco)
        {
            KartRules.ValidateGrid(drivers, teams);
            this.circuit = circuit ?? throw new ArgumentNullException(nameof(circuit));
            this.laps = Guard.Range(laps, 1, 100, nameof(laps));
            if (!Enum.IsDefined(typeof(Weather), initialWeather)) throw new ArgumentOutOfRangeException(nameof(initialWeather));
            this.seed = seed; weather = initialWeather; random = new SeededRandom(seed);
            // Ordem estável de consumo do RNG, independente da ordem dos dados de entrada.
            entries = drivers.OrderBy(x => x.Id, StringComparer.Ordinal).Select(x => new Entry(x)).ToArray();
        }

        public RaceState State => Snapshot();
        public uint RandomState => random.CurrentState;

        public RaceState Advance(IReadOnlyDictionary<string, Pace>? commands = null)
        {
            if (step == laps * 3) throw new InvalidOperationException("Corrida já encerrada.");
            // Validar o lote inteiro antes de qualquer mutação/consumo aleatório.
            if (commands != null)
                foreach (var command in commands)
                {
                    if (!entries.Any(x => x.Driver.Id == command.Key)) throw new ArgumentException("Piloto desconhecido.");
                    if (!Enum.IsDefined(typeof(Pace), command.Value)) throw new ArgumentException("Ritmo inválido.");
                }
            var before = Ranked().Select(x => x.Driver.Id).ToArray();
            if (step % 3 == 0)
            {
                var roll = random.Next(100);
                var next = weather == Weather.Seco
                    ? (roll < 15 ? Weather.Chuva : Weather.Seco)
                    : (roll < 30 ? Weather.Seco : Weather.Chuva);
                if (next != weather)
                {
                    weather = next;
                    events.Add(new RaceEvent(step + 1, "", "clima",
                        weather == Weather.Chuva ? "Começou a chover: aderência menor e setores mais lentos." :
                        "A pista secou: removida a penalidade de chuva."));
                }
            }
            foreach (var entry in entries)
            {
                if (commands != null && commands.TryGetValue(entry.Driver.Id, out var pace) && pace != entry.Pace)
                {
                    entry.Pace = pace;
                    events.Add(new RaceEvent(step + 1, entry.Driver.Id, "ritmo", "Ritmo alterado para " + pace + "."));
                }
                var paceDelta = entry.Pace == Pace.Conservar ? 350 : entry.Pace == Pace.Atacar ? -300 : 0;
                var wearDelta = entry.Pace == Pace.Conservar ? 1 : entry.Pace == Pace.Atacar ? 3 : 2;
                var rain = weather == Weather.Chuva ? 1800 + (100 - entry.Driver.Skill) * 12 : 0;
                var jitter = random.Next(401) - 200;
                var mistakeRoll = random.Next(1000);
                var risk = entry.Pace == Pace.Atacar ? 45 : entry.Pace == Pace.Conservar ? 5 : 15;
                if (weather == Weather.Chuva) risk += 25;
                var mistake = mistakeRoll < risk ? 1500 : 0;
                var tirePenalty = entry.Wear * 12;
                var duration = circuit.SectorBaseMs[step % 3] + (100 - entry.Driver.Skill) * 8 +
                    paceDelta + rain + jitter + mistake + tirePenalty;
                if (duration <= 0) throw new InvalidOperationException("Tempo de setor não positivo.");
                entry.Time = checked(entry.Time + duration);
                entry.Wear = Math.Min(100, entry.Wear + wearDelta);
                if (mistake != 0)
                    events.Add(new RaceEvent(step + 1, entry.Driver.Id, "erro",
                        "Erro de pilotagem: +1500 ms; risco do ritmo e da chuva."));
                events.Add(new RaceEvent(step + 1, entry.Driver.Id, "setor",
                    "Setor: " + duration + " ms; base " + circuit.SectorBaseMs[step % 3] +
                    ", habilidade +" + (100 - entry.Driver.Skill) * 8 + ", ritmo " + paceDelta +
                    ", pneus +" + tirePenalty + ", clima +" + rain + ", variação " + jitter +
                    ", erro +" + mistake + "."));
            }
            step++;
            var ranked = Ranked();
            if (step > 1)
                for (var i = 0; i < ranked.Length; i++)
                    if (Array.IndexOf(before, ranked[i].Driver.Id) > i)
                        events.Add(new RaceEvent(step, ranked[i].Driver.Id, "posicao",
                            "Subiu na classificação por tempo acumulado; não simula contato físico."));
            if (step == laps * 3)
                events.Add(new RaceEvent(step, "", "fim", "Bandeirada: classificação final e pontos atribuídos."));
            return Snapshot();
        }

        private Entry[] Ranked() => entries.OrderBy(x => x.Time)
            .ThenBy(x => x.Driver.Id, StringComparer.Ordinal).ToArray();

        private RaceState Snapshot() => new RaceState(seed, circuit.Id, step, laps, weather,
            Ranked().Select((x, i) => new DriverResult(x.Driver.Id, i + 1, x.Time, x.Wear,
                x.Pace, step == laps * 3 ? KartRules.PointsFor(i + 1) : 0)).ToArray(), events.ToArray());
    }
}
