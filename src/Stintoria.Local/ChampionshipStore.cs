using System.Text;
using Stintoria.Core;

namespace Stintoria.Local;

/// <summary>IO fica fora do núcleo. Toda operação valida e todo avanço bloqueia ler→alterar→gravar.</summary>
public static class ChampionshipStore
{
    public static Championship Create(string path, uint seed)
    {
        using var held = Lock(path);
        if (File.Exists(path)) throw new IOException("Já existe um salvamento nesse destino; nada foi substituído.");
        var championship = new Championship(seed);
        WriteAtomic(path, ChampionshipArchive.Encode(championship), false);
        return championship;
    }

    public static Championship Load(string path)
    {
        using var held = Lock(path);
        return Read(path);
    }

    public static Championship RunRound(string path, int expectedRound, IEnumerable<SectorDecision> decisions)
    {
        using var held = Lock(path);
        var championship = Read(path);
        championship.RunNext(expectedRound, decisions);
        WriteAtomic(path, ChampionshipArchive.Encode(championship), true);
        return championship;
    }

    public static void Export(string source, string destination)
    {
        // Não mantém dois locks simultâneos: exporta um snapshot validado.
        var championship = Load(source);
        using var held = Lock(destination);
        if (File.Exists(destination)) throw new IOException("Destino já existe; exportação não sobrescreve arquivos.");
        WriteAtomic(destination, ChampionshipArchive.Encode(championship), false);
    }

    public static Championship Import(string source, string destination)
    {
        var championship = Load(source);
        using var held = Lock(destination);
        if (File.Exists(destination)) throw new IOException("Destino já existe; importação não sobrescreve campeonatos.");
        WriteAtomic(destination, ChampionshipArchive.Encode(championship), false);
        return championship;
    }

    internal static FileStream Lock(string path)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Arquivo de lock persiste; remover o arquivo após liberar permitiria corrida entre processos.
        try { return new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new IOException("Arquivo em uso ou indisponível. Tente novamente sem outra instância aberta."); }
    }

    private static Championship Read(string path) => ChampionshipArchive.Decode(ReadText(path));

    internal static string ReadText(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > ChampionshipArchive.MaxBytes) throw new InvalidDataException("Salvamento excede o limite de 8 MiB.");
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), false);
        return reader.ReadToEnd();
    }

    internal static void WriteAtomic(string path, string contents, bool overwrite)
    {
        path = Path.GetFullPath(path);
        var bytes = new UTF8Encoding(false).GetBytes(contents);
        if (bytes.Length > ChampionshipArchive.MaxBytes) throw new InvalidDataException("Salvamento excede o limite de 8 MiB.");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { file.Write(bytes); file.Flush(flushToDisk: true); }
            File.Move(temporary, path, overwrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
