using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace FasterGameLoading
{
    /// <summary>
    /// 自適應靜態圖集烘焙 — 根據 GPU 烘焙速度動態調整批次大小，
    /// 以「每個 slice 約 8ms」為目標，避免單幀卡頓。
    /// 使用加權移動平均追蹤歷史烘焙速度，並在每 slice 後即時調整。
    /// </summary>
    public static class AdaptiveAtlasBaker
    {
        /// <summary>
        /// 自適應烘焙的可變狀態：目前測得的烘焙速度，以及依此推導的下個 slice 大小。
        /// 每烘完一批就地更新，故以 ref 傳遞。
        /// </summary>
        private struct AdaptiveBakeState
        {
            public float MeasuredBakeSpeed;
            public int AdaptivePixelsPerSlice;
        }

        /// <summary>
        /// 自適應烘焙的調校常數。整個協程期間不變，故以 in 傳遞避免複製。
        /// </summary>
        private readonly struct AdaptiveBakeTuning
        {
            public readonly float TargetBakeTime;
            public readonly float AdaptationFactor;
            public readonly int MinPixelsPerSlice;
            public readonly int MaxPixelsPerSlice;
            public readonly float PackDensity;

            public AdaptiveBakeTuning(float targetBakeTime, float adaptationFactor, int minPixelsPerSlice, int maxPixelsPerSlice, float packDensity)
            {
                TargetBakeTime = targetBakeTime;
                AdaptationFactor = adaptationFactor;
                MinPixelsPerSlice = minPixelsPerSlice;
                MaxPixelsPerSlice = maxPixelsPerSlice;
                PackDensity = packDensity;
            }
        }

        /// <summary>
        /// 自適應靜態圖集烘焙主協程。
        /// </summary>
        /// <param name="delayedActions">延遲動作管理器實例，主要用來回報或獲取狀態。</param>
        // MA0051: 速度估算、失敗收尾與提交階段都已抽出（見下方三個方法），剩下的
        // 本體是一段以 yield 分段的線性敘事：逐 group 累積批次、滿一個 slice 就烘焙
        // 並讓出一幀。要再縮短只能改成巢狀迭代器，那會改變編譯器產生的狀態機結構
        // ——正是此處必須避免的——故就地抑制。
#pragma warning disable MA0051
        public static IEnumerator PerformAdaptiveStaticAtlasBake(DelayedActions delayedActions)
#pragma warning restore MA0051
        {
            FGLLog.Message("Starting adaptive static atlas bake");

            // 初始保守估計：256×256 像素。這是時間切片大小，不是圖集最終尺寸。
            const int INITIAL_PIXELS_PER_SLICE = 256 * 256;
            var tuning = new AdaptiveBakeTuning(
                targetBakeTime: 0.008f,
                adaptationFactor: 0.2f,
                minPixelsPerSlice: 64 * 64,
                // 高效能 GPU 的上限
                maxPixelsPerSlice: 4096 * 4096,
                // 0.7–0.9 可減少圖集中的空白區域
                packDensity: 0.8f);

            var state = new AdaptiveBakeState
            {
                MeasuredBakeSpeed = EstimateInitialBakeSpeed(),
                AdaptivePixelsPerSlice = INITIAL_PIXELS_PER_SLICE,
            };

            var bakeStopwatch = new Stopwatch();
            InsertVanillaStaticAtlasEntries();
            var buildQueueSnapshot = GlobalTextureAtlasManager.buildQueue.ToList();
            var atlasesToCommit = new List<StaticTextureAtlas>();

            foreach (var kvp in buildQueueSnapshot)
            {
                var key = kvp.Key;
                var allTexturesForThisGroup = kvp.Value.Item1.ToList();

                long pixelsInCurrentSlice = 0;
                var batchForNextBake = new List<(Texture2D main, Texture2D mask)>();
                var bakedAtlasesForGroup = new List<StaticTextureAtlas>();

                foreach (Texture2D texture in allTexturesForThisGroup)
                {
                    if (texture == null) continue;

                    Texture2D mask = key.hasMask
                        && GlobalTextureAtlasManager.buildQueueMasks.TryGetValue(texture, out var m)
                        ? m : null;

                    batchForNextBake.Add((texture, mask));
                    // 使用 long 乘積避免大尺寸紋理造成 int 溢位
                    pixelsInCurrentSlice += (long)texture.width * texture.height;

                    if (pixelsInCurrentSlice >= state.AdaptivePixelsPerSlice)
                    {
                        if (!TryBakeSingleBatch(key, batchForNextBake, bakedAtlasesForGroup,
                                bakeStopwatch, pixelsInCurrentSlice, ref state, in tuning))
                        {
                            AbortBake(atlasesToCommit, bakedAtlasesForGroup);
                            yield break;
                        }

                        yield return null;
                        batchForNextBake.Clear();
                        pixelsInCurrentSlice = 0;
                    }
                }

                // 處理最後一批未滿一個 slice 的紋理
                if (batchForNextBake.Count > 0)
                {
                    if (!TryBakeSingleBatch(key, batchForNextBake, bakedAtlasesForGroup,
                            bakeStopwatch, pixelsInCurrentSlice, ref state, in tuning))
                    {
                        AbortBake(atlasesToCommit, bakedAtlasesForGroup);
                        yield break;
                    }
                    yield return null;
                }

                // 將此 group 的所有烘焙結果放入全域清單
                foreach (var atlas in bakedAtlasesForGroup)
                {
                    atlasesToCommit.Add(atlas);
                }
            }

            CommitBakedAtlases(atlasesToCommit, state.MeasuredBakeSpeed);
            FGLLog.Message("Adaptive static atlas bake complete");
        }

        /// <summary>
        /// 以歷史記錄的加權移動平均推估本次的起始烘焙速度（像素／秒）；
        /// 初次執行時回傳保守估計值。
        /// </summary>
        private static float EstimateInitialBakeSpeed()
        {
            if (SessionCache.historicalBakeSpeeds.Count is 0)
            {
                // 初次執行：使用保守估計值
                return 2_000_000f;
            }

            float weightedSum = 0f;
            float weightSum = 0f;
            int count = Math.Min(SessionCache.historicalBakeSpeeds.Count, SessionCache.WEIGHTS.Length);
            for (int i = 0; i < count; i++)
            {
                weightedSum += SessionCache.historicalBakeSpeeds[i] * SessionCache.WEIGHTS[i];
                weightSum += SessionCache.WEIGHTS[i];
            }
            return weightedSum / weightSum;
        }

        /// <summary>
        /// 烘焙失敗時的收尾：銷毀已產生的所有圖集紋理並豎起失敗旗標，
        /// 由呼叫端接手 fallback 到原版烘焙流程。
        /// </summary>
        private static void AbortBake(List<StaticTextureAtlas> atlasesToCommit, List<StaticTextureAtlas> bakedAtlasesForGroup)
        {
            DestroyAtlases(atlasesToCommit);
            DestroyAtlases(bakedAtlasesForGroup);
            DelayedActions.AdaptiveStaticAtlasBakeFailed = true;
        }

        /// <summary>
        /// 提交所有烘焙完成的圖集、記錄本次速度供下次啟動參考，
        /// 並清空原始 buildQueue 防止 vanilla 重複處理。
        /// </summary>
        private static void CommitBakedAtlases(List<StaticTextureAtlas> atlasesToCommit, float measuredBakeSpeed)
        {
            foreach (var staticTextureAtlas in atlasesToCommit)
            {
                GlobalTextureAtlasManager.staticTextureAtlases.Add(staticTextureAtlas);
            }

            SessionCache.historicalBakeSpeeds.Insert(0, measuredBakeSpeed);
            if (SessionCache.historicalBakeSpeeds.Count > SessionCache.HISTORY_SIZE)
            {
                SessionCache.historicalBakeSpeeds.RemoveAt(SessionCache.HISTORY_SIZE);
            }

            GlobalTextureAtlasManager.buildQueue.Clear();
            GlobalTextureAtlasManager.buildQueueMasks.Clear();
        }

        private static void InsertVanillaStaticAtlasEntries()
        {
            BuildingsDamageSectionLayerUtility.TryInsertIntoAtlas();
            MinifiedThing.TryInsertIntoAtlas();
        }

        private static bool TryBakeSingleBatch(
            TextureAtlasGroupKey key,
            List<(Texture2D main, Texture2D mask)> batch,
            List<StaticTextureAtlas> bakedAtlases,
            Stopwatch bakeStopwatch,
            long pixelsInThisSlice,
            ref AdaptiveBakeState state,
            in AdaptiveBakeTuning tuning)
        {
            try
            {
                var atlas = new StaticTextureAtlas(key);
                foreach (var (main, msk) in batch)
                {
                    atlas.Insert(main, msk);
                }

                if (batch.Count is 1)
                {
                    // 單紋理批次：Bake() 不支援單一紋理，改為直接指定 colorTexture/maskTexture
                    // 並呼叫 BuildMeshesForUvs([全幅 UV])。
                    // BuildMeshesForUvs 會完整填入 atlas.tiles（textures[0] → uvRect(0,0,1,1)），
                    // 與多紋理路徑呼叫 Bake() 後的 tiles 結構等價，故此路徑正確。
                    // 審查曾疑慮「UV 退化」與「material 重建」，經反編譯確認：
                    //   - BuildMeshesForUvs 完整建立 tiles，無 UV 退化問題；
                    //   - StaticTextureAtlasTile 無 material 欄位，material 疑慮不成立。
                    atlas.colorTexture = batch[0].main;
                    if (key.hasMask)
                    {
                        atlas.maskTexture = batch[0].mask;
                    }
                    atlas.BuildMeshesForUvs([new Rect(0, 0, 1, 1)]);
                    bakeStopwatch.Reset();
                }
                else
                {
                    bakeStopwatch.Restart();
                    atlas.Bake();
                    bakeStopwatch.Stop();
                }

                bakedAtlases.Add(atlas);

                // 根據實際烘焙時間調整下個 slice 的大小
                double secondsElapsed = bakeStopwatch.Elapsed.TotalSeconds;
                if (secondsElapsed > 0)
                {
                    float latestBakeSpeed = (float)(pixelsInThisSlice / secondsElapsed);
                    state.MeasuredBakeSpeed = Mathf.Lerp(state.MeasuredBakeSpeed, latestBakeSpeed, tuning.AdaptationFactor);
                    float newSliceSize = state.MeasuredBakeSpeed * tuning.TargetBakeTime;
                    int adjusted = (int)(newSliceSize * tuning.PackDensity);
                    state.AdaptivePixelsPerSlice = Mathf.Clamp(
                        adjusted.FloorToPowerOfTwo(),
                        tuning.MinPixelsPerSlice, tuning.MaxPixelsPerSlice);
                }

                return true;
            }
            catch (Exception ex)
            {
                FGLLog.Warning("Error baking atlas batch:", ex);
                return false;
            }
        }

        private static void DestroyAtlases(List<StaticTextureAtlas> atlases)
        {
            foreach (var atlas in atlases)
            {
                DestroyAtlasTextures(atlas);
            }
            atlases.Clear();
        }

        private static void DestroyAtlasTextures(StaticTextureAtlas atlas)
        {
            if (atlas == null) return;

            if (atlas.colorTexture != null && !atlas.textures.Contains(atlas.colorTexture))
            {
                UnityEngine.Object.Destroy(atlas.colorTexture);
                atlas.colorTexture = null;
            }

            if (atlas.maskTexture != null && !atlas.textures.Contains(atlas.maskTexture))
            {
                UnityEngine.Object.Destroy(atlas.maskTexture);
                atlas.maskTexture = null;
            }
        }
    }
}
