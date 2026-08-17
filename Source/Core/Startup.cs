using System;
using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace FasterGameLoading
{
    /// <summary>
    /// 在所有 StaticConstructorOnStartupUtility 完成後執行收尾工作：
    /// 儲存跨 session 快取資料、注入翻譯、排程延遲動作。
    /// </summary>
    [HarmonyPatch(typeof(StaticConstructorOnStartupUtility), nameof(StaticConstructorOnStartupUtility.CallAll))]
    public static class Startup
    {
        private static readonly List<Action> onStartupCompleted = new List<Action>();

        /// <summary>
        /// 註冊在遊戲啟動載入完畢（CallAll 完成）後要執行的回呼。
        /// </summary>
        public static void RegisterOnStartupCompleted(Action callback)
        {
            if (callback != null)
            {
                onStartupCompleted.Add(callback);
            }
        }

        public static void Postfix()
        {
            // 儲存目前 session 的數據，以用於跨 session 快取（使用 loop 避免 LINQ 分配）
            var activeMods = ModsConfig.ActiveModsInLoadOrder;
            var mods = new List<string>();
            if (activeMods != null)
            {
                foreach (var mod in activeMods)
                {
                    if (mod != null)
                    {
                        mods.Add(mod.packageIdLowerCase);
                    }
                }
            }
            SessionCache.modsInLastSession = mods;

            RunStartupCallbacks();
            StartBackgroundCacheCleanup();
            InjectTranslations();
            ScheduleDeferredStartupActions();
        }

        /// <summary>依註冊順序執行所有啟動完成回呼；個別回呼的例外只記錄，不影響其餘回呼。</summary>
        private static void RunStartupCallbacks()
        {
            foreach (var callback in onStartupCompleted)
            {
                try
                {
                    callback();
                }
                catch (Exception ex)
                {
                    FGLLog.Error("Error executing startup-completed callback:", ex);
                }
            }
            onStartupCompleted.Clear();
        }

        /// <summary>在背景執行緒啟動過期／無效的材質快取自動清理，避免阻塞啟動流程與主頁面。</summary>
        private static void StartBackgroundCacheCleanup()
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    FasterGameLoadingMod.Instance?.CacheManager?.CleanupObsoleteCacheFiles();
                }
                catch (Exception ex)
                {
                    FGLLog.Warning("Error executing obsolete cache cleanup:", ex);
                }
            });
        }

        /// <summary>注入翻譯；包在 try/catch 內，避免例外中斷 StaticConstructorOnStartupUtility.CallAll。</summary>
        private static void InjectTranslations()
        {
            try
            {
                TranslationInjector.InjectTranslations();
            }
            catch (Exception ex)
            {
                FGLLog.Error("TranslationInjector.InjectTranslations execution failed", ex);
            }
        }

        /// <summary>透過 LongEventHandler 排程設定寫入與延遲動作，以避免阻塞啟動流程。</summary>
        private static void ScheduleDeferredStartupActions()
        {
            try
            {
                LongEventHandler.ExecuteWhenFinished(delegate
                {
                    LoadedModManager.GetMod<FasterGameLoadingMod>().WriteSettings();
                });
                LongEventHandler.ExecuteWhenFinished(delegate
                {
                    var delayedActions = FasterGameLoadingMod.delayedActions;
                    if (delayedActions)
                    {
                        delayedActions.enabled = true;
                        delayedActions.StartCoroutine(delayedActions.PerformActions());
                    }
                });
            }
            catch (Exception ex)
            {
                FGLLog.Error("Error scheduling startup completion actions in LongEventHandler", ex);
            }
        }
    }
}
