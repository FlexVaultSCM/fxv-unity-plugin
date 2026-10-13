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
        private static bool s_fetchInFlight;

        public static bool IsFetchInFlight => s_fetchInFlight;

        static FlexVaultPeriodicFetch()
        {
            s_lastFetchTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += CheckPeriodicFetch;
        }

        private static void CheckPeriodicFetch()
        {
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
                        FlexVaultStateCache.RefreshAsync(skipScan: false, force: false, skipRemoteUpdate: true);
                    }
                }
                else
                {
                    Debug.LogWarning($"[FlexVault] Periodic fetch failed: {result.ErrorMessage}");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[FlexVault] Periodic fetch exception: {ex.Message}");
            }
            finally
            {
                s_fetchInFlight = false;
            }
        }
    }
}
