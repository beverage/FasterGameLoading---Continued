using HarmonyLib;
using Verse;

namespace FasterGameLoading
{
    /// <summary>
    /// 攔截 GlobalTextureAtlasManager.BakeStaticAtlases，根據模組設定決定烘焙策略：
    /// - 沒有啟用延遲載入（DelayGraphicLoading = false）：放行原版。烘焙在啟動流程中同步完成，
    ///   自適應分批只為了分幀讓出，在同一幀內跑完沒有任何好處，反而會把圖集切得比原版更碎。
    /// - 如果延遲視覺效果尚未載入完成：跳過（稍後由 DelayedActions 處理）
    /// - 如果自適應烘焙關閉：放行原始流程
    /// </summary>
    [HarmonyPatch(typeof(GlobalTextureAtlasManager), "BakeStaticAtlases")]
    public static class GlobalTextureAtlasManager_BakeStaticAtlases_Patch
    {
        public static bool Prefix()
        {
            if (!FasterGameLoadingSettings.DelayGraphicLoading)
            {
                return true;
            }

            // 啟用了延遲圖形載入
            if (!DelayedActions.AllDeferredVisualsLoaded)
            {
                return false; // 還沒載入完，跳過
            }

            if (!FasterGameLoadingSettings.StaticAtlasesBaking)
            {
                return true; // 放行 vanilla 烘焙
            }

            return DelayedActions.AdaptiveStaticAtlasBakeFailed;
        }
    }
}
