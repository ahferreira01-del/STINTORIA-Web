using System.Globalization;
using Stintoria.Core;
using Stintoria.Local;

internal static class CareerMigrationCli
{
    internal static int Run(string source, string directory, string slotText)
    {
        if (!int.TryParse(slotText, NumberStyles.None, CultureInfo.InvariantCulture, out var slot) || slot < 1 || slot > 3)
            throw new ArgumentException("Slot deve ser 1, 2 ou 3.");
        var migration = new TerminalCareerMigration();
        var p = migration.Preview(source, directory, slot);
        Console.WriteLine("PRÉVIA — TERMINAL → CLIENTE UNITY DESKTOP");
        Console.WriteLine($"Origem somente-leitura: {p.SourcePath}\nSHA-256 original: {p.SourceSha256}");
        Console.WriteLine($"Piloto: {p.Profile.Name} | ID: {CareerProfile.PlayerId} | {p.Profile.Age} anos | {p.Profile.Nationality}");
        Console.WriteLine($"Origem do piloto: {p.Profile.Origin} | equipe: {p.Profile.TeamId} | vaga original: {p.Profile.DisplacedDriverId}");
        Console.WriteLine($"Temporada Kart: {p.CompletedRounds}/{p.TotalRounds} etapas concluídas; seed {p.Seed}.");
        Console.WriteLine("Ano/número de temporada não registrado pelo modelo atual; nenhum será inventado.");
        Console.WriteLine($"Campeonato encerrado: {(p.Finished ? "sim" : "não")} | preparação pendente: {(p.HasPendingPreparation ? "sim" : "não")}.");
        Console.WriteLine($"Habilidade: {p.Skill} | créditos: {p.Credits}");
        Console.WriteLine($"Formato original: {p.SourceFormat} | carreira {p.CareerVersion} | simulação {p.SimulationVersion}");
        Console.WriteLine($"RNG {p.RandomVersion} | dados {p.DataVersion} | destino: checkpoint STINTORIA-CLIENTE-1.");
        Console.WriteLine($"Destino exato: {p.DestinationPath}");
        Console.WriteLine("Esta pasta pertence à máquina do terminal. Não é um caminho remoto automático para o Unity.");
        if (p.DestinationOccupied) {
            Console.Error.WriteLine("Slot ocupado. Importação recusada; escolha outro slot vazio. Nenhum arquivo foi substituído.");
            return 1;
        }
        Console.WriteLine($"Slot {slot}: vazio na prévia. Conflitos serão verificados novamente na confirmação.");
        Console.Write($"Para confirmar, digite IMPORTAR {slot} (Enter/outro texto/EOF cancela): ");
        if (Console.ReadLine()?.Trim() != $"IMPORTAR {slot}") {
            Console.WriteLine("Importação cancelada. Origem preservada; nenhum slot foi gravado.");
            return 0;
        }
        try { migration.Confirm(p, true); }
        catch (IOException e) when (e is not FileNotFoundException && e is not DirectoryNotFoundException) {
            Console.Error.WriteLine("Falha de armazenamento: confira espaço/quota, permissões, bloqueio ou destino ocupado. " +
                "Nenhum sucesso foi confirmado; confira o slot antes de tentar novamente. O JSON original não foi alterado.");
            return 1;
        }
        Console.WriteLine($"Importação concluída no slot {slot}: {p.DestinationPath}");
        Console.WriteLine("JSON original preservado. No cliente, atualize a seleção de pilotos e carregue o slot.");
        return 0;
    }
}
