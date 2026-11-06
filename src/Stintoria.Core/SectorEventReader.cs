using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Stintoria.Core
{
    /// <summary>Leitor estrito do formato de evento versionado usado pelo motor Kart atual.</summary>
    internal static class SectorEventReader
    {
        internal const string SupportedEventVersion = "kart-0.1.0";

        private static readonly Regex Pattern = new Regex(
            @"\ASetor: (?<time>\d+) ms; base (?<base>\d+), habilidade \+(?<skill>\d+), ritmo (?<pace>-?\d+), pneus \+(?<tires>\d+), clima \+(?<rain>\d+), variação (?<jitter>-?\d+), erro \+(?<error>\d+)\.\z",
            RegexOptions.CultureInvariant);

        internal static bool TryRead(string text, out (long Time, int Wear, Pace Pace) value)
        {
            value = default;
            if (text == null) return false;
            var match = Pattern.Match(text);
            if (!match.Success) return false;
            var fields = new Dictionary<string, long>();
            foreach (var name in new[] { "time", "base", "skill", "pace", "tires", "rain", "jitter", "error" })
            {
                if (!long.TryParse(match.Groups[name].Value, NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out var number) || number < -10000000 || number > 10000000)
                    return false;
                fields.Add(name, number);
            }
            if (fields["time"] <= 0 || fields["tires"] % 12 != 0 || fields["tires"] > 1200 ||
                !new long[] { 350, 0, -300 }.Contains(fields["pace"]))
                return false;
            if (fields.Where(p => p.Key != "time").Sum(p => p.Value) != fields["time"])
                return false;
            value = (fields["time"], (int)(fields["tires"] / 12),
                fields["pace"] == 350 ? Pace.Conservar :
                    fields["pace"] == -300 ? Pace.Atacar : Pace.Equilibrado);
            return true;
        }
    }
}
