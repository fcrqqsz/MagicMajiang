using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.IO;
using System.Collections.Generic;
using System.Linq;

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

        // Merge with existing build settings instead of overwriting
        var requiredScenes = new string[]
        {
            "Assets/Scenes/00_Persistent.unity",
            "Assets/Scenes/01_Login.unity",
            "Assets/Scenes/02_MainLobby.unity",
            "Assets/Scenes/03_Game.unity"
        };

        var existingScenes = EditorBuildSettings.scenes.ToList();
        var existingPaths = new HashSet<string>(existingScenes.Select(s => s.path));

        foreach (string scenePath in requiredScenes)
        {
            if (!existingPaths.Contains(scenePath))
            {
                existingScenes.Add(new EditorBuildSettingsScene(scenePath, true));
            }
        }

        EditorBuildSettings.scenes = existingScenes.ToArray();
        Debug.Log($"Build settings updated. Total scenes: {existingScenes.Count}");
        AssetDatabase.SaveAssets();
    }
}
