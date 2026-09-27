using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using RimWorld;
using Verse;
using Verse.Sound;

namespace FasterGameLoading
{
    /// <summary>
    /// 負責批次延遲載入圖形、圖示、更新地圖網格、以及解析音效的協程邏輯。
    /// </summary>
    public static class DeferredLoader
    {
        /// <summary>
        /// 三個預算協程的共用 driver：外層 while＋預算內批次＋yield＋RestartStopwatch。
        /// 圖形／圖示在非主執行緒時讓出執行權（防禦性保護）；音效解析不碰 Unity 物件，可直接執行。
        /// </summary>
        private delegate bool TryDequeueItem<TDef>(out TDef def, out Action run);

        private static IEnumerator DrainQueue<TDef>(
            DelayedActions delayedActions,
            Func<DelayedActions, int> getCount,
            TryDequeueItem<TDef> tryDequeue,
            Action<TDef, Action> runItem,
            bool checkMainThread)
        {
            delayedActions.RestartStopwatch();
            while (getCount(delayedActions) > 0)
            {
                // 協程只在主執行緒被恢復執行，此檢查僅為防禦性保護。
                // 若非主執行緒，讓出執行權後由外層 while 重新檢查，不落穿到 Unity 工作。
                if (checkMainThread && !UnityData.IsInMainThread)
                {
                    yield return 0;
                    continue;
                }
                while (getCount(delayedActions) > 0 && !delayedActions.IsOverBudget)
                {
                    if (!tryDequeue(out var def, out var run))
                        break;

                    runItem(def, run);
                }

                if (getCount(delayedActions) > 0)
                {
                    yield return 0;
                    delayedActions.RestartStopwatch();
                }
            }
        }

        /// <summary>
        /// 執行單一 SubSound 延遲動作；個別例外只記錄（Warning）不外傳，避免中斷批次流程。
        /// 由延遲解析協程與世界初始化收尾共用。
        /// </summary>
        internal static void TryRunSubSoundAction(SubSoundDef def, Action run)
        {
            try
            {
                run();
            }
            catch (Exception ex)
            {
                FGLLog.Warning($"Error resolving AudioGrain for {def}:", ex);
            }
        }

        /// <summary>
        /// 在時間預算內批次載入延遲的圖形紋理。
        /// </summary>
        /// <param name="delayedActions">延遲動作管理器實例，提供時間預算與佇列存取。</param>
        /// <param name="loadedDefs">存放已載入的 ThingDef 清單，供後續更新地圖網格使用。</param>
        public static IEnumerator LoadDeferredGraphicsCoroutine(DelayedActions delayedActions, ICollection<ThingDef> loadedDefs)
        {
            FGLLog.Message($"Starting deferred graphics: {delayedActions.GraphicsToLoadCount.ToString(CultureInfo.InvariantCulture)}");
            var drain = DrainQueue<ThingDef>(delayedActions, static d => d.GraphicsToLoadCount, delayedActions.TryDequeueGraphic, (def, run) => LoadOneGraphic(def, run, loadedDefs), checkMainThread: true);
            while (drain.MoveNext())
            {
                yield return drain.Current;
            }
            FGLLog.Message("Deferred graphics loaded");
        }

        /// <summary>
        /// 執行單一 ThingDef 的延遲圖形載入動作。
        /// UI 圖示交給延遲圖示佇列裡的原版回呼（ResolveIcon 另外會設定顏色、UI 材質與角度），這裡不自行填入。
        /// 個別 def 的例外只記錄不外傳，避免中斷整個延遲載入協程。
        /// </summary>
        private static void LoadOneGraphic(ThingDef def, Action action, ICollection<ThingDef> loadedDefs)
        {
            bool graphicActionSucceeded = false;
            try
            {
                action();
                loadedDefs.Add(def);
                graphicActionSucceeded = true;
            }
            catch (Exception ex)
            {
                FGLLog.Warning($"Error loading graphic for {def}:", ex);
            }
            // 僅在圖形動作成功後才呼叫 PostLoadSpecial，避免傳入損壞的圖形資料
            if (graphicActionSucceeded)
            {
                def.plant?.PostLoadSpecial(def);
            }
        }

        /// <summary>
        /// 將已載入的圖形標記為需要重新繪製地圖網格，
        /// 確保延遲載入的圖形在地圖上立即顯示。
        /// </summary>
        /// <param name="loadedDefs">已載入的 ThingDef 清單。</param>
        public static void UpdateMapMeshForLoadedDefs(IReadOnlyList<ThingDef> loadedDefs)
        {
            try
            {
                if (Current.Game != null)
                {
                    foreach (var map in Find.Maps)
                    {
                        if (map.mapDrawer.sections != null)
                        {
                            foreach (var thing in map.listerThings.ThingsOfDefs(loadedDefs))
                            {
                                map.mapDrawer.MapMeshDirty(thing.Position,
                                    MapMeshFlagDefOf.Things | MapMeshFlagDefOf.Buildings);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                FGLLog.Warning("Error updating map mesh:", ex);
            }
        }

        /// <summary>
        /// 在時間預算內批次載入延遲的圖示紋理。
        /// </summary>
        /// <param name="delayedActions">延遲動作管理器實例。</param>
        public static IEnumerator LoadDeferredIconsCoroutine(DelayedActions delayedActions)
        {
            FGLLog.Message($"Starting deferred icons: {delayedActions.IconsToLoadCount.ToString(CultureInfo.InvariantCulture)}");
            var drain = DrainQueue<BuildableDef>(delayedActions, static d => d.IconsToLoadCount, delayedActions.TryDequeueIcon, static (def, run) => LoadOneIcon(def, run), checkMainThread: true);
            while (drain.MoveNext())
            {
                yield return drain.Current;
            }
            FGLLog.Message("Deferred icons loaded");
        }

        /// <summary>
        /// 執行單一 BuildableDef 的延遲圖示載入動作。
        /// 圖示已非 BadTex 代表其他路徑已補上，直接跳過；
        /// 個別 def 的例外只記錄不外傳，避免中斷整個延遲載入協程。
        /// </summary>
        private static void LoadOneIcon(BuildableDef def, Action action)
        {
            if (def.uiIcon != BaseContent.BadTex)
            {
                return;
            }

            try
            {
                action();
            }
            catch (Exception ex)
            {
                FGLLog.Warning($"Error loading icon for {def}:", ex);
            }
        }

        /// <summary>
        /// 在時間預算內批次解析延遲的 SubSoundDef。
        /// 佇列清空後取消 SoundStarter 攔截，讓主選單與遊戲內音效恢復原版流程。
        /// </summary>
        /// <param name="delayedActions">延遲動作管理器實例。</param>
        public static IEnumerator ResolveSubSoundDefsCoroutine(DelayedActions delayedActions)
        {
            FGLLog.Message($"Starting SubSoundDef resolution: {delayedActions.SubSoundDefToResolveCount.ToString(CultureInfo.InvariantCulture)}");
            var drain = DrainQueue<SubSoundDef>(delayedActions, static d => d.SubSoundDefToResolveCount, delayedActions.TryDequeueSubSound, static (def, run) => TryRunSubSoundAction(def, run), checkMainThread: false);
            while (drain.MoveNext())
            {
                yield return drain.Current;
            }
            // 協程已執行完畢，所有延遲的 SubSoundDef 已解析完成，在此時安全取消攔截，
            // 確保若玩家留在主選單也能正常播放按鈕與背景聲音。
            SoundStarter_Patch.Unpatch();
            FGLLog.Message("SubSoundDef resolution complete");
        }
    }
}
