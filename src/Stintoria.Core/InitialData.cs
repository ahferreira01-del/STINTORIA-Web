using System;
using System.Collections.Generic;
using System.Linq;

namespace Stintoria.Core
{
    public static class InitialData
    {
        public static IReadOnlyList<Category> Categories { get; } = Array.AsReadOnly(new[] {
            new Category("kart", "Kart", 8, CareerLevel.Nacional, true),
            new Category("f4", "Fórmula 4", 10, CareerLevel.Nacional, true),
            new Category("regional", "Fórmula Regional", 12, CareerLevel.Continental),
            new Category("f3", "Fórmula 3", 14, CareerLevel.Internacional),
            new Category("f2", "Fórmula 2", 16, CareerLevel.Internacional),
            new Category("principal", "Principal", 20, CareerLevel.Mundial)
        });
        public static IReadOnlyList<Team> Teams { get; } = Array.AsReadOnly(new[] {
            new Team("t01", "Aurora Kart"), new Team("t02", "Escuderia Cedro"),
            new Team("t03", "Vértice Racing"), new Team("t04", "Litoral Motorsport"),
            new Team("t05", "Serra Dourada"), new Team("t06", "Cometa Kart"),
            new Team("t07", "Horizonte Racing"), new Team("t08", "Arco Azul"),
            new Team("t09", "Pedra Alta"), new Team("t10", "Vento Sul")
        });
        private static readonly string[] names = {
            "Caio Valença", "Lia Monteverde", "Ravi Silveira", "Nina Alencar",
            "Ícaro Falcão", "Maya Serrano", "Davi Castanheira", "Clara Venturi",
            "Noah Amaral", "Cecília Prado", "Theo Lacerda", "Luna Azevedo",
            "Enzo Seabra", "Alice Brum", "Bento Dourado", "Sofia Rangel",
            "Hugo Marins", "Eva Salgado", "Otávio Reis", "Helena Duarte"
        };
        public static IReadOnlyList<Driver> Drivers { get; } = Array.AsReadOnly(
            names.Select((name, i) => new Driver("p" + (i + 1).ToString("D2",
                System.Globalization.CultureInfo.InvariantCulture), name, Teams[i / 2].Id, 70 + i % 11)).ToArray());
        public static IReadOnlyList<Circuit> Circuits { get; } = Array.AsReadOnly(new[] {
            new Circuit("vale", "Kartódromo Vale do Cedro", 16000, 18500, 14000),
            new Circuit("porto", "Kartódromo Porto da Brisa", 14500, 17000, 16500),
            new Circuit("serra", "Kartódromo Serra de Âmbar", 19000, 15000, 17500)
        });
        public static IReadOnlyList<CalendarRound> KartCalendar { get; } = Array.AsReadOnly(
            new[] { "vale", "porto", "serra", "vale", "serra", "porto", "vale", "serra" }
                .Select((id, i) => new CalendarRound(i + 1, id)).ToArray());
    }
}
