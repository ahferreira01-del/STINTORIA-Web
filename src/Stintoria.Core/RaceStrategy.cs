using System;
using System.Collections.Generic;

namespace Stintoria.Core
{
    /// <summary>Três trechos contínuos, não vazios, nos 36 setores da corrida Kart atual.</summary>
    public sealed class RaceStrategy
    {
        public const string Version = "estrategia-tres-trechos-v1";
        public const int TotalSectors = KartRules.DefaultLaps * 3;
        public int MiddleStartSector { get; }
        public int FinalStartSector { get; }
        public Pace Beginning { get; }
        public Pace Middle { get; }
        public Pace Final { get; }

        public RaceStrategy(Pace beginning, Pace middle, Pace final,
            int middleStartSector = 13, int finalStartSector = 25)
        {
            if (middleStartSector < 2 || middleStartSector > TotalSectors - 1 ||
                finalStartSector <= middleStartSector || finalStartSector > TotalSectors)
                throw new ArgumentException("Trechos devem ser contínuos e não vazios: 1 < início do meio < início do final <= 36.");
            foreach (var pace in new[] { beginning, middle, final })
                if (!Enum.IsDefined(typeof(Pace), pace)) throw new ArgumentException("Ritmo inválido no plano.");
            Beginning = beginning; Middle = middle; Final = final;
            MiddleStartSector = middleStartSector; FinalStartSector = finalStartSector;
        }

        public Pace PaceAt(int sector)
        {
            if (sector < 1 || sector > TotalSectors)
                throw new ArgumentOutOfRangeException(nameof(sector), "Setor deve estar entre 1 e 36.");
            return sector < MiddleStartSector ? Beginning : sector < FinalStartSector ? Middle : Final;
        }

        /// <summary>Jogador e IA usam os mesmos comandos normais; sem modificadores especiais.</summary>
        public IReadOnlyList<SectorDecision> DecisionsFor(string driverId) => Array.AsReadOnly(new[] {
            new SectorDecision(1, driverId, Beginning),
            new SectorDecision(MiddleStartSector, driverId, Middle),
            new SectorDecision(FinalStartSector, driverId, Final)
        });
    }
}
