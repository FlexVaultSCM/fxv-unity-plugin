using System;
using UnityEditor;
using UnityEditor.VersionControl;
using UnityEngine;

namespace FlexVault.VCS.Editor.Core
{
    /// <summary>
    /// Integrates FlexVault directly into the Unity Editor's Version Control Mode dropdown
    /// (Edit > Project Settings > Version Control).
    /// </summary>
    [VersionControl("FlexVault")]
    public class FlexVaultVersionControlObject : VersionControlObject, ISettingsInspectorExtension
    {
        private readonly FlexVaultSettingsUIState m_uiState = new FlexVaultSettingsUIState();

        public override void OnActivate()
        {
            if (Application.isBatchMode)
                return;

            FlexVaultSettings.InvalidateRepoRoot();
            if (FlexVaultSettings.IsInFlexVaultRepository())
            {
                FlexVaultStateCache.RefreshAsync();
            }
        }

        public override void OnDeactivate()
        {
        }

        void ISettingsInspectorExtension.OnInspectorGUI()
        {
            FlexVaultSettingsDrawer.DrawSettings(m_uiState, () => EditorUtility.SetDirty(this));
        }
    }
}
