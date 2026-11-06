using System;
using System.Collections.Generic;
using System.Linq;

namespace Stintoria.Core
{
    /// <summary>Dados fictícios próprios da Fórmula 4.</summary>
    public static class Formula4Data
    {
        public const string DataVersion = "f4-estrutura-v1";

        public static IReadOnlyList<Team> Teams { get; } = Array.AsReadOnly(new[]
        {
            new Team("f4t01", "Aster Fórmula"),
            new Team("f4t02", "Cobalto Motorsport"),
            new Team("f4t03", "Vértice Júnior"),
            new Team("f4t04", "Pontal Racing"),
            new Team("f4t05", "Serra Nova Fórmula"),
            new Team("f4t06", "Lumen Academy"),
            new Team("f4t07", "Pinhais Competition"),
            new Team("f4t08", "Orbe Racing"),
            new Team("f4t09", "Campo Alto Motorsport"),
            new Team("f4t10", "Nascente Fórmula")
        });

        /// <summary>Atributos propostos nesta etapa; não alegam equivalência a equipes reais.</summary>
        public static IReadOnlyList<Formula4CarSpec> CarSpecs { get; } = Array.AsReadOnly(new[]
        {
            new Formula4CarSpec("f4t01", 94, 94),
            new Formula4CarSpec("f4t02", 91, 97),
            new Formula4CarSpec("f4t03", 97, 87),
            new Formula4CarSpec("f4t04", 89, 98),
            new Formula4CarSpec("f4t05", 92, 91),
            new Formula4CarSpec("f4t06", 96, 89),
            new Formula4CarSpec("f4t07", 88, 99),
            new Formula4CarSpec("f4t08", 93, 92),
            new Formula4CarSpec("f4t09", 90, 96),
            new Formula4CarSpec("f4t10", 95, 88)
        });

        private static readonly string[] Names =
        {
            "Aline Furtado", "Bruno Meireles", "Catarina Nobre", "Diego Portela",
            "Elisa Noronha", "Felipe Sampaio", "Gabriela Tavares", "Heitor Paes",
            "Isadora Matos", "João Vilela", "Karen Pires", "Leonardo Mota",
            "Marina Queiroz", "Nicolas Farias", "Olívia Barreto", "Pedro Lins",
            "Rafaela Campos", "Samuel Freire", "Talita Neves", "Vitor Lobo"
        };

        public static IReadOnlyList<Driver> Drivers { get; } = Array.AsReadOnly(
            Names.Select((name, index) => new Driver(
                "f4p" + (index + 1).ToString("D2", System.Globalization.CultureInfo.InvariantCulture),
                name,
                Teams[index / 2].Id,
                65 + index % 13)).ToArray());

        private static readonly string[] RoundNames =
        {
            "Autódromo Horizonte", "Circuito das Águas", "Pista Vale Claro",
            "Autódromo Nova Aurora", "Circuito Pedra Branca", "Pista do Litoral",
            "Autódromo Serra Verde", "Circuito Campo Dourado",
            "Pista Porto Novo", "Autódromo Final da Serra"
        };

        public static IReadOnlyList<CareerCalendarEntry> Calendar { get; } = Array.AsReadOnly(
            RoundNames.Select((name, index) => new CareerCalendarEntry(
                index + 1, "f4-round-" + (index + 1).ToString("D2",
                    System.Globalization.CultureInfo.InvariantCulture), name)).ToArray());

        public static IReadOnlyList<Formula4Circuit> Circuits { get; } = Array.AsReadOnly(new[]
        {
            new Formula4Circuit("f4c01", "Autódromo Horizonte", 26400, 31800, 28700),
            new Formula4Circuit("f4c02", "Circuito das Águas", 30100, 27200, 32900),
            new Formula4Circuit("f4c03", "Pista Vale Claro", 28300, 33500, 27600),
            new Formula4Circuit("f4c04", "Autódromo Nova Aurora", 32200, 29100, 34400),
            new Formula4Circuit("f4c05", "Circuito Pedra Branca", 27800, 30600, 31800),
            new Formula4Circuit("f4c06", "Pista do Litoral", 33700, 28400, 30900),
            new Formula4Circuit("f4c07", "Autódromo Serra Verde", 29600, 34100, 28100),
            new Formula4Circuit("f4c08", "Circuito Campo Dourado", 31500, 27700, 32600),
            new Formula4Circuit("f4c09", "Pista Porto Novo", 28900, 32300, 30100),
            new Formula4Circuit("f4c10", "Autódromo Final da Serra", 33100, 29500, 34700)
        });

        internal static IReadOnlyList<Driver> Roster(string teamId, CareerProfile profile, int skill)
        {
            if (!Teams.Any(team => team.Id == teamId))
                throw new ArgumentException("Equipe de Fórmula 4 inelegível.", nameof(teamId));
            var displaced = Drivers.First(driver => driver.TeamId == teamId);
            return Array.AsReadOnly(Drivers.Where(driver => driver.Id != displaced.Id)
                .Concat(new[] { new Driver(CareerProfile.PlayerId, profile.Name, teamId, skill) })
                .ToArray());
        }
    }

    public sealed class CareerCalendarEntry
    {
        public int Number { get; }
        public string Id { get; }
        public string Name { get; }
        internal CareerCalendarEntry(int number, string id, string name)
        { Number = number; Id = id; Name = name; }
    }
}
