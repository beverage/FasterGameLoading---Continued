using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace FasterGameLoading
{
    /// <summary>
    /// FasterGameLoading 模組的進入點。
    /// 初始化 Harmony patch、設定、延遲動作管理器，並註冊快取重置回呼。
    /// </summary>
    public class FasterGameLoadingMod : Mod
    {
        public static FasterGameLoadingMod Instance { get; private set; }
        public static Harmony harmony { get; private set; }
        public static FasterGameLoadingSettings settings { get; private set; }
        public static DelayedActions delayedActions { get; private set; }

        public TextureCacheManager CacheManager { get; private set; }
        public TextureResize Resizer { get; private set; }

        public FasterGameLoadingMod(ModContentPack pack) : base(pack)
        {
            Instance = this;
            CacheManager = new TextureCacheManager();
            Resizer = new TextureResize(CacheManager);

            var gameObject = new GameObject("FasterGameLoadingMod");
            Object.DontDestroyOnLoad(gameObject);
            delayedActions = gameObject.AddComponent<DelayedActions>();
            settings = this.GetSettings<FasterGameLoadingSettings>();

            // 背景預載入已快取的紋理
            ModContentLoaderTexture2D_LoadTexture_Patch.StartPreloadCachedTextures();
            StartCleanupInvalidImageOptCaches();

            harmony = new Harmony("FasterGameLoadingMod");

            // 背景預載入所有類型，以加速後續的 AccessTools.AllTypes() 呼叫
            AccessTools_AllTypes_Patch.Preload();
            harmony.PatchAll();
            ImageOptEarlyLoadCoordinator.TryInstall();

            // 註冊執行個體層級的快取清理（在語言切換時由 CacheResetter.ResetAll() 觸發）
            CacheResetter.Register(() =>
            {
                if (delayedActions) // 利用 Unity Object 的隱式 bool 轉型檢查，防範 GameObject 銷毀時的異常
                {
                    delayedActions.StopAllCoroutines();
                    delayedActions.ClearQueues();
                    delayedActions.ResetEarlyLoading();
                }
                try
                {
                    SoundStarter_Patch.ResetUnpatchedStatus();
                    harmony.PatchCategory("SoundStarter");
                }
                catch (System.InvalidOperationException)
                {
                    // 補丁類別 "SoundStarter" 尚未被註冊或已經被解除補丁 — 靜默跳過
                }
            });

            StartXmlScan();

        }

        private static void StartXmlScan()
        {
            // XML metadata 僅在背景執行緒讀取；快取狀態由 Update 主執行緒提交。
            try
            {
                // 掃描目標取自引擎已解析的內容根目錄清單
                // (ModContentPack.foldersToLoadDescendingOrder)，而非自行探測目錄佈局：
                // 該清單已涵蓋版本資料夾、Common，以及 LoadFolders.xml 宣告的任意深度
                // 路徑，因此像 1.6/ModSupport/Royalty/Defs 這種兩層以上的內容也會被看見
                // （issue #5）。此欄位由 ModContentPack 建構式填入，早於 CreateModClasses，
                // 所以在本建構式執行時已就緒。
                var scanTargets = new List<XmlChangeDetector.ModScanTarget>();
                foreach (var contentPack in LoadedModManager.RunningMods)
                {
                    if (contentPack == null || contentPack.IsOfficialMod || string.IsNullOrEmpty(contentPack.RootDir))
                    {
                        continue;
                    }

                    var roots = contentPack.foldersToLoadDescendingOrder;
                    if (roots == null || roots.Count is 0)
                    {
                        roots = new List<string> { contentPack.RootDir };
                    }

                    // 鍵沿用 Mod 根目錄，與先前版本一致，避免升級時整批快取失效。
                    scanTargets.Add(new XmlChangeDetector.ModScanTarget(contentPack.RootDir.ToLowerInvariant(), roots));
                }
                XmlNode_SelectSingleNode_Patch.isXmlScanComplete = false;
                XmlChangeDetector.StartScanAsync(scanTargets, null, delayedActions.EnqueueMainThreadAction);
            }
            catch (System.Exception ex)
            {
                // 掃描無法啟動：標記完成以免流程永久懸置，但不驗證快取
                // （isCacheValidated 保持 false），並清空持久化 miss 快取，
                // 以冷啟動語義運作（fail-closed）。安全動作先於記錄執行。
                XmlNode_SelectSingleNode_Patch.isCacheValidated = false;
                XmlNode_SelectSingleNode_Patch.isXmlScanComplete = true;
                SessionCache.xmlPathsSinceLastSession.Clear();
                FGLLog.Warning("Failed to start XML file scan:", ex);
            }
        }

        private static void StartCleanupInvalidImageOptCaches()
        {
            if (!ImageOptCompat.IsActive) return;

            var roots = new List<string>();
            foreach (var mod in ModsConfig.ActiveModsInLoadOrder)
            {
                if (mod?.RootDir != null)
                {
                    roots.Add(mod.RootDir.FullName);
                }
            }

            Task.Run(() =>
            {
                Thread.Sleep(FGLConsts.TexturePreloadDelayMs);
                var deleted = ImageOptCompat.CleanupInvalidDdsZstdCaches(roots);
                if (deleted > 0)
                {
                    FGLLog.Message($"Removed invalid ImageOpt DDS cache files: {deleted.ToString(CultureInfo.InvariantCulture)}");
                }
            });
        }



        public override string SettingsCategory()
        {
            return "FGL_ModName".Translate();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            base.DoSettingsWindowContents(inRect);
            FasterGameLoadingSettings.DoSettingsWindowContents(inRect);
        }
    }
}

