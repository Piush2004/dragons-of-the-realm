using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BlockRealm.EditorTools
{
    /// <summary>
    /// One-time project configuration, run automatically the first time the project opens (and available from the
    /// BlockRealm menu or <c>-executeMethod BlockRealm.EditorTools.ProjectSetup.Run</c> in batch mode): player
    /// settings, the gamepad input axes, and the (empty) start scene. Everything else is built at run time.
    /// </summary>
    public static class ProjectSetup
    {
        private const string MarkerPath = "ProjectSettings/BlockRealmSetup.txt";
        private const string ScenePath = "Assets/Scenes/Main.unity";
        private const int Version = 3;

        [InitializeOnLoadMethod]
        private static void RunOnce()
        {
            try
            {
                if (File.Exists(MarkerPath) && File.ReadAllText(MarkerPath).Trim() == Version.ToString()) return;
            }
            catch (Exception) { }
            EditorApplication.delayCall += () => { if (!EditorApplication.isCompiling) Run(); };
        }

        [MenuItem("BlockRealm/Set up project")]
        public static void Run()
        {
            try
            {
                ConfigurePlayer();
                ConfigureInput();
                CreateScene();
                File.WriteAllText(MarkerPath, Version.ToString());
                AssetDatabase.SaveAssets();
                Debug.Log("[BlockRealm] project setup complete");
            }
            catch (Exception e)
            {
                Debug.LogError("[BlockRealm] project setup failed: " + e);
            }
        }

        private static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "BlockRealm";
            PlayerSettings.productName = "BlockRealm";
            PlayerSettings.colorSpace = ColorSpace.Gamma;               // lighting values are authored for gamma space
            PlayerSettings.runInBackground = true;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.usePlayerLog = true;
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 });
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
        }

        // ------------------------------------------------------------------ gamepad axes (classic Input Manager)

        private static void ConfigureInput()
        {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/InputManager.asset");
            if (assets == null || assets.Length == 0) return;
            SerializedObject so = new SerializedObject(assets[0]);
            SerializedProperty axes = so.FindProperty("m_Axes");

            // Standard Xbox controller layout on Windows (zero-based axis indices).
            EnsureAxis(so, axes, "PadLX", 0, false);
            EnsureAxis(so, axes, "PadLY", 1, false);
            EnsureAxis(so, axes, "PadRX", 3, false);
            EnsureAxis(so, axes, "PadRY", 4, false);
            EnsureAxis(so, axes, "PadDX", 5, false);
            EnsureAxis(so, axes, "PadDY", 6, false);
            EnsureAxis(so, axes, "PadLT", 8, false);
            EnsureAxis(so, axes, "PadRT", 9, false);
            so.ApplyModifiedProperties();
        }

        private static void EnsureAxis(SerializedObject so, SerializedProperty axes, string name, int axisIndex, bool invert)
        {
            for (int i = 0; i < axes.arraySize; i++)
            {
                SerializedProperty existing = axes.GetArrayElementAtIndex(i);
                if (existing.FindPropertyRelative("m_Name").stringValue == name) return;
            }
            axes.arraySize++;
            so.ApplyModifiedProperties();
            SerializedProperty a = axes.GetArrayElementAtIndex(axes.arraySize - 1);
            a.FindPropertyRelative("m_Name").stringValue = name;
            a.FindPropertyRelative("descriptiveName").stringValue = "";
            a.FindPropertyRelative("descriptiveNegativeName").stringValue = "";
            a.FindPropertyRelative("negativeButton").stringValue = "";
            a.FindPropertyRelative("positiveButton").stringValue = "";
            a.FindPropertyRelative("altNegativeButton").stringValue = "";
            a.FindPropertyRelative("altPositiveButton").stringValue = "";
            a.FindPropertyRelative("gravity").floatValue = 0f;
            a.FindPropertyRelative("dead").floatValue = 0.19f;
            a.FindPropertyRelative("sensitivity").floatValue = 1f;
            a.FindPropertyRelative("snap").boolValue = false;
            a.FindPropertyRelative("invert").boolValue = invert;
            a.FindPropertyRelative("type").intValue = 2;                  // joystick axis
            a.FindPropertyRelative("axis").intValue = axisIndex;
            a.FindPropertyRelative("joyNum").intValue = 0;                // any joystick
        }

        // ------------------------------------------------------------------ scene

        private static void CreateScene()
        {
            Directory.CreateDirectory("Assets/Scenes");
            if (!File.Exists(ScenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
    }
}
