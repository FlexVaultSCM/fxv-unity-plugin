using FlexVault.VCS.Editor.Core;
using UnityEditor;
using UnityEngine;

namespace FlexVault.VCS.Editor.Hooks
{
    [InitializeOnLoad]
    public static class FlexVaultPeriodicFetch
    {
        private const double PeriodicCheckIntervalSeconds = 5.0;
        private static double s_lastCheckTime = -1;
        private static double s_lastFetchTime = -1;
        private static volatile bool s_fetchInFlight;

        public static bool IsFetchInFlight => s_fetchInFlight;

        static FlexVaultPeriodicFetch()
        {
            s_lastFetchTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += CheckPeriodicFetch;
        }

        private static void CheckPeriodicFetch()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying || EditorApplication.isPaused)
            {
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            if (s_lastCheckTime >= 0 && now - s_lastCheckTime < PeriodicCheckIntervalSeconds)
            {
                return;
            }
            s_lastCheckTime = now;

            if (!FlexVaultSettings.IsFlexVaultActive())
            {
                return;
            }

            // Periodic fetch is supported only when CLI meets minimum fetch capability
            if (FlexVaultVersionGuard.IsVersionCompatible != true || !FlexVaultVersionGuard.SupportsFetch)
            {
                return;
            }

            int periodSeconds = FlexVaultSettings.FetchPeriodSeconds;
            if (periodSeconds <= 0)
            {
                return;
            }

            if (s_lastFetchTime >= 0 && now - s_lastFetchTime < periodSeconds)
            {
                return;
            }

            if (s_fetchInFlight || FlexVaultStateCache.IsRefreshing)
            {
                return;
            }

            TriggerFetch();
        }

        private static async void TriggerFetch()
        {
            s_fetchInFlight = true;
            s_lastFetchTime = EditorApplication.timeSinceStartup;

            try
            {
                var result = await FxvRunner.FetchAsync();
                if (result.Success)
                {
                    if (result.Data != null && result.Data.BytesDownloaded > 0)
                    {
                        // Skip local disk scan (working tree is untouched by fetch), force branch cache refresh, and skip remote update
                        FlexVaultStateCache.RefreshAsync(skipScan: true, force: true, skipRemoteUpdate: true);
                    }
                }
                else
                {
                    // Ignore normal cancellation during domain reload, script recompilation, or play mode transitions
                    if (result.ErrorMessage != null
                        && !result.ErrorMessage.Contains("canceled")
                        && !result.ErrorMessage.Contains("cancelled"))
                    {
                        Debug.Log($"[FlexVault] Periodic fetch completed with notice: {result.ErrorMessage}");
                    }
                }
            }
            catch (System.OperationCanceledException)
            {
                // Silently swallow cancellation from domain reloads or editor assembly transitions
            }
            catch (System.Exception ex)
            {
                Debug.Log($"[FlexVault] Periodic fetch notice: {ex.Message}");
            }
            finally
            {
                s_fetchInFlight = false;
            }
        }
    }
}
