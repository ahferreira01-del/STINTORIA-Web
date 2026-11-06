using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Stintoria.ClientBridge;
using Stintoria.Core;
using UnityEngine;

namespace Stintoria.UnityClient
{
    /// <summary>Fonte Unity IMGUI. Nenhum resultado é calculado pela UI.</summary>
    public sealed partial class StintoriaApp : MonoBehaviour
    {
        private enum Page { Home, Create, Select, Hub, Prepare, Review, Tables, History, Migration, Race, Storage }
        private KartClientController client;
        private Page page;
        private string message = "";
        private string slotsDirectory = "";
        private Vector2 scroll;
        private int selectedRound = 1, reviewSector = 1;
        private string pilotName = "", nationality = "Brasileira", seed = "42";
        private int origin, team, slot;
        private bool confirmVacancy, tactical = true;
        private int training, firstPace, middlePace = 1, lastPace = 2;
        private string middleStart = "13", finalStart = "25";
        private RaceStrategy draft;
        private string[] slotDescriptions = new string[3];
        private bool[] validSlots = new bool[3];
        private static readonly Color Graphite = new Color(0.075f, 0.09f, 0.115f);
        private static readonly Color Navy = new Color(0.10f, 0.16f, 0.25f);
        private static readonly Color Gold = new Color(0.82f, 0.69f, 0.39f);

        private bool busy;
        private BrowserCheckpointStore browserStore;
        private StintoriaWebStorage webStorage;
        private StintoriaKartScene kartScene;
        private async void Awake()
        {
            kartScene = GetComponent<StintoriaKartScene>();
            busy = true;
            try {
#if UNITY_WEBGL && !UNITY_EDITOR
                var receiver = new GameObject("STINTORIA_Save_" + Guid.NewGuid().ToString("N"));
                receiver.transform.SetParent(transform);
                webStorage = receiver.AddComponent<StintoriaWebStorage>();
                browserStore = new BrowserCheckpointStore(webStorage);
                client = new KartClientController(browserStore);
                slotsDirectory = "Navegador: IndexedDB local por origem, sem pasta desktop.";
                await browserStore.RefreshAsync();
#else
                slotsDirectory = Path.Combine(Application.persistentDataPath, "STINTORIA-Kart");
                client = new KartClientController(new DesktopCheckpointStore(slotsDirectory));
#endif
                RefreshSlots();
            }
            catch (Exception e) { message = "Inicialização interrompida: " + FriendlyError(e); }
            finally { busy = false; RefreshSceneFromEngine(); }
        }
        private void OnGUI()
        {
            var oldColor = GUI.color; var oldBackground = GUI.backgroundColor; var oldContent = GUI.contentColor;
            GUI.color = new Color(Graphite.r, Graphite.g, Graphite.b, 0.12f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            var sidebarWidth = Mathf.Min(560f, Mathf.Max(280f, Screen.width * 0.44f));
            GUI.color = new Color(Graphite.r, Graphite.g, Graphite.b, 0.96f);
            GUI.DrawTexture(new Rect(0, 0, Mathf.Min(Screen.width, sidebarWidth + 32), Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white; GUI.backgroundColor = Navy; GUI.contentColor = Color.white;
            GUILayout.BeginArea(new Rect(16, 16, sidebarWidth, Mathf.Max(240, Screen.height - 32)));
            scroll = GUILayout.BeginScrollView(scroll);
            GUI.contentColor = Gold;
            GUILayout.Label("STINTORIA 0.1 — KART", new GUIStyle(GUI.skin.label) { fontSize = 25 });
            GUI.contentColor = Color.white;
            GUILayout.Label("Fonte Unity editável • PT-BR • Motorsport Prestige • sem arte 3D final");
            if (kartScene != null)
            {
                GUILayout.Label("Maquete gratuita estática • não representa posições físicas.");
                if (GUILayout.Button("Alternar câmera • " + kartScene.CameraLabel)) kartScene.ToggleCamera();
            }
            if (message.Length != 0) GUILayout.Label(message);
            if (browserStore != null) GUILayout.Label(browserStore.Message);
            if (busy) GUILayout.Label("Operação pendente: aguarde confirmação. Não feche a aba.");
            var wasEnabled = GUI.enabled; GUI.enabled = wasEnabled && !busy;
            if (client == null) GUILayout.Label("Cliente indisponível. Não foi criada uma carreira substituta.");
            else {
                switch (page) {
                    case Page.Home: Home(); break;
                    case Page.Create: Creation(); break;
                    case Page.Select: Selection(); break;
                    case Page.Hub: Hub(); break;
                    case Page.Prepare: Preparation(); break;
                    case Page.Review: Review(); break;
                    case Page.Race: ParticipatoryRace(); break;
                    case Page.Tables: Tables(); break;
                    case Page.History: History(); break;
                    case Page.Migration: Migration(); break;
                    case Page.Storage: Storage(); break;
                }
            }
            GUI.enabled = wasEnabled;
            GUILayout.EndScrollView(); GUILayout.EndArea();
            GUI.color = oldColor; GUI.backgroundColor = oldBackground; GUI.contentColor = oldContent;
        }
        internal void RefreshSceneFromEngine()
        {
            if (kartScene == null || !kartScene.IsInitialized) return;
            try
            {
                if (client == null || !client.HasCareer)
                    kartScene.SetEngineSnapshot(InitialData.Drivers, "", null);
                else
                    kartScene.SetEngineSnapshot(client.DriverRoster(), CareerProfile.PlayerId, client.ActiveRace());
            }
            catch (Exception e)
            {
                kartScene.ShowSnapshotUnavailable("snapshot indisponível; nenhum dado estimado.");
                Debug.LogWarning("STINTORIA: leitura da maquete foi suspensa sem alterar o estado oficial. " + e);
            }
        }
        private void Go(Page next) { page = next; message = ""; scroll = Vector2.zero; }
        private void Action(Action action)
        {
            try { action(); RefreshSceneFromEngine(); }
            catch (Exception e) { message = "Ação não concluída: " + FriendlyError(e) + " Não houve avanço confirmado na carreira."; }
        }
        private async void ActionAsync(Func<Task> action)
        {
            if (busy) return;
            busy = true;
            try { await action(); RefreshSceneFromEngine(); }
            catch (Exception e) { message = "Operação não confirmada: " + FriendlyError(e); }
            finally { busy = false; }
        }
        private static string FriendlyError(Exception e)
        {
            if (e is FormatException || e is OverflowException) return "Informe números inteiros dentro dos limites indicados.";
            if (e is BrowserStorageException) return e.Message;
            if (e is UnauthorizedAccessException) return "Sem permissão para acessar o diretório de salvamento.";
            if (e is FileNotFoundException || e is DirectoryNotFoundException) return "Arquivo de salvamento não encontrado.";
            if (e is InvalidDataException) return "Save corrompido ou incompatível. O arquivo foi preservado.";
            if (e is IOException) return "Falha no armazenamento, slot ocupado ou sessão desatualizada. Verifique os arquivos e recarregue.";
            if (e is ArgumentOutOfRangeException) return "Valor fora dos limites indicados.";
            if (e is ArgumentException || e is InvalidOperationException || e is NotSupportedException) return e.Message;
            return "Erro inesperado; operação interrompida. Consulte a validação no Editor externo.";
        }
        private void Home()
        {
            GUILayout.Label("MENU INICIAL — single-player offline");
            if (GUILayout.Button("Criar piloto e iniciar carreira")) { RefreshSlots(); confirmVacancy = false; Go(Page.Create); }
            if (GUILayout.Button("Selecionar piloto salvo")) { RefreshSlots(); Go(Page.Select); }
            if (GUILayout.Button("Migrar carreira do terminal — instruções")) Go(Page.Migration);
            if (GUILayout.Button("Exportar, importar e recuperar checkpoint")) Go(Page.Storage);
            if (client.HasCareer && GUILayout.Button("Voltar à carreira atual")) Go(Page.Hub);
            GUILayout.Label("Saves locais próprios .stint, até 3 pilotos. Os JSON do terminal não são importados nem alterados.");
        }
        private void Migration()
        {
            if (browserStore != null) {
                GUILayout.Label("TERMINAL → NAVEGADOR: converta explicitamente o JSON para .stint no terminal.");
                GUILayout.Label("Use a exportação/importação abaixo para selecionar o .stint ou colar Base64. Não informe caminho desktop como pasta do navegador.");
                if (GUILayout.Button("Abrir importação de checkpoint")) Go(Page.Storage);
                if (GUILayout.Button("Menu inicial")) Go(Page.Home);
                return;
            }
            GUILayout.Label("IMPORTAÇÃO EXPLÍCITA — JSON → CHECKPOINT DESKTOP");
            GUILayout.Label("O conversor validado roda no terminal .NET 8. Esta UI não decodifica JSON e não inicia processos externos.");
            GUILayout.Label("Na raiz do repositório, execute carreira importar-unity com --arquivo, --slots e --slot.");
            GUILayout.Label("Exemplo: dotnet run --project src/Stintoria.Cli -- carreira importar-unity --arquivo saves/carreira.json --slots pasta/slots --slot 1");
            GUILayout.Label("Confira piloto, ID, seed, etapas, formato e destino na prévia. Confirme digitando IMPORTAR N, com o número do slot.");
            GUILayout.Label("Pasta REAL deste cliente desktop:");
            GUILayout.TextArea(slotsDirectory);
            if (GUILayout.Button("Copiar caminho da pasta de slots")) {
                GUIUtility.systemCopyBuffer = slotsDirectory; message = "Caminho copiado; nenhuma importação foi executada.";
            }
            GUILayout.Label("Se o terminal roda em outra máquina/Replit, use uma pasta de preparação lá; não informe caminho local do seu computador como se fosse remoto.");
            GUILayout.Label("Após conversão remota, transfira só o .stint validado para um slot vazio na pasta acima. Não sobrescreva arquivos existentes.");
            GUILayout.Label("JSON original nunca é alterado. Slots ocupados são recusados; não há substituição automática.");
            if (GUILayout.Button("Atualizar slots e selecionar piloto")) ActionAsync(async () => {
                if (browserStore != null) await browserStore.RefreshAsync();
                RefreshSlots(); Go(Page.Select);
            });
            if (GUILayout.Button("Menu inicial")) Go(Page.Home);
        }
        private void RefreshSlots()
        {
            for (var i = 0; i < 3; i++) {
                validSlots[i] = false;
                try {
                    if (!client.SlotExists(i + 1)) { slotDescriptions[i] = "Slot " + (i + 1) + ": vazio"; continue; }
                    var overview = client.InspectSlot(i + 1);
                    slotDescriptions[i] = "Slot " + (i + 1) + ": " + overview.Profile.Name + " — " +
                        (overview.Finished ? "campeonato encerrado" : "próxima etapa " + overview.NextRound);
                    validSlots[i] = true;
                }
                catch (Exception) { slotDescriptions[i] = "Slot " + (i + 1) + ": ilegível/incompatível; arquivo preservado"; }
            }
        }
        private void Selection()
        {
            GUILayout.Label("SELEÇÃO DE PILOTO");
            for (var i = 0; i < 3; i++) {
                GUILayout.Label(slotDescriptions[i] ?? "Slot indisponível");
                var chosen = i + 1;
                if (validSlots[i] && GUILayout.Button("Carregar slot " + chosen))
                    ActionAsync(async () => { await client.LoadAsync(chosen); Go(Page.Hub); });
            }
            if (GUILayout.Button("Atualizar lista")) ActionAsync(async () => {
                if (browserStore != null) await browserStore.RefreshAsync(); RefreshSlots();
            });
            if (GUILayout.Button("Menu inicial")) Go(Page.Home);
        }
        private void Creation()
        {
            GUILayout.Label("PILOTO FICTÍCIO — 14 anos");
            GUILayout.Label("Nome"); pilotName = GUILayout.TextField(pilotName, 60);
            GUILayout.Label("Nacionalidade"); nationality = GUILayout.TextField(nationality, 60);
            GUILayout.Label("Seed inteira (0..4294967295)"); seed = GUILayout.TextField(seed, 10);
            origin = GUILayout.SelectionGrid(origin, new[] {"Família humilde", "Ligada ao esporte", "Empresária", "Sim racer"}, 2);
            GUILayout.Label("Equipe inicial elegível");
            var previousTeam = team;
            team = GUILayout.SelectionGrid(team, CareerProfile.EligibleTeams.Select(t => t.Name).ToArray(), 2);
            if (team != previousTeam) confirmVacancy = false;
            var selected = CareerProfile.EligibleTeams[team];
            var displaced = InitialData.Drivers.First(d => d.TeamId == selected.Id);
            GUILayout.Label("Vaga: " + displaced.Name + " (" + displaced.Id + "). Novo ID: jogador. Saves anteriores não mudam.");
            confirmVacancy = GUILayout.Toggle(confirmVacancy, "Confirmo ocupar explicitamente esta vaga na nova carreira.");
            slot = GUILayout.SelectionGrid(slot, slotDescriptions, 1);
            GUILayout.Label("Criar só é permitido em slot vazio; não há sobrescrita silenciosa.");
            if (GUILayout.Button("Confirmar criação e salvar"))
                ActionAsync(async () => {
                    if (!uint.TryParse(seed, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                        throw new ArgumentException("Seed inválida.");
                    await client.CreateAsync(slot + 1, value, pilotName, nationality, (CareerOrigin)origin, selected.Id, confirmVacancy);
                    Go(Page.Hub);
                });
            if (GUILayout.Button("Cancelar")) Go(Page.Home);
        }
        private void Hub()
        {
            var info = client.Overview();
            GUILayout.Label("CENTRAL DE CARREIRA — " + info.Profile.Name + " [VOCÊ], " + info.Profile.Age + " anos");
            GUILayout.Label(info.Profile.Nationality + " | " + InitialData.Teams.Single(t => t.Id == info.Profile.TeamId).Name);
            GUILayout.Label("Etapas: " + client.History().CompletedRounds + "/8 | habilidade " + info.Skill + " | créditos " + info.Credits);
            if (info.Finished) GUILayout.Label("Campeonato concluído. Consulte campeões, resultados e histórico.");
            else if (client.ActiveRace() != null) {
                var active = client.ActiveRace();
                GUILayout.Label("Prova em andamento: " + active.State.CompletedSectors + "/36 setores salvos.");
                if (GUILayout.Button("Retomar corrida participativa")) Go(Page.Race);
            }
            else if (!info.Prepared) {
                if (GUILayout.Button("Preparar próxima etapa")) { draft = null; Go(Page.Prepare); }
            }
            else {
                GUILayout.Label("Preparação confirmada e salva; não pode ser cobrada ou editada novamente.");
                if (GUILayout.Button("Iniciar corrida participativa e salvar largada"))
                    ActionAsync(async () => { await client.StartRaceAsync(info.NextRound); Go(Page.Race); });
                if (GUILayout.Button("Corrida automática — calcular prova inteira e rever"))
                    ActionAsync(async () => { var number = info.NextRound; await client.RunAsync(); selectedRound = number; reviewSector = 1; Go(Page.Review); });
            }
            if (GUILayout.Button("Classificação de pilotos e equipes")) Go(Page.Tables);
            if (GUILayout.Button("Resultados e histórico")) { selectedRound = Math.Max(1, client.History().CompletedRounds); Go(Page.History); }
            if (GUILayout.Button("Salvar agora")) ActionAsync(async () => {
                await client.SaveAsync(); message = "Gravação confirmada no slot " + client.ActiveSlot + ".";
            });
            if (GUILayout.Button("Menu inicial")) Go(Page.Home);
        }
    }
}
