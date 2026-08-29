using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace FasterGameLoading
{
    /// <summary>
    /// 靜態圖集（Static Atlas）烘焙排除名單。
    /// 攔截 GlobalTextureAtlasManager.TryInsertStatic，阻止排除名單中 Mod 的紋理進入靜態圖集，避免因多遮罩（multi-mask）造成圖案衝突與載入不全。
    /// </summary>
    [HarmonyPatch(typeof(GlobalTextureAtlasManager), "TryInsertStatic")]
    public static class AdaptiveBakingSkipList
    {
        /// <summary>
        /// 調適用總開關。設為 false 即可完整停用整套烘焙排除邏輯：
        /// ・Prepare() 回 false → Harmony 不掛上 Prefix，TryInsertStatic 完全不被攔截（零執行期成本）；
        /// ・ShouldSkipBaking() 一律回 false → 不登記任何排除貼圖。
        /// </summary>
        private static readonly bool Enabled = true;

        // ── 針對的 Mod 名單 ──
        private static readonly HashSet<string> targetMods = new(StringComparer.OrdinalIgnoreCase)
        {
            "automatic.bionicicons",
            "erdelf.HumanoidAlienRaces",
            "Ancot.AncotLibrary",
        };

        private static readonly object rootsLock = new object();
        private static readonly HashSet<string> targetModRoots = new(StringComparer.OrdinalIgnoreCase);
        private static volatile bool rootsInitialized = false;
        private static bool? isAnyTargetModActive;

        static AdaptiveBakingSkipList()
        {
            CacheResetter.Register(() =>
            {
                lock (rootsLock)
                {
                    targetModRoots.Clear();
                    rootsInitialized = false;
                    isAnyTargetModActive = null;
                }
            });
        }

        /// <summary>
        /// 取得目標 Mod 是否為啟用狀態（快取判定結果以維護啟動時期的效能）。
        /// 只在判定可信時才快取：ModsConfig 命中、或 RunningMods 已就緒時才鎖定結果；
        /// RunningMods 尚未就緒則維持未判定，下次再來。
        /// </summary>
        // S1144：此屬性目前確實沒有呼叫端 —— 排除判定已改走 IsProtectedModTexturePath
        // 的路徑比對（見 ShouldSkipBaking）。刻意保留而非刪除的理由：
        // 它與 isAnyTargetModActive 快取欄位、CacheResetter 的重置註冊，以及下方三個
        // helper（IsAnyTargetModActiveViaConfig／IsActiveSafe／
        // TryDetectActiveTargetModFromRunningMods）是同一組「RunningMods 尚未就緒時
        // 不得下定論」的 fail-open 判定邏輯，該時序條件難以重建；路徑比對若日後在
        // 未提供 RootDir 的 Mod 上失效，這裡是既有且已驗證的退路。
        // 以逐處 pragma 抑制而非在 .editorconfig 全域停用 S1144，避免遮蔽其他死碼。
#pragma warning disable S1144
        private static bool IsAnyTargetModActive
        {
            get
            {
                if (isAnyTargetModActive is not null) return isAnyTargetModActive.Value;

                // 1. 先用 ModsConfig 判定（啟動早期即可用，不需等 RunningMods）
                if (IsAnyTargetModActiveViaConfig())
                {
                    isAnyTargetModActive = true;
                    return true;
                }

                // 2. Config 未命中：改掃 RunningMods，結果僅在 RunningMods 已就緒時才可信
                bool found = TryDetectActiveTargetModFromRunningMods(out bool hasRunningMods);
                if (found)
                {
                    isAnyTargetModActive = true;
                    return true;
                }
                if (hasRunningMods)
                {
                    isAnyTargetModActive = false; // 已就緒且未發現目標，定論
                    return false;
                }

                // RunningMods 尚未就緒，先不快取，下次再判定
                return false;
            }
        }
#pragma warning restore S1144

        /// <summary>透過 ModsConfig.IsActive 判定外星人種族核心或特定名單是否啟用。</summary>
        private static bool IsAnyTargetModActiveViaConfig()
        {
            if (IsActiveSafe("erdelf.HumanoidAlienRaces")) return true;
            foreach (var modId in targetMods)
            {
                if (IsActiveSafe(modId)) return true;
            }
            return false;
        }

        /// <summary>ModsConfig.IsActive 的安全包裝：初始化時期的例外一律視為未啟用。</summary>
        private static bool IsActiveSafe(string packageId)
        {
            try
            {
                return ModsConfig.IsActive(packageId);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryDetectActiveTargetModFromRunningMods(out bool hasRunningMods)
        {
            hasRunningMods = false;
            var mods = LoadedModManager.RunningMods;
            if (mods == null) return false;

            foreach (var mod in mods)
            {
                hasRunningMods = true;
                if (IsTargetMod(mod)) return true;
            }
            return false;
        }

        /// <summary>
        /// 判定單一 Mod 是否屬於需要排除烘焙的目標：命中名單、為外星人種族衍生、或依賴 Ancot 函式庫。
        /// 註：RimWorld 的 PackageId 取自 About.xml（小寫化），Steam 版不帶 "_steam" 後綴，故直接比對即可。
        /// </summary>
        private static bool IsTargetMod(ModContentPack mod)
        {
            if (mod == null) return false;

            string packageId = mod.PackageId;
            if (packageId != null && targetMods.Contains(packageId)) return true;

            return ModDependencyReflection.DependsOnMod(mod.ModMetaData, "erdelf.HumanoidAlienRaces")
                || ModDependencyReflection.DependsOnMod(mod.ModMetaData, "Ancot.AncotLibrary");
        }

        public static void InitializeModRoots()
        {
            if (rootsInitialized) return;
            var mods = LoadedModManager.RunningMods;
            if (mods == null) return;

            lock (rootsLock)
            {
                if (rootsInitialized) return;
                try
                {
                    bool hasAny = false;
                    foreach (var mod in mods)
                    {
                        hasAny = true;
                        if (IsTargetMod(mod))
                        {
                            if (string.IsNullOrEmpty(mod.RootDir)) continue;
                            string root = mod.RootDir.Replace('\\', '/').TrimEnd('/');
                            targetModRoots.Add(root);
                        }
                    }

                    if (!hasAny) return; // 載入列表尚未初始化完畢（空集合），下次再來

                    // 迴圈順利完成後才標記初始化，避免例外導致半初始化狀態被永久鎖定
                    rootsInitialized = true;
                }
                catch (Exception ex)
                {
                    FGLLog.Error("Error initializing target mod roots:", ex);
                }
            }
        }

        /// <summary>
        /// 判斷指定紋理路徑是否屬於需要排除烘焙的目標 Mod。
        /// </summary>
        public static bool ShouldSkipBaking(string path)
        {
            if (!Enabled) return false;
            if (!FasterGameLoadingSettings.StaticAtlasesBaking) return false;
            return IsProtectedModTexturePath(path);
        }

        public static bool IsProtectedModTexturePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            InitializeModRoots();

            string normalizedPath = path.Replace('\\', '/');
            // 直接在鎖內走訪，不另外複製一份清單：本方法是每張貼圖都會走的熱路徑，
            // 而 IsPathUnderRoot 只做純字串比對，持鎖期間不會回呼外部程式碼。
            lock (rootsLock)
            {
                foreach (var root in targetModRoots)
                {
                    if (IsPathUnderRoot(normalizedPath, root))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool IsPathUnderRoot(string normalizedPath, string root)
        {
            if (string.IsNullOrEmpty(normalizedPath) || string.IsNullOrEmpty(root)) return false;
            return normalizedPath.Equals(root, StringComparison.OrdinalIgnoreCase)
                || normalizedPath.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
        }

        public static bool Prepare() => Enabled && FasterGameLoadingSettings.StaticAtlasesBaking;

        /// <summary>
        /// 在將主紋理與遮罩紋理寫入靜態圖集前進行攔截。
        /// 如果該紋理屬於目標排除 Mod，則回傳 false 跳過原方法。
        /// </summary>
        public static bool Prefix(TextureAtlasGroup group, Texture2D texture, Texture2D mask)
        {
            // 主紋理或遮罩任一屬於排除 Mod，就回傳 false 跳過原方法（不寫入靜態圖集）
            return !IsTargetModTexture(texture) && !IsTargetModTexture(mask);
        }

        /// <summary>
        /// 輔助方法：判定 Texture2D 實體是否來自排除名單中的 Mod。
        /// </summary>
        private static bool IsTargetModTexture(Texture2D texture)
        {
            if (texture == null) return false;

            // 1. 優先比對實體
            if (ModContentLoaderTexture2D_LoadTexture_Patch.skippedBakingTextures.ContainsKey(texture))
            {
                return true;
            }

            // 2. 實體不同時比對檔名。texture.name 走 UnityEngine.Object.get_name() (ECall)，
            //    非 Unity 運行環境（如單元測試）下會拋例外；此時視為無檔名可比對，安全降級為 false。
            string textureName;
            try
            {
                textureName = texture.name;
            }
            catch
            {
                return false;
            }
            if (!string.IsNullOrEmpty(textureName)
                && ModContentLoaderTexture2D_LoadTexture_Patch.skippedBakingTextureNames.ContainsKey(textureName))
            {
                return true;
            }

            return false;
        }
    }
}
