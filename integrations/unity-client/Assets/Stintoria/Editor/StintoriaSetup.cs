using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Stintoria.UnityClient;

namespace Stintoria.UnityEditorTools
{
    public static class StintoriaSetup
    {
        [MenuItem("STINTORIA/Criar cena inicial (fonte)")]
        public static void CreateScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            const string path = "Assets/Stintoria/Scenes/KartCliente.unity";
            if (File.Exists(path) && !EditorUtility.DisplayDialog("STINTORIA",
                "A cena inicial já existe. Deseja substituí-la?", "Substituir", "Cancelar")) return;
            Directory.CreateDirectory("Assets/Stintoria/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("STINTORIA — cliente Kart");
            root.AddComponent<StintoriaKartScene>();
            root.AddComponent<StintoriaApp>();
            EditorSceneManager.SaveScene(scene, path);
            AssetDatabase.Refresh();
        }
    }
}
