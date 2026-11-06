using System;
using System.Collections.Generic;
using System.Linq;

namespace Stintoria.Core
{
    public enum CareerOrigin { FamiliaHumilde, LigadaAoEsporte, Empresaria, SimRacer }
    public enum Training { Descanso, Tecnico }

    public sealed class CareerProfile
    {
        public const string PlayerId = "jogador";
        public int Age => 14;
        public string Name { get; }
        public string Nationality { get; }
        public CareerOrigin Origin { get; }
        public string TeamId { get; }
        public string DisplacedDriverId { get; }
        public static IReadOnlyList<Team> EligibleTeams => InitialData.Teams;

        public CareerProfile(string name, string nationality, CareerOrigin origin, string teamId)
        {
            Name = Text(name, "Nome");
            Nationality = Text(nationality, "Nacionalidade");
            if (InitialData.Drivers.Any(d => string.Equals(d.Name, Name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Use um nome fictício diferente dos pilotos existentes.");
            if (!Enum.IsDefined(typeof(CareerOrigin), origin)) throw new ArgumentException("Origem inválida.");
            if (!EligibleTeams.Any(t => t.Id == teamId)) throw new ArgumentException("Equipe inicial inelegível.");
            Origin = origin; TeamId = teamId;
            DisplacedDriverId = InitialData.Drivers.First(d => d.TeamId == teamId).Id;
        }

        private static string Text(string value, string label)
        {
            if (value == null) throw new ArgumentException(label + " obrigatório.");
            var trimmed = value.Trim();
            if (trimmed.Length < 2 || trimmed.Length > 60 || !trimmed.Any(char.IsLetter) ||
                trimmed.Any(c => !char.IsLetter(c) && c != ' ' && c != '-' && c != '\'' && c != '’'))
                throw new ArgumentException(label + " deve ter 2 a 60 caracteres: letras, espaços, hífen ou apóstrofo.");
            return trimmed;
        }

        internal IReadOnlyList<Driver> Roster(int skill) => Array.AsReadOnly(InitialData.Drivers
            .Where(d => d.Id != DisplacedDriverId)
            .Concat(new[] { new Driver(PlayerId, Name, TeamId, skill) }).ToArray());
    }
}
