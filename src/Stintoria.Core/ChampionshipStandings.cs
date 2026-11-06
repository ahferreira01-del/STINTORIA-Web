using System;
using System.Collections.Generic;
using System.Linq;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Stintoria.Core.Tests")]

namespace Stintoria.Core
{
    public sealed class ChampionshipStanding
    {
        public string Id { get; }
        public int Points { get; }
        public int Wins => Finishes[0];
        public IReadOnlyList<int> Finishes { get; }
        public IReadOnlyList<int> RecentPositions { get; }
        internal ChampionshipStanding(string id, int points, int[] finishes, int[] recentPositions)
        {
            Id = id; Points = points;
            Finishes = Array.AsReadOnly((int[])finishes.Clone());
            RecentPositions = Array.AsReadOnly((int[])recentPositions.Clone());
        }
    }

    internal static class ChampionshipStandings
    {
        // Pontos; mais 1ºs, 2ºs...20ºs; resultados mais recentes; ID ordinal.
        // Equipes: contam as posições dos dois pilotos; por etapa recente, melhor e pior posição.
        internal static int Compare(ChampionshipStanding a, ChampionshipStanding b)
        {
            var comparison = b.Points.CompareTo(a.Points);
            if (comparison != 0) return comparison;
            for (var i = 0; i < KartRules.GridSize; i++)
            {
                comparison = b.Finishes[i].CompareTo(a.Finishes[i]);
                if (comparison != 0) return comparison;
            }
            for (var i = 0; i < a.RecentPositions.Count; i++)
            {
                comparison = a.RecentPositions[i].CompareTo(b.RecentPositions[i]);
                if (comparison != 0) return comparison;
            }
            return StringComparer.Ordinal.Compare(a.Id, b.Id);
        }

        internal static IReadOnlyList<ChampionshipStanding> Calculate(IReadOnlyList<ChampionshipRound> rounds,
            bool teams, IReadOnlyList<Driver> drivers)
        {
            var ids = teams ? InitialData.Teams.Select(x => x.Id) : drivers.Select(x => x.Id);
            var rows = new List<ChampionshipStanding>();
            foreach (var id in ids)
            {
                var members = new HashSet<string>(teams
                    ? drivers.Where(d => d.TeamId == id).Select(d => d.Id)
                    : new[] { id }, StringComparer.Ordinal);
                var results = rounds.SelectMany(r => r.Result.Classification).Where(d => members.Contains(d.DriverId)).ToArray();
                var finishes = Enumerable.Range(1, KartRules.GridSize)
                    .Select(position => results.Count(r => r.Position == position)).ToArray();
                var recent = rounds.Reverse().SelectMany(r => r.Result.Classification
                    .Where(d => members.Contains(d.DriverId)).Select(d => d.Position).OrderBy(p => p)).ToArray();
                rows.Add(new ChampionshipStanding(id, results.Sum(r => r.Points), finishes, recent));
            }
            rows.Sort(Compare);
            return rows.AsReadOnly();
        }
    }
}
