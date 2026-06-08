using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using TEVR;

namespace TEVR.EditorTools
{
    /// <summary>
    /// EDITOR-ONLY auto-wire guard. Validates that critical inspector-assigned references on the core TEVR
    /// managers are wired in the open scene, so a missing reference is caught at author time instead of
    /// surfacing as a silent runtime failure (e.g. an unassigned demoModeButton or pointerArrow). Runs from
    /// the menu and automatically (advisory) whenever a scene is saved. Fields that the managers auto-resolve
    /// at runtime (singletons / FindAnyObjectByType / GameObject.Find) are intentionally NOT flagged.
    /// </summary>
    [InitializeOnLoad]
    internal static class TevrSceneValidator
    {
        // Curated "must be wired in the inspector" fields per component (auto-resolved fields are excluded).
        private static readonly Dictionary<string, string[]> CriticalFields = new Dictionary<string, string[]>
        {
            {
                "SessionUiController", new[]
                {
                    "signInButton", "scanLoginCodeButton", "demoModeButton", "leaveButton",
                    "qrCodeDropdown", "loginStatusText", "loginDetectionStatusText", "chatDisplayText",
                    "roomCodeInput"
                }
            },
            {
                "UIManager", new[]
                {
                    "uiCanvasRoot", "hudController", "sessionController", "remoteHighlight"
                }
            },
            {
                // The directional arrow + HUD panel are driven directly by VrHudController.
                "VrHudController", new[] { "pointerArrow", "hudPanel" }
            }
        };

        static TevrSceneValidator()
        {
            EditorSceneManager.sceneSaving += OnSceneSaving;
        }

        private static void OnSceneSaving(Scene scene, string path)
        {
            Validate(scene, logHeaderIfClean: false);
        }

        [MenuItem("TrueEchoVR/Validate Scene Wiring")]
        private static void ValidateMenu()
        {
            int issues = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++)
                issues += Validate(SceneManager.GetSceneAt(i), logHeaderIfClean: true);

            if (issues == 0)
                EditorUtility.DisplayDialog("TEVR Scene Wiring", "All critical references are wired. ✅", "OK");
            else
                EditorUtility.DisplayDialog("TEVR Scene Wiring", $"{issues} unassigned reference(s) found. See Console.", "OK");
        }

        private static int Validate(Scene scene, bool logHeaderIfClean)
        {
            if (!scene.IsValid() || !scene.isLoaded) return 0;

            int issues = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb == null) continue;
                    string typeName = mb.GetType().Name;
                    if (!CriticalFields.TryGetValue(typeName, out var fields)) continue;

                    foreach (var fieldName in fields)
                    {
                        var f = mb.GetType().GetField(fieldName,
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (f == null) continue; // field renamed/removed — skip silently
                        var val = f.GetValue(mb);
                        bool isNull = val == null || (val is Object o && o == null);
                        if (isNull)
                        {
                            issues++;
                            Debug.LogWarning($"[TEVR Wiring] '{typeName}.{fieldName}' is NOT assigned on '{GetPath(mb.transform)}' (scene '{scene.name}').", mb);
                        }
                    }
                }
            }

            if (issues == 0 && logHeaderIfClean)
                Debug.Log($"[TEVR Wiring] Scene '{scene.name}': all critical references wired. ✅");

            return issues;
        }

        private static string GetPath(Transform t)
        {
            string path = t.name;
            while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
            return path;
        }
    }
}
