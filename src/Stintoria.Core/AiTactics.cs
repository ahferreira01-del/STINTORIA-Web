using System;

namespace Stintoria.Core
{
    public enum TacticalProfile { Prudente, Regular, Agressivo }

    public sealed class TacticalPlan
    {
        public string DriverId { get; }
        public TacticalProfile Profile { get; }
        public int ObservedSkill { get; }
        public Weather ObservedWeather { get; }
        public RaceStrategy Strategy { get; }
        public string Reason { get; }
        internal TacticalPlan(Driver driver, TacticalProfile profile, Weather weather, RaceStrategy strategy)
        {
            DriverId = driver.Id; Profile = profile; ObservedSkill = driver.Skill;
            ObservedWeather = weather; Strategy = strategy;
            Reason = "Perfil " + profile + " derivado da habilidade " + driver.Skill +
                "; condição inicial observada: " + weather + ". Plano pré-largada, sem previsão do clima futuro.";
        }
    }

    public static class AiTactics
    {
        public const string Version = "ia-tatica-inicial-v1";
        // Sem seed, relógio, resultados futuros ou referência ao jogador na entrada.
        public static TacticalPlan Choose(Driver driver, Weather observedWeather)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));
            if (!Enum.IsDefined(typeof(Weather), observedWeather)) throw new ArgumentException("Clima observado inválido.");
            if (driver.Id == CareerProfile.PlayerId) throw new ArgumentException("Política de rivais não escolhe pelo jogador.");
            var profile = driver.Skill < 74 ? TacticalProfile.Prudente :
                driver.Skill < 78 ? TacticalProfile.Regular : TacticalProfile.Agressivo;
            var strategy = observedWeather == Weather.Chuva || profile == TacticalProfile.Prudente
                ? new RaceStrategy(Pace.Conservar, Pace.Equilibrado, Pace.Equilibrado)
                : profile == TacticalProfile.Regular
                    ? new RaceStrategy(Pace.Equilibrado, Pace.Conservar, Pace.Atacar)
                    : new RaceStrategy(Pace.Atacar, Pace.Equilibrado, Pace.Conservar);
            return new TacticalPlan(driver, profile, observedWeather, strategy);
        }
    }
}
