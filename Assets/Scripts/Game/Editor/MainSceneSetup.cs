using System.IO;
using System.Linq;
using TTTXO.Game.Bootstrap;
using TTTXO.Game.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace TTTXO.Game.Editor
{
    /// <summary>
    /// One-shot Editor tooling that assembles the Milestone 1 persistent scene: a PanelSettings
    /// asset (with a default theme style sheet), the Main scene with a single "AppRoot" GameObject
    /// wired to UIDocument + GameManager + ScreenRouter + AppBootstrap, and a Build Settings entry.
    ///
    /// Re-running the menu item is safe: existing assets/objects are reused and re-wired rather than
    /// duplicated, so it can be run again after pulling changes that touch this setup.
    ///
    /// This intentionally hand-builds the scene through the Editor API instead of a hand-authored
    /// .unity/.meta file, per project convention - those formats are not meant to be edited by hand.
    /// </summary>
    public static class MainSceneSetup
    {
        private const string ScenesFolder = "Assets/Scenes";
        private const string ScenePath = ScenesFolder + "/Main.unity";
        private const string SettingsFolder = "Assets/UI/Settings";
        private const string PanelSettingsPath = SettingsFolder + "/MainPanelSettings.asset";
        private const string ThemePath = SettingsFolder + "/MainTheme.tss";
        private const string RootUxmlPath = "Assets/UI/Screens/Root.uxml";
        private const string AppRootObjectName = "AppRoot";

        [MenuItem("TTTXO/Setup/Create Main Scene")]
        public static void CreateMainScene()
        {
            EnsureFolder(SettingsFolder);
            EnsureFolder(ScenesFolder);

            var theme = GetOrCreateThemeStyleSheet();
            var panelSettings = GetOrCreatePanelSettings(theme);
            var rootUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(RootUxmlPath);

            if (rootUxml == null)
            {
                Debug.LogError($"MainSceneSetup: could not load '{RootUxmlPath}'. Make sure the root UXML exists before running this tool.");
                return;
            }

            var scene = OpenOrCreateScene();

            var appRoot = GameObject.Find(AppRootObjectName) ?? new GameObject(AppRootObjectName);

            var document = GetOrAddComponent<UIDocument>(appRoot);
            document.panelSettings = panelSettings;
            document.visualTreeAsset = rootUxml;

            GetOrAddComponent<GameManager>(appRoot);
            GetOrAddComponent<ScreenRouter>(appRoot);
            GetOrAddComponent<AppBootstrap>(appRoot);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            AddSceneToBuildSettings(ScenePath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("TTTXO MainSceneSetup: Scene created successfully.");
        }

        private static Scene OpenOrCreateScene()
        {
            if (File.Exists(ScenePath))
            {
                return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            return EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        }

        private static ThemeStyleSheet GetOrCreateThemeStyleSheet()
        {
            var existing = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            if (existing != null)
            {
                return existing;
            }

            File.WriteAllText(ThemePath, "@import url(\"unity-theme://default\");\n");
            AssetDatabase.ImportAsset(ThemePath);
            return AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
        }

        private static PanelSettings GetOrCreatePanelSettings(ThemeStyleSheet theme)
        {
            var existing = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (existing != null)
            {
                existing.themeStyleSheet = theme;
                EditorUtility.SetDirty(existing);
                return existing;
            }

            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.themeStyleSheet = theme;
            AssetDatabase.CreateAsset(panelSettings, PanelSettingsPath);
            return panelSettings;
        }

        private static T GetOrAddComponent<T>(GameObject go) where T : Component
        {
            return go.TryGetComponent<T>(out var component) ? component : go.AddComponent<T>();
        }

        private static void AddSceneToBuildSettings(string scenePath)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            var existingEntry = scenes.FirstOrDefault(s => s.path == scenePath);

            if (existingEntry != null)
            {
                existingEntry.enabled = true;
            }
            else
            {
                scenes.Insert(0, new EditorBuildSettingsScene(scenePath, true));
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void EnsureFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            string parent = Path.GetDirectoryName(folderPath)?.Replace('\\', '/');
            string folderName = Path.GetFileName(folderPath);

            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, folderName);
        }
    }
}
