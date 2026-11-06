using Stintoria.Core;

namespace Stintoria.Local;

/// <summary>Mesmo lock, flush e rename dos saves Kart; formato separado evita conversão silenciosa.</summary>
public static class CareerStore
{
    public static CareerSession Create(string path, uint seed, CareerProfile profile)
    {
        using var held = ChampionshipStore.Lock(path);
        if (File.Exists(path)) throw new IOException("Destino já existe; carreira não foi criada.");
        var career = new CareerSession(seed, profile);
        ChampionshipStore.WriteAtomic(path, CareerArchive.Encode(career), false);
        return career;
    }

    public static CareerSession Load(string path)
    {
        using var held = ChampionshipStore.Lock(path);
        return Read(path);
    }

    public static CareerSession Prepare(string path, int round, Training training, Pace pace)
        => Update(path, c => c.Prepare(round, training, pace));

    public static CareerSession PrepareStrategy(string path, int round, Training training, RaceStrategy strategy)
        => Update(path, c => c.PrepareStrategy(round, training, strategy));

    public static CareerSession PrepareTactical(string path, int round, Training training, RaceStrategy strategy)
        => Update(path, c => c.PrepareTactical(round, training, strategy));

    public static CareerSession RunRound(string path, int round) => Update(path, c => c.RunNext(round));
    public static CareerSession StartRace(string path, int round) => Update(path, c => c.StartRace(round));
    public static CareerSession AdvanceRace(string path, int round, int sector, int count = 1, Pace? pace = null)
        => Update(path, c => c.AdvanceRace(round, sector, count, pace));

    public static void Export(string source, string destination) => Copy(source, destination);
    public static CareerSession Import(string source, string destination) => Copy(source, destination);

    private static CareerSession Copy(string source, string destination)
    {
        var career = Load(source);
        using var held = ChampionshipStore.Lock(destination);
        if (File.Exists(destination)) throw new IOException("Destino já existe; nada foi sobrescrito.");
        ChampionshipStore.WriteAtomic(destination, CareerArchive.Encode(career), false);
        return career;
    }

    private static CareerSession Read(string path) => CareerArchive.Decode(ChampionshipStore.ReadText(path));

    private static CareerSession Update(string path, Action<CareerSession> action)
    {
        using var held = ChampionshipStore.Lock(path);
        var career = Read(path);
        action(career);
        ChampionshipStore.WriteAtomic(path, CareerArchive.Encode(career), true);
        return career;
    }
}
