using System;
using System.Collections.Generic;
using System.Linq;
using Stintoria.ClientBridge;
using Stintoria.Core;
using UnityEngine;

namespace Stintoria.UnityClient
{
    /// <summary>
    /// Maquete estática composta por primitivas Unity. Só lê elenco e snapshots
    /// fornecidos pelo motor/controlador; não move karts nem calcula classificações.
    /// </summary>
    public sealed class StintoriaKartScene : MonoBehaviour
    {
        private const float RoadWidth = 10f;
        private static readonly Color Graphite = new Color32(24, 31, 41, 255);
        private static readonly Color Asphalt = new Color32(66, 75, 87, 255);
        private static readonly Color Paddock = new Color32(36, 48, 62, 255);
        private static readonly Color Gold = new Color32(209, 176, 100, 255);
        private static readonly Color Ivory = new Color32(220, 221, 216, 255);
        private static readonly Color[] TeamColors = {
            new Color32(35, 125, 207, 255), new Color32(180, 117, 63, 255),
            new Color32(206, 70, 73, 255), new Color32(60, 160, 145, 255),
            new Color32(210, 166, 47, 255), new Color32(119, 88, 187, 255),
            new Color32(197, 91, 155, 255), new Color32(63, 169, 204, 255),
            new Color32(135, 151, 164, 255), new Color32(105, 154, 77, 255)
        };

        private readonly Dictionary<int, Material> materials = new Dictionary<int, Material>();
        private readonly Dictionary<string, Transform> kartTransforms = new Dictionary<string, Transform>();
        private Driver[] roster = new Driver[0];
        private string rosterKey = "";
        private string protagonistId = "";
        private bool snapshotAvailable = true;
        private bool topDown = true;
        private ClientRaceSession activeRace;
        private Transform kartGallery;
        private Transform followTarget;
        private Transform protagonistMarker;
        private Camera topCamera;
        private Camera followCamera;
        private Vector2 hudScroll;
        private string errorMessage = "";
        private bool initialized;

        public string CameraLabel => topDown ? "Câmera superior" : "Câmera de acompanhamento";
        public bool IsInitialized => initialized;

        private void Awake()
        {
            BuildCircuit();
            initialized = true;
            var app = GetComponent<StintoriaApp>();
            if (app != null) app.RefreshSceneFromEngine();
            else SetEngineSnapshot(InitialData.Drivers, "", null);
            SetCameraMode();
        }

        public void ToggleCamera()
        {
            topDown = !topDown;
            SetCameraMode();
        }

        public void ShowSnapshotUnavailable(string detail)
        {
            snapshotAvailable = false;
            activeRace = null;
            errorMessage = detail;
        }

        public void SetEngineSnapshot(IReadOnlyList<Driver> drivers, string playerId, ClientRaceSession race)
        {
            if (drivers == null || drivers.Count != InitialData.Drivers.Count)
            {
                ShowSnapshotUnavailable("Elenco Kart inválido; karts anteriores preservados.");
                return;
            }

            var nextRoster = drivers.ToArray();
            var nextKey = string.Join("|", nextRoster.Select(d => d.Id + ":" + d.TeamId + ":" + d.Name));
            if (nextKey != rosterKey)
            {
                RebuildGallery(nextRoster);
                rosterKey = nextKey;
            }

            roster = nextRoster;
            protagonistId = playerId ?? "";
            activeRace = race;
            snapshotAvailable = true;
            errorMessage = "";
            UpdateProtagonistMarker();
        }

        private void BuildCircuit()
        {
            var terrain = Primitive(PrimitiveType.Plane, "Terreno — placeholder", transform,
                new Vector3(0, -0.32f, 0), new Vector3(17f, 1f, 12f), Quaternion.identity, Graphite);
            terrain.name = "Terreno — placeholder gratuito";

            var lightObject = new GameObject("Iluminação simples — placeholder");
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color32(225, 226, 231, 255);
            light.intensity = 1.05f;

            var roadParent = new GameObject("Pista demonstrativa — geometria provisória").transform;
            roadParent.SetParent(transform, false);
            BuildRoundedRoad(roadParent);
            BuildStartingStripe(roadParent);

            kartGallery = new GameObject("Boxes — exposição estática, não é grid de corrida").transform;
            kartGallery.SetParent(transform, false);
            Primitive(PrimitiveType.Cube, "Piso de exposição", kartGallery,
                new Vector3(0, -0.04f, 0), new Vector3(42f, 0.3f, 21.5f), Quaternion.identity, Paddock);

            topCamera = MakeCamera("Câmera superior", new Vector3(0, 104f, 0), Quaternion.Euler(90f, 0, 0));
            topCamera.orthographic = true;
            topCamera.orthographicSize = 55f;
            topCamera.clearFlags = CameraClearFlags.SolidColor;
            topCamera.backgroundColor = new Color32(12, 19, 29, 255);

            followCamera = MakeCamera("Câmera de acompanhamento", new Vector3(0, 7f, -12f),
                Quaternion.Euler(20f, 0, 0));
            followCamera.orthographic = false;
            followCamera.fieldOfView = 58f;
            followCamera.clearFlags = CameraClearFlags.SolidColor;
            followCamera.backgroundColor = new Color32(12, 19, 29, 255);
            followCamera.enabled = false;
        }

        private Camera MakeCamera(string objectName, Vector3 position, Quaternion rotation)
        {
            var cameraObject = new GameObject(objectName);
            cameraObject.transform.SetParent(transform, false);
            cameraObject.transform.localPosition = position;
            cameraObject.transform.localRotation = rotation;
            var camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 220f;
            return camera;
        }

        private void BuildRoundedRoad(Transform parent)
        {
            var points = new List<Vector3>();
            AppendLine(points, new Vector3(-30, 0, 25), new Vector3(30, 0, 25), 12);
            AppendArc(points, new Vector3(30, 0, 13), 12, 90, 0, 8);
            AppendLine(points, new Vector3(42, 0, 13), new Vector3(42, 0, -13), 12);
            AppendArc(points, new Vector3(30, 0, -13), 12, 0, -90, 8);
            AppendLine(points, new Vector3(30, 0, -25), new Vector3(-30, 0, -25), 12);
            AppendArc(points, new Vector3(-30, 0, -13), 12, -90, -180, 8);
            AppendLine(points, new Vector3(-42, 0, -13), new Vector3(-42, 0, 13), 12);
            AppendArc(points, new Vector3(-30, 0, 13), 12, 180, 90, 8);

            for (var i = 0; i < points.Count; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                var delta = b - a;
                if (delta.sqrMagnitude < 0.01f) continue;
                var direction = delta.normalized;
                var rotation = Quaternion.LookRotation(direction, Vector3.up);
                var center = (a + b) * 0.5f;
                var length = delta.magnitude + 0.18f;

                Primitive(PrimitiveType.Cube, "Asfalto", parent, center + Vector3.up * 0.12f,
                    new Vector3(RoadWidth, 0.34f, length), rotation, Asphalt);

                if (i % 4 == 0)
                {
                    var side = Vector3.Cross(Vector3.up, direction).normalized;
                    var curbColor = i % 8 == 0 ? Gold : Ivory;
                    for (var edge = -1; edge <= 1; edge += 2)
                        Primitive(PrimitiveType.Cube, "Marcador de borda provisório", parent,
                            center + side * edge * (RoadWidth * 0.5f + 0.18f) + Vector3.up * 0.33f,
                            new Vector3(0.36f, 0.07f, length), rotation, curbColor);
                }
            }
        }

        private void BuildStartingStripe(Transform parent)
        {
            const int blocks = 12;
            for (var i = 0; i < blocks; i++)
            {
                var color = i % 2 == 0 ? Gold : Ivory;
                Primitive(PrimitiveType.Cube, "Faixa ilustrativa de largada",
                    parent, new Vector3(-RoadWidth * 0.5f + RoadWidth * (i + 0.5f) / blocks, 0.34f, 25f),
                    new Vector3(RoadWidth / blocks + 0.02f, 0.04f, 0.75f), Quaternion.identity, color);
            }
        }

        private static void AppendLine(List<Vector3> points, Vector3 from, Vector3 to, int steps)
        {
            for (var i = 0; i < steps; i++) points.Add(Vector3.Lerp(from, to, i / (float)steps));
        }

        private static void AppendArc(List<Vector3> points, Vector3 center, float radius,
            float fromDegrees, float toDegrees, int steps)
        {
            for (var i = 0; i < steps; i++)
            {
                var angle = Mathf.Lerp(fromDegrees, toDegrees, i / (float)steps) * Mathf.Deg2Rad;
                points.Add(center + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius));
            }
        }

        private void RebuildGallery(Driver[] drivers)
        {
            kartTransforms.Clear();
            foreach (Transform child in kartGallery) Destroy(child.gameObject);

            for (var i = 0; i < drivers.Length; i++)
            {
                var driver = drivers[i];
                var teamIndex = TeamIndex(driver.TeamId);
                var x = -14.6f + i % 5 * 7.3f;
                var z = -7.5f + i / 5 * 5f;
                var root = new GameObject("Exposição — " + driver.Id);
                root.transform.SetParent(kartGallery, false);
                root.transform.localPosition = new Vector3(x, 0, z);
                root.transform.localRotation = Quaternion.identity;

                Primitive(PrimitiveType.Cube, "Base de exposição", root.transform,
                    new Vector3(0, 0.17f, 0), new Vector3(4.7f, 0.14f, 4.1f), Quaternion.identity,
                    i % 2 == 0 ? Graphite : new Color32(32, 40, 51, 255));

                var body = TeamColors[teamIndex];
                var stripe = i % 2 == 0 ? Gold : Color.Lerp(body, Color.white, 0.45f);
                BuildKart(root.transform, body, stripe);
                kartTransforms[driver.Id] = root.transform;
            }

            protagonistMarker = Primitive(PrimitiveType.Sphere, "Marcador visual do protagonista",
                kartGallery, new Vector3(0, 2.6f, 0), new Vector3(0.48f, 0.48f, 0.48f),
                Quaternion.identity, Gold).transform;
            UpdateProtagonistMarker();
        }

        private void BuildKart(Transform parent, Color body, Color stripe)
        {
            var dark = new Color32(18, 22, 27, 255);
            Primitive(PrimitiveType.Cube, "Chassi provisório", parent, new Vector3(0, 0.57f, 0),
                new Vector3(1.65f, 0.42f, 3.25f), Quaternion.identity, body);
            Primitive(PrimitiveType.Cube, "Bico provisório", parent, new Vector3(0, 0.52f, 1.72f),
                new Vector3(0.82f, 0.27f, 0.92f), Quaternion.identity, stripe);
            Primitive(PrimitiveType.Cube, "Faixa de cor da equipe", parent, new Vector3(0, 0.8f, -0.02f),
                new Vector3(0.2f, 0.08f, 2.45f), Quaternion.identity, stripe);
            Primitive(PrimitiveType.Cube, "Assento provisório", parent, new Vector3(0, 0.81f, -0.48f),
                new Vector3(0.8f, 0.55f, 0.95f), Quaternion.identity, dark);
            Primitive(PrimitiveType.Sphere, "Capacete provisório", parent, new Vector3(0, 1.18f, -0.24f),
                new Vector3(0.72f, 0.66f, 0.72f), Quaternion.identity, stripe);
            Primitive(PrimitiveType.Cube, "Aerofólio provisório", parent, new Vector3(0, 0.62f, -1.72f),
                new Vector3(2.0f, 0.12f, 0.36f), Quaternion.identity, dark);

            for (var side = -1; side <= 1; side += 2)
                for (var axle = -1; axle <= 1; axle += 2)
                    Primitive(PrimitiveType.Cylinder, "Pneu provisório", parent,
                        new Vector3(side * 0.96f, 0.61f, axle * 1.04f),
                        new Vector3(0.59f, 0.32f, 0.67f), Quaternion.Euler(0, 0, 90), dark);
        }

        private int TeamIndex(string teamId)
        {
            for (var i = 0; i < InitialData.Teams.Count; i++)
                if (InitialData.Teams[i].Id == teamId) return i % TeamColors.Length;
            return 0;
        }

        private void UpdateProtagonistMarker()
        {
            if (protagonistMarker == null) return;
            Transform target;
            if (!string.IsNullOrEmpty(protagonistId) && kartTransforms.TryGetValue(protagonistId, out target))
            {
                protagonistMarker.gameObject.SetActive(true);
                protagonistMarker.position = target.position + new Vector3(0, 2.5f, 0);
                followTarget = target;
            }
            else
            {
                protagonistMarker.gameObject.SetActive(false);
                followTarget = kartTransforms.Count == 0 ? transform : kartTransforms.Values.First();
            }
        }

        private void LateUpdate()
        {
            if (topDown || followCamera == null || followTarget == null) return;
            followCamera.transform.position = followTarget.position + followTarget.TransformDirection(new Vector3(0, 6f, -9f));
            followCamera.transform.LookAt(followTarget.position + Vector3.up * 0.55f);
        }

        private void SetCameraMode()
        {
            if (topCamera != null) topCamera.enabled = topDown;
            if (followCamera != null) followCamera.enabled = !topDown;
        }

        private void OnGUI()
        {
            if (Screen.width < 720) return;
            var previousColor = GUI.color;
            var previousBackground = GUI.backgroundColor;
            var previousContent = GUI.contentColor;
            var width = Mathf.Min(440, Screen.width * 0.36f);
            var rect = new Rect(Screen.width - width - 12, 12, width, Screen.height - 24);
            GUI.color = new Color(Graphite.r, Graphite.g, Graphite.b, 0.93f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.backgroundColor = new Color32(16, 27, 43, 255);
            GUI.contentColor = Gold;
            GUILayout.BeginArea(new Rect(rect.x + 12, rect.y + 10, rect.width - 24, rect.height - 20));
            GUILayout.Label("STINTORIA • MAQUETE DE PISTA", new GUIStyle(GUI.skin.label) { fontSize = 18 });
            GUI.contentColor = Color.white;
            GUILayout.Label("Protótipo com primitivas gratuitas • sem modelo 3D final");
            GUILayout.Label("Oval genérico provisório; não representa o traçado oficial do circuito.");
            GUILayout.Label(CameraLabel + " • karts de exposição permanecem estáticos");

            if (!snapshotAvailable)
                GUILayout.Label("DADOS DO MOTOR INDISPONÍVEIS — " + errorMessage);
            else if (activeRace != null)
                ActiveRaceHud();
            else
                RosterHud();

            GUILayout.EndArea();
            GUI.color = previousColor;
            GUI.backgroundColor = previousBackground;
            GUI.contentColor = previousContent;
        }

        private void RosterHud()
        {
            GUILayout.Label("ELENCO REAL DO MOTOR — 20 karts");
            GUILayout.Label("Mostruário estático. A ordem visual não é grid, classificação ou resultado.");
            if (!string.IsNullOrEmpty(protagonistId))
                GUILayout.Label("Carreira carregada: " + (roster.FirstOrDefault(d => d.Id == protagonistId)?.Name ?? "nome indisponível"));
            hudScroll = GUILayout.BeginScrollView(hudScroll);
            foreach (var driver in roster)
                GUILayout.Label((driver.Id == protagonistId ? "[VOCÊ] " : "") + driver.Name +
                    " | " + TeamName(driver.TeamId) + " | habilidade " + driver.Skill);
            GUILayout.EndScrollView();
        }

        private void ActiveRaceHud()
        {
            RaceState state = activeRace.State;
            var circuit = InitialData.Circuits.FirstOrDefault(c => c.Id == state.CircuitId);
            GUILayout.Label("CORRIDA PARTICIPATIVA REAL • etapa " + activeRace.Round +
                " — " + (circuit == null ? state.CircuitId : circuit.Name));
            GUILayout.Label("Motor: " + state.CompletedSectors + "/36 setores • clima " + state.Weather);
            GUILayout.Label("Mapa: exposição estática; posições físicas dos karts não são simuladas.");
            if (state.CompletedSectors == 0)
            {
                GUILayout.Label("Sem tempos de setor. A ordem inicial não é um resultado.");
                return;
            }

            GUILayout.Label("CLASSIFICAÇÃO POR TEMPO ACUMULADO • não é posição física");
            hudScroll = GUILayout.BeginScrollView(hudScroll);
            foreach (var row in state.Classification)
            {
                var driver = activeRace.Drivers.FirstOrDefault(d => d.Id == row.DriverId);
                if (driver == null) continue;
                GUILayout.Label(row.Position + ". " + (row.DriverId == protagonistId ? "[VOCÊ] " : "") +
                    driver.Name + " | " + row.ElapsedMs + " ms | pneus " + row.TireWear + "/100");
            }
            GUILayout.EndScrollView();
        }

        private static string TeamName(string teamId)
        {
            var team = InitialData.Teams.FirstOrDefault(t => t.Id == teamId);
            return team == null ? "Equipe indisponível" : team.Name;
        }

        private GameObject Primitive(PrimitiveType type, string objectName, Transform parent,
            Vector3 localPosition, Vector3 localScale, Quaternion localRotation, Color color)
        {
            var item = GameObject.CreatePrimitive(type);
            item.name = objectName;
            item.transform.SetParent(parent, false);
            item.transform.localPosition = localPosition;
            item.transform.localRotation = localRotation;
            item.transform.localScale = localScale;
            var collider = item.GetComponent<Collider>();
            if (collider != null) collider.enabled = false;
            var renderer = item.GetComponent<Renderer>();
            if (renderer != null)
            {
                var material = MaterialFor(color);
                if (material != null) renderer.sharedMaterial = material;
            }
            return item;
        }

        private Material MaterialFor(Color color)
        {
            var c = (Color32)color;
            var key = c.r << 24 | c.g << 16 | c.b << 8 | c.a;
            Material existing;
            if (materials.TryGetValue(key, out existing)) return existing;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null)
            {
                Debug.LogWarning("STINTORIA: shader simples indisponível; mantendo material padrão.");
                return null;
            }

            var created = new Material(shader);
            created.color = color;
            materials.Add(key, created);
            return created;
        }

        private void OnDestroy()
        {
            foreach (var material in materials.Values)
            {
                if (Application.isPlaying) Destroy(material);
                else DestroyImmediate(material);
            }
            materials.Clear();
        }
    }
}
