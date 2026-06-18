using CubeWuweiDemo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CubeWuweiDemo.Editor
{
    public static class DemoSceneBuilder
    {
        private const string DemoScenePath = "Assets/CubeWuweiDemo/CubeWuweiDemo.unity";

        [InitializeOnLoadMethod]
        private static void PrepareDemoOnEditorLoad()
        {
            EditorApplication.delayCall += () =>
            {
                EnsureDemoInBuildSettings();
                OpenAndRepairDemoScene();
            };
        }

        [MenuItem("Cube Wuwei Demo/Create Playable Demo Scene")]
        public static void CreateScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "CubeWuweiDemo";

            EnsureMainCamera();
            EnsureGameObject();

            EditorSceneManager.SaveScene(scene, DemoScenePath);
            EnsureDemoInBuildSettings();
            Debug.Log($"Created {DemoScenePath}. Press Play to test.");
        }

        [MenuItem("Cube Wuwei Demo/Open Playable Demo Scene")]
        public static void OpenAndRepairDemoScene()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != DemoScenePath)
            {
                scene = EditorSceneManager.OpenScene(DemoScenePath, OpenSceneMode.Single);
            }

            EnsureMainCamera();
            EnsureGameObject();
            EditorSceneManager.SaveScene(scene);
        }

        private static void EnsureDemoInBuildSettings()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(DemoScenePath, true)
            };
        }

        private static void EnsureMainCamera()
        {
            var camera = Camera.main;
            if (camera == null)
            {
                var cameraObject = new GameObject("Main Camera", typeof(Camera));
                camera = cameraObject.GetComponent<Camera>();
                cameraObject.tag = "MainCamera";
            }

            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.015f, 0.012f, 0.01f);
            camera.orthographic = true;
        }

        private static void EnsureGameObject()
        {
            var game = Object.FindObjectOfType<CubeDemoGame>();
            if (game == null)
            {
                var holder = GameObject.Find("Cube Wuwei Demo") ?? new GameObject("Cube Wuwei Demo");
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(holder);
                game = holder.GetComponent<CubeDemoGame>() ?? holder.AddComponent<CubeDemoGame>();
            }

            Selection.activeObject = game.gameObject;
        }
    }
}
