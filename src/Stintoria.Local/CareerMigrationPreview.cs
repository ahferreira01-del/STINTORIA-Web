using Stintoria.Core;

namespace Stintoria.Local;

/// <summary>Prévia validada e imutável; não expõe o agregado mutável nem bytes compartilhados.</summary>
public sealed class CareerMigrationPreview
{
    public string SourcePath { get; }
    public string SourceSha256 { get; }
    public string SourceFormat { get; }
    public string SlotsDirectory { get; }
    public int Slot { get; }
    public string DestinationPath { get; }
    public bool DestinationOccupied { get; }
    public CareerProfile Profile { get; }
    public uint Seed { get; }
    public int CompletedRounds { get; }
    public int TotalRounds => 8;
    public bool Finished { get; }
    public bool HasPendingPreparation { get; }
    public int Skill { get; }
    public int Credits { get; }
    public string CareerVersion => CareerSession.Version;
    public string SimulationVersion => RaceState.SimulationVersion;
    public string RandomVersion => SeededRandom.Version;
    public string DataVersion => Championship.DataVersion;
    private readonly byte[] checkpoint;
    internal CareerMigrationPreview(string source, string hash, string format, string directory, int slot,
        string destination, bool occupied, CareerSession career, byte[] checkpoint)
    {
        SourcePath = source; SourceSha256 = hash; SourceFormat = format; SlotsDirectory = directory;
        Slot = slot; DestinationPath = destination; DestinationOccupied = occupied;
        Profile = career.Profile; Seed = career.Seed; CompletedRounds = career.Rounds.Count;
        Finished = career.Finished; HasPendingPreparation = career.Pending != null;
        Skill = career.Skill; Credits = career.TrainingCredits;
        this.checkpoint = (byte[])checkpoint.Clone();
    }
    internal byte[] CopyCheckpoint() => (byte[])checkpoint.Clone();
}
