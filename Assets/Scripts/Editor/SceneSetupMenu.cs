using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.IO;

public class SceneSetupMenu
{
    [MenuItem("Tools/Setup Multi-Scene Architecture")]
    public static void SetupScenes()
    {
        string[] newScenes = new string[] {
            "Assets/Scenes/00_Persistent.unity",
            "Assets/Scenes/01_Login.unity",
            "Assets/Scenes/02_MainLobby.unity"
        };

        if (!Directory.Exists("Assets/Scenes"))
        {
            Directory.CreateDirectory("Assets/Scenes");
        }

        foreach (string scenePath in newScenes)
        {
            if (!File.Exists(scenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, scenePath);
                Debug.Log($"Created {scenePath}");
            }
        }

        string oldGameScene = "Assets/Scenes/SampleScene.scene";
        string newGameScene = "Assets/Scenes/03_Game.unity";
        if (File.Exists(oldGameScene) && !File.Exists(newGameScene))
        {
            AssetDatabase.MoveAsset(oldGameScene, newGameScene);
            Debug.Log($"Renamed {oldGameScene} to {newGameScene}");
        }
        else if (!File.Exists(newGameScene))
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, newGameScene);
            Debug.Log($"Created {newGameScene}");
        }

        // Add to build settings
        var buildScenes = new EditorBuildSettingsScene[4];
        buildScenes[0] = new EditorBuildSettingsScene("Assets/Scenes/00_Persistent.unity", true);
        buildScenes[1] = new EditorBuildSettingsScene("Assets/Scenes/01_Login.unity", true);
        buildScenes[2] = new EditorBuildSettingsScene("Assets/Scenes/02_MainLobby.unity", true);
        buildScenes[3] = new EditorBuildSettingsScene("Assets/Scenes/03_Game.unity", true);

        EditorBuildSettings.scenes = buildScenes;
        Debug.Log("Build settings updated with 4 scenes.");
        AssetDatabase.SaveAssets();
    }
}
