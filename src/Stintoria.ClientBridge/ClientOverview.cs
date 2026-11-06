using Stintoria.Core;

namespace Stintoria.ClientBridge
{
    public sealed class ClientOverview
    {
        public CareerProfile Profile { get; }
        public uint Seed { get; }
        public int NextRound { get; }
        public int Skill { get; }
        public int Credits { get; }
        public bool Finished { get; }
        public bool Prepared { get; }
        internal ClientOverview(CareerSession c)
        { Profile = c.Profile; Seed = c.Seed; NextRound = c.NextRound; Skill = c.Skill; Credits = c.TrainingCredits; Finished = c.Finished; Prepared = c.Pending != null; }
    }
}
