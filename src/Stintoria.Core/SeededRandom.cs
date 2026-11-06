using System;

namespace Stintoria.Core
{
    /// <summary>Algoritmo e mapeamento congelados para replays v1. Seed zero é válida.</summary>
    public sealed class SeededRandom
    {
        public const string Version = "lcg32-v1";
        private uint state;
        public SeededRandom(uint seed) { state = seed; }
        public uint CurrentState => state;
        public uint NextUInt()
        {
            state = unchecked(state * 1664525u + 1013904223u);
            return state;
        }
        public int Next(int exclusiveMax)
        {
            if (exclusiveMax <= 0) throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
            // Redução por módulo é intencional em v1 (pequeno viés, não criptográfico).
            return (int)(NextUInt() % (uint)exclusiveMax);
        }
    }
}
