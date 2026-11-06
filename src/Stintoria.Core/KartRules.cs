using System;
using System.Collections.Generic;
using System.Linq;

namespace Stintoria.Core
{
    /// <summary>Regulamento fictício do protótipo, não de uma federação real.</summary>
    public static class KartRules
    {
        public const int DefaultLaps = 12;
        public const int GridSize = 20;
        public const int DriversPerTeam = 2;
        private static readonly int[] points = { 25, 18, 15, 12, 10, 8, 6, 4, 2, 1 };
        public static int PointsFor(int position)
        {
            if (position < 1) throw new ArgumentOutOfRangeException(nameof(position));
            return position <= points.Length ? points[position - 1] : 0;
        }
        public static void ValidateGrid(IReadOnlyList<Driver> drivers, IReadOnlyList<Team> teams)
        {
            if (drivers == null || teams == null || drivers.Count != GridSize || teams.Count != 10)
                throw new ArgumentException("Kart exige 20 pilotos e 10 equipes.");
            if (drivers.Any(x => x == null) || teams.Any(x => x == null))
                throw new ArgumentException("Participante nulo.");
            if (drivers.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != drivers.Count ||
                teams.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != teams.Count)
                throw new ArgumentException("Identificadores duplicados.");
            var ids = new HashSet<string>(teams.Select(x => x.Id), StringComparer.Ordinal);
            if (drivers.Any(x => !ids.Contains(x.TeamId)) ||
                teams.Any(t => drivers.Count(d => d.TeamId == t.Id) != DriversPerTeam))
                throw new ArgumentException("Cada equipe deve ter dois pilotos; referências devem existir.");
        }
    }
}
