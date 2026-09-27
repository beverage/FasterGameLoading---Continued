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

        static AdaptiveBakingSkipList()
        {
            CacheResetter.Register(static () =>
            {
                lock (rootsLock)
                {
                    targetModRoots.Clear();
                    rootsInitialized = false;
                }
            });
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

            return ModDependencyReflection.DependsOnAlienRaces(mod.ModMetaData)
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
            if (!FasterGameLoadingSettings.StaticAtlasesBaking) return false;
            return IsProtectedModTexturePath(path);
        }

        public static bool IsProtectedModTexturePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            InitializeModRoots();

            string normalizedPath = path.Replace('\\', '/');
            // 直接在鎖內走訪，不另外複製一份清單：本方法是每張貼圖都會走的熱路徑，
            // 而下方只做純字串比對，持鎖期間不會回呼外部程式碼。
            lock (rootsLock)
            {
                foreach (var root in targetModRoots)
                {
                    if (normalizedPath.Equals(root, StringComparison.OrdinalIgnoreCase)
                        || normalizedPath.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public static bool Prepare() => FasterGameLoadingSettings.StaticAtlasesBaking;

        /// <summary>
        /// 在將主紋理與遮罩紋理寫入靜態圖集前進行攔截。
        /// 如果該紋理屬於目標排除 Mod，則回傳 false 跳過原方法。
        /// 只比對實體（實體來自以完整路徑判定的載入流程）；不以檔名比對，
        /// 否則其他 Mod 的同名貼圖（例如 Body_north）也會被誤排除在圖集之外。
        /// </summary>
        public static bool Prefix(TextureAtlasGroup group, Texture2D texture, Texture2D mask)
        {
            // 主紋理或遮罩任一屬於排除 Mod，就回傳 false 跳過原方法（不寫入靜態圖集）。
            // 註：Dictionary.ContainsKey(null) 會拋例外，故 null 需先短路。
            return (texture == null || !ModContentLoaderTexture2D_LoadTexture_Patch.skippedBakingTextures.ContainsKey(texture))
                && (mask == null || !ModContentLoaderTexture2D_LoadTexture_Patch.skippedBakingTextures.ContainsKey(mask));
        }
    }
}
