using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Xml;
using HarmonyLib;
using Verse;

namespace FasterGameLoading
{
    /// <summary>
    /// 攔截 XmlNode.SelectSingleNode，利用跨 session 快取跳過已知不存在的 XPath 查詢。
    /// 上次 session 中回傳 null 的 XPath 路徑會在本次直接被攔截，節省重複的 XML 走訪時間。
    /// </summary>
    [HarmonyPatch(typeof(XmlNode), nameof(XmlNode.SelectSingleNode), new Type[] { typeof(string) })]
    public static class XmlNode_SelectSingleNode_Patch
    {
        /// <summary>
        /// 記錄本次 session 所有 XPath 查詢結果：true=節點存在、false=查無節點
        /// 使用 ConcurrentDictionary 以確保多執行緒環境下安全
        /// </summary>
        public static ConcurrentDictionary<string, bool> xmlPathsThisSession = new ConcurrentDictionary<string, bool>();
        private static volatile bool patchEnabled = true;

        /// <summary>
        /// 背景 XML 檔案掃描與雜湊比對是否已完成。
        /// </summary>
        public static volatile bool isXmlScanComplete = false;

        /// <summary>
        /// 背景掃描是否成功完成並驗證了快取基準。
        /// 掃描失敗或被略過時保持 false，持久化 miss 快取該 session 全程停用
        /// （fail-closed）。此旗標在掃描開始、失敗、略過與啟動完成時都會明確重設，
        /// 使每條路徑都能就地判讀，而非仰賴「先前不曾被設為 true」這個隱含前提。
        ///
        /// Whether the background scan completed successfully AND validated the
        /// cache baseline. Stays false when the scan fails or is bypassed, which
        /// disables the persisted miss cache for the whole session (fail-closed).
        /// It is reset explicitly on scan start, failure, bypass and startup
        /// completion, so every path is correct on inspection rather than
        /// relying on the implicit premise that nothing set it true earlier.
        /// </summary>
        public static volatile bool isCacheValidated = false;

        /// <summary>
        /// 標記當前執行緒是否處於補丁套用（PatchOperation.Apply）流程中。
        /// </summary>
        [ThreadStatic]
        public static bool isInPatchOperation;

        static XmlNode_SelectSingleNode_Patch()
        {
            CacheResetter.Register(() =>
            {
                isXmlScanComplete = false;
                isCacheValidated = false;
                xmlPathsThisSession.Clear();
                isXmlExtensionsActive = null;
            });

            Startup.RegisterOnStartupCompleted(() =>
            {
                try
                {
                    foreach (var kvp in xmlPathsThisSession)
                    {
                        if (!kvp.Value)
                        {
                            SessionCache.xmlPathsSinceLastSession.TryAdd(kvp.Key, 0);
                        }
                    }

                    xmlPathsThisSession.Clear();

                    if (XmlChangeDetector.needWriteSettings)
                    {
                        XmlChangeDetector.needWriteSettings = false;
                        try
                        {
                            LoadedModManager.GetMod<FasterGameLoadingMod>().WriteSettings();
                            if (FasterGameLoadingSettings.VerboseLogging)
                            {
                                FGLLog.Message("XPath cache invalidated and new hash saved to settings on main thread at startup completion.");
                            }
                        }
                        catch (Exception ex)
                        {
                            FGLLog.Warning("Failed to save updated XML combined hash at startup completion:", ex);
                        }
                    }
                }
                finally
                {
                    EndStartupCacheWindow();
                }
            });
        }

        /// <summary>
        /// 結束啟動期 XPath 快取攔截。
        /// 已收集的跨 session 未命中快取會保留到下次啟動使用，
        /// 但主選單與遊戲期間的動態 XML 查詢必須一律交由原始方法處理。
        /// </summary>
        internal static void EndStartupCacheWindow()
        {
            patchEnabled = false;
            isXmlScanComplete = false;
            isCacheValidated = false;
            xmlPathsThisSession.Clear();
        }

        public static void DisableAndClear()
        {
            EndStartupCacheWindow();
            SessionCache.xmlPathsSinceLastSession.Clear();
        }

        private static readonly object xmlExtensionsLock = new object();
        private static bool? isXmlExtensionsActive;
        public static bool IsXmlExtensionsActive
        {
            get
            {
                if (!isXmlExtensionsActive.HasValue)
                {
                    lock (xmlExtensionsLock)
                    {
                        if (!isXmlExtensionsActive.HasValue)
                        {
                            try
                            {
                                isXmlExtensionsActive = ModsConfig.IsActive("krafs.xmlextensions");
                            }
                            catch
                            {
                                isXmlExtensionsActive = false;
                            }
                        }
                    }
                }
                return isXmlExtensionsActive.Value;
            }
        }

        internal static bool IsCacheableXpath(string xpath)
        {
            if (string.IsNullOrEmpty(xpath)) return false;
            // 含有屬性篩選的 XPath (如 [@...) 屬於補丁或特定定位，不安全，不應進行快取
            if (xpath.Contains("[@")) return false;

            // 只有包含 '/'，或者以 'Defs'、'/'、'[' 開頭的 XPath 查詢才被認為是定位用的 XPath，可以安全地進行快取。
            // 避免誤快取像是 'settingsKey', 'match', 'nomatch', 'value', 'xpath' 這樣的局部子節點欄位名稱。
            return xpath.Contains("/") ||
                   xpath.StartsWith("Defs", StringComparison.OrdinalIgnoreCase) ||
                   xpath.StartsWith("/") ||
                   xpath.StartsWith("[");
        }

        public static bool Prefix(string xpath, ref XmlNode __result)
        {
            // isCacheValidated 必須與 isXmlScanComplete 一起檢查：掃描「結束」不等於
            // 掃描「成功」。掃描失敗或被略過時基準未經驗證，此時沿用上次 session 的
            // miss 快取等同於對已變更的 XML 回答舊答案。
            //
            // isCacheValidated must be checked alongside isXmlScanComplete: the scan
            // having FINISHED is not the same as the scan having SUCCEEDED. When it
            // failed or was bypassed the baseline is unverified, and honouring the
            // previous session's misses would answer stale results against XML that
            // may have changed.
            if (isInPatchOperation || !isXmlScanComplete || !isCacheValidated || !patchEnabled || !FasterGameLoadingSettings.XPathCaching || IsXmlExtensionsActive || Utils.IsMissileGirlActive)
            {
                return true;
            }

            if (!IsCacheableXpath(xpath))
            {
                return true;
            }

            bool found = SessionCache.xmlPathsSinceLastSession.ContainsKey(xpath);

            if (found)
            {
                __result = null;
                return false;
            }
            return true;
        }


        public static void Postfix(string xpath, XmlNode __result, bool __runOriginal)
        {
            if (isInPatchOperation || !__runOriginal || !patchEnabled || !FasterGameLoadingSettings.XPathCaching || IsXmlExtensionsActive || Utils.IsMissileGirlActive)
            {
                return;
            }

            if (!IsCacheableXpath(xpath))
            {
                return;
            }

            // 同一 XPath 可在多份 XML 中有不同結果；只要曾命中就不可持久化為不存在。
            xmlPathsThisSession.AddOrUpdate(xpath, __result is not null, (_, wasEverMatched) => wasEverMatched || __result is not null);
        }
    }

    /// <summary>
    /// 攔截 ModContentPack.LoadPatches，在補丁套用期間標記 isInPatchOperation，
    /// 以免 XPath 查詢被錯誤地全域快取為 null，導致補丁失效。
    /// </summary>
    [HarmonyPatch(typeof(ModContentPack), nameof(ModContentPack.LoadPatches))]
    public static class ModContentPack_LoadPatches_Patch
    {
        public static void Prefix()
        {
            XmlNode_SelectSingleNode_Patch.isInPatchOperation = true;
        }

        public static void Postfix()
        {
            XmlNode_SelectSingleNode_Patch.isInPatchOperation = false;
        }

        public static void Finalizer()
        {
            XmlNode_SelectSingleNode_Patch.isInPatchOperation = false;
        }
    }
}
