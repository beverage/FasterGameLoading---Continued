using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using RimWorld;
using Verse;

namespace FasterGameLoading
{
    /// <summary>
    /// 負責管理與執行 Mod 內容的提早載入邏輯。
    /// </summary>
    public class EarlyModContentLoader
    {
        private List<ModContentPack> pendingEarlyLoads;
        private bool useImageOptSyncScope;
        private int consecutiveTimeouts;
        private int skipFrames;
        private const int TIMEOUT_THRESHOLD = 3;
        private const int SKIP_FRAME_COUNT = 5;

        // ReloadContentInt 在遊戲 Assembly-CSharp 中是 private，直接呼叫會觸發 JIT 的可見性驗證
        // (MethodAccessException)。提早載入需在遊戲正式流程前觸發內容重載，故經由反射呼叫；
        // 運行時遊戲 DLL 中的 Harmony 攔截 (ModContentPack_ReloadContentInt_Patch) 對反射呼叫同樣生效。
        private static readonly MethodInfo ReloadContentIntMethod = typeof(ModContentPack).GetMethod(
            "ReloadContentInt",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); // NOSONAR S3011: 遊戲 DLL 中該方法為 private，經由反射呼叫是唯一可行的整合方式

        /// <summary>
        /// 經由反射呼叫 ModContentPack.ReloadContentInt(false)，繞過 private 的可見性驗證。
        /// </summary>
        private static void InvokeReloadContentInt(ModContentPack mod)
        {
            if (ReloadContentIntMethod == null)
            {
                throw new MissingMethodException(typeof(ModContentPack).FullName, "ReloadContentInt");
            }
            try
            {
                ReloadContentIntMethod.Invoke(mod, new object[] { false });
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                // 反射會將目標方法拋出的例外包裝成 TargetInvocationException，這裡解包後原樣重新拋出。
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            }
        }

        /// <summary>
        /// 取得 Mod 內容提早載入是否已完成。
        /// </summary>
        public bool EarlyLoadingComplete { get; private set; }

        /// <summary>
        /// 每幀執行，利用空閒時間預先載入尚未處理的 Mod 內容。
        /// </summary>
        /// <param name="delayedActions">延遲動作管理器的實例，用於確認時間預算。</param>
        public void Update(DelayedActions delayedActions)
        {
            // earlyModContentLoading 採 camelCase 以相容 loading-progress 的反射查詢，詳見 FasterGameLoadingSettings
            if (EarlyLoadingComplete)
                return;

            if (!FasterGameLoadingSettings.earlyModContentLoading)
            {
                return;
            }

            if (skipFrames > 0)
            {
                skipFrames--;
                return;
            }

            if (pendingEarlyLoads == null)
            {
                // ImageOpt 整合狀態在 Mod 初始化後不會改變；每輪提早載入只判斷一次。
                useImageOptSyncScope = ImageOptEarlyLoadCoordinator.IsInstalled;
                pendingEarlyLoads = LoadedModManager.RunningMods
                    .Where(x => !ModContentPack_ReloadContentInt_Patch.loadedMods.Contains(x)
                                && !EarlyLoadSkipList.ShouldSkip(x))
                    .ToList();
            }

            delayedActions.RestartStopwatch();
            while (pendingEarlyLoads.Count > 0)
            {
                var modToLoad = pendingEarlyLoads[0];
                pendingEarlyLoads.RemoveAt(0);
                if (ModContentPack_ReloadContentInt_Patch.loadedMods.Contains(modToLoad))
                    continue;
                try
                {
                    if (useImageOptSyncScope)
                    {
                        using (ImageOptEarlyLoadCoordinator.EnterEarlyLoadSyncScope())
                        {
                            InvokeReloadContentInt(modToLoad);
                        }
                    }
                    else
                    {
                        // 未啟用 ImageOpt 時維持原始熱路徑，不建立或釋放空 scope。
                        InvokeReloadContentInt(modToLoad);
                    }
                    ModContentPack_ReloadContentInt_Patch.loadedMods.Add(modToLoad);
                }
                catch (Exception ex)
                {
                    // 載入失敗時不加入 loadedMods，讓正式流程可以重試
                    FGLLog.Warning($"Early loading failed for {modToLoad.PackageIdPlayerFacing}, will retry in normal flow:", ex);
                }

                // 用完時間預算就讓出這幀，下幀繼續
                if (delayedActions.IsOverBudget)
                {
                    consecutiveTimeouts++;
                    if (consecutiveTimeouts >= TIMEOUT_THRESHOLD)
                    {
                        consecutiveTimeouts = 0;
                        skipFrames = SKIP_FRAME_COUNT;
                    }
                    return;
                }
                else
                {
                    consecutiveTimeouts = 0;
                }
            }

            EarlyLoadingComplete = true;
        }

        /// <summary>
        /// 重置提早載入狀態（語言切換等情況）。
        /// </summary>
        public void Reset()
        {
            pendingEarlyLoads = null;
            useImageOptSyncScope = false;
            EarlyLoadingComplete = false;
            consecutiveTimeouts = 0;
            skipFrames = 0;
        }
    }
}

