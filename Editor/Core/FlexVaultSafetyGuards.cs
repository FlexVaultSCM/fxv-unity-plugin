using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FlexVault.VCS.Editor.Core
{
    public static class FlexVaultSafetyGuards
    {
        public static bool EnsureSafeToMutateWorkspace(string operationName, bool promptSaveDirtyScenes = true)
        {
            if (EditorApplication.isPlaying || EditorApplication.isPaused)
            {
                EditorUtility.DisplayDialog(
                    $"{operationName} Blocked",
                    $"Cannot perform '{operationName}' while the Unity Editor is in Play Mode.\nPlease exit Play Mode before modifying workspace files.",
                    "OK");
                return false;
            }

            if (EditorApplication.isCompiling)
            {
                EditorUtility.DisplayDialog(
                    $"{operationName} Blocked",
                    $"Cannot perform '{operationName}' while the Unity Editor is compiling scripts.\nPlease wait for compilation to finish before modifying workspace files.",
                    "OK");
                return false;
            }

            if (promptSaveDirtyScenes)
            {
                bool saved = EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
                if (!saved)
                {
                    return false;
                }
            }

            AssetDatabase.SaveAssets();
            return true;
        }
    }
}
