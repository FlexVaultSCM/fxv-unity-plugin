using FlexVault.VCS.Editor.Core;
using UnityEditor;
using UnityEngine;

namespace FlexVault.VCS.Editor.UI
{
    [InitializeOnLoad]
    public static class FlexVaultOverlayDrawer
    {
        private static readonly Color s_addedColor = new Color(0.18f, 0.75f, 0.28f, 0.95f);
        private static readonly Color s_modifiedColor = new Color(0.24f, 0.52f, 0.95f, 0.95f);
        private static readonly Color s_deletedColor = new Color(0.92f, 0.26f, 0.21f, 0.95f);
        private static readonly Color s_conflictedColor = new Color(0.98f, 0.45f, 0.09f, 0.95f);
        private static readonly Color s_untrackedColor = new Color(0.55f, 0.55f, 0.55f, 0.95f);

        private static GUIStyle s_badgeStyle;

        static FlexVaultOverlayDrawer()
        {
            EditorApplication.projectWindowItemOnGUI += OnProjectWindowItemGUI;
        }

        private static void OnProjectWindowItemGUI(string guid, Rect selectionRect)
        {
            if (string.IsNullOrEmpty(guid) || Event.current.type != EventType.Repaint)
            {
                return;
            }

            string state = FlexVaultStateCache.GetStateByGuid(guid);
            if (string.IsNullOrEmpty(state) || state.Equals("unchanged", System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (s_badgeStyle == null)
            {
                s_badgeStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 9,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = Color.white }
                };
            }

            Color badgeColor;
            string symbol;

            switch (state.ToLowerInvariant())
            {
                case "added":
                    badgeColor = s_addedColor;
                    symbol = "+";
                    break;
                case "modified":
                    badgeColor = s_modifiedColor;
                    symbol = "~";
                    break;
                case "deleted":
                    badgeColor = s_deletedColor;
                    symbol = "-";
                    break;
                case "conflicted":
                    badgeColor = s_conflictedColor;
                    symbol = "!";
                    break;
                default:
                    badgeColor = s_untrackedColor;
                    symbol = "?";
                    break;
            }

            bool isListMode = selectionRect.height <= 20f;
            Rect badgeRect;

            if (isListMode)
            {
                badgeRect = new Rect(selectionRect.x - 4f, selectionRect.y + (selectionRect.height - 13f) * 0.5f, 13f, 13f);
            }
            else
            {
                badgeRect = new Rect(selectionRect.x + selectionRect.width - 15f, selectionRect.y + 2f, 14f, 14f);
            }

            Color prevColor = GUI.color;
            GUI.color = badgeColor;
            GUI.Box(badgeRect, GUIContent.none, EditorStyles.helpBox);
            GUI.color = Color.white;
            GUI.Label(badgeRect, symbol, s_badgeStyle);
            GUI.color = prevColor;
        }
    }
}
