using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Verse;

namespace FasterGameLoading
{
    /// <summary>
    /// 跨 session 的載入快取資料與執行期查詢快取。
    /// 這些不是「使用者設定」，而是自動記錄的載入歷程，
    /// 僅因需要 Scribe 持久化而存放於此。
    /// </summary>
    internal static class SessionCache
    {
        // ── 跨 session 持久化資料（由 Scribe 存檔） ──

        /// <summary>
        /// 上一次 session 中所有已載入的紋理路徑映射。
        /// </summary>
internal static Dictionary<string, string> loadedTexturesSinceLastSession { get; set; } = new(StringComparer.Ordinal);

        /// <summary>
        /// 上一次 session 中所有已查詢的完整型別名稱映射。
        /// </summary>
        internal static ConcurrentDictionary<string, string> loadedTypesByFullNameSinceLastSession { get; set; } = new(StringComparer.Ordinal);

        /// <summary>
        /// 上一次 session 中啟用的 mod 列表（packageIdLowerCase）。
        /// </summary>
        internal static List<string> modsInLastSession { get; set; } = new();

        /// <summary>
        /// 上一次 session 中所有 XPath 查詢結果（僅存缺失的 XPath 查詢）。
        /// </summary>
        internal static ConcurrentDictionary<string, byte> xmlPathsSinceLastSession { get; set; } = new(StringComparer.Ordinal);

        /// <summary>
        /// 上一次 session 中所有第三方 Mod 的 XML 檔案的累積雜湊值。
        /// </summary>
        internal static long xmlCombinedHashSinceLastSession { get; set; }

        /// <summary>
        /// 上一次 session 中每個 Mod 所有 XML 檔案的 metadata 累積雜湊值。
        /// </summary>
        internal static Dictionary<string, long> xmlMetadataHashByMod { get; set; } = new(StringComparer.Ordinal);

        /// <summary>
        /// 舊版 XML 內容雜湊欄位。保留 Scribe 相容性，新版 metadata-only 掃描不再使用。
        /// </summary>
        internal static Dictionary<string, long> xmlContentHashByMod { get; set; } = new(StringComparer.Ordinal);


        /// <summary>
        /// 歷次靜態圖集烘焙速度記錄（用於自適應批次調整）。
        /// </summary>
internal static List<float> historicalBakeSpeeds { get; set; } = new();
        private static readonly object loadedTexturesLock = new();

        /// <summary>
        /// 加權移動平均的權重。
        /// </summary>
        internal static readonly float[] WEIGHTS = { 0.4f, 0.3f, 0.2f, 0.1f };

        /// <summary>
        /// 保留的歷史記錄筆數上限。
        /// 需與 WEIGHTS 長度一致，確保加權平均能正確計算。
        /// </summary>
        internal const int HISTORY_SIZE = 4;

        // ── 執行期查詢快取（不持久化） ──

        static SessionCache()
        {
            if (WEIGHTS.Length != HISTORY_SIZE)
            {
                FGLLog.Error("WEIGHTS length must match HISTORY_SIZE!");
            }
        }

        /// <summary>
        /// 由 FasterGameLoadingSettings.ExposeData() 委派呼叫，
        /// 處理所有跨 session 快取資料的序列化。
        /// </summary>
        internal static void ExposeData()
        {
            var loadedTextures = loadedTexturesSinceLastSession;
            Scribe_Collections.Look(ref loadedTextures, FGLConsts.LoadedTexturesKey, LookMode.Value, LookMode.Value);

            Dictionary<string, string> tempTypes = null;
            if (Scribe.mode is LoadSaveMode.Saving)
            {
                tempTypes = new Dictionary<string, string>(loadedTypesByFullNameSinceLastSession, StringComparer.Ordinal);
            }
            Scribe_Collections.Look(ref tempTypes, FGLConsts.LoadedTypesKey, LookMode.Value, LookMode.Value);

            Dictionary<string, bool> tempXmlPaths = null;
            if (Scribe.mode is LoadSaveMode.Saving)
            {
                tempXmlPaths = new Dictionary<string, bool>(StringComparer.Ordinal);
                foreach (var kvp in xmlPathsSinceLastSession)
                {
                    tempXmlPaths[kvp.Key] = false;
                }
            }
            Scribe_Collections.Look(ref tempXmlPaths, FGLConsts.XmlPathsKey, LookMode.Value, LookMode.Value);

            var xmlCombinedHash = xmlCombinedHashSinceLastSession;
            Scribe_Values.Look(ref xmlCombinedHash, "FGL_XmlCombinedHash", 0L);
            var xmlMetadataHash = xmlMetadataHashByMod;
            Scribe_Collections.Look(ref xmlMetadataHash, "FGL_XmlMetadataHashByMod", LookMode.Value, LookMode.Value);
            var xmlContentHash = xmlContentHashByMod;
            Scribe_Collections.Look(ref xmlContentHash, "FGL_XmlContentHashByMod", LookMode.Value, LookMode.Value);
            var mods = modsInLastSession;
            Scribe_Collections.Look(ref mods, FGLConsts.ModsInLastSessionKey, LookMode.Value);
            var bakeSpeeds = historicalBakeSpeeds;
            Scribe_Collections.Look(ref bakeSpeeds, FGLConsts.HistoricalBakeSpeedsKey, LookMode.Value);

            loadedTexturesSinceLastSession = loadedTextures;
            xmlCombinedHashSinceLastSession = xmlCombinedHash;
            xmlMetadataHashByMod = xmlMetadataHash;
            xmlContentHashByMod = xmlContentHash;
            modsInLastSession = mods;
            historicalBakeSpeeds = bakeSpeeds;


            if (Scribe.mode is LoadSaveMode.PostLoadInit)
            {
                RestoreAfterLoad(tempTypes, tempXmlPaths);
            }
        }

        /// <summary>
        /// PostLoadInit 階段的還原：補齊空集合、重建 XPath 未命中快取，
        /// 並在偵測到 mod 組合變更時清空所有跨 session 快取。
        /// </summary>
        private static void RestoreAfterLoad(Dictionary<string, string> tempTypes, Dictionary<string, bool> tempXmlPaths)
        {
            loadedTexturesSinceLastSession ??= new Dictionary<string, string>(StringComparer.Ordinal);

            if (tempTypes != null)
            {
                loadedTypesByFullNameSinceLastSession = new ConcurrentDictionary<string, string>(tempTypes, StringComparer.Ordinal);
            }
            loadedTypesByFullNameSinceLastSession ??= new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

            xmlPathsSinceLastSession ??= new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
            RebuildXPathMissCache(tempXmlPaths);

            modsInLastSession ??= new List<string>();
            xmlMetadataHashByMod ??= new Dictionary<string, long>(StringComparer.Ordinal);
            xmlContentHashByMod ??= new Dictionary<string, long>(StringComparer.Ordinal);
            historicalBakeSpeeds ??= new List<float>();

            if (DetectModSetChange())
            {
                lock (loadedTexturesLock)
                {
                    loadedTexturesSinceLastSession.Clear();
                }
                loadedTypesByFullNameSinceLastSession.Clear();
                xmlPathsSinceLastSession.Clear();
                xmlMetadataHashByMod.Clear();
                xmlContentHashByMod.Clear();
                FasterGameLoadingMod.Instance?.CacheManager?.ClearCache();
            }
        }

        /// <summary>
        /// 以存檔中的 XPath 記錄重建未命中快取，只收下仍屬可快取且不在排除清單中的路徑。
        /// </summary>
        private static void RebuildXPathMissCache(Dictionary<string, bool> tempXmlPaths)
        {
            if (tempXmlPaths == null)
            {
                return;
            }

            xmlPathsSinceLastSession.Clear();
            foreach (var kvp in tempXmlPaths)
            {
                if (kvp.Value || !XmlNode_SelectSingleNode_Patch.IsCacheableXpath(kvp.Key))
                {
                    continue;
                }

                // 排除之前因 Bug 錯誤快取的 Ayameduki/WRelicK 相關補丁 XPath，或是包含定位符的 XPath
                if (kvp.Key.IndexOf("AT_Tag_", StringComparison.Ordinal) >= 0 ||
                    kvp.Key.IndexOf("KeyedSettings", StringComparison.Ordinal) >= 0 ||
                    kvp.Key.IndexOf("FactionDef", StringComparison.Ordinal) >= 0 ||
                    kvp.Key.IndexOf("[@", StringComparison.Ordinal) >= 0)
                {
                    continue;
                }

                xmlPathsSinceLastSession.TryAdd(kvp.Key, 0);
            }
        }

        /// <summary>
        /// 比對目前啟用的 mod 清單與上次 session 的記錄是否一致。
        /// 逐項比對而非算雜湊，以避免 GetHashCode 隨機種子碰撞與 MD5 重複記憶體配發。
        /// </summary>
        private static bool DetectModSetChange()
        {
            var currentActiveMods = ModsConfig.ActiveModsInLoadOrder.ToList();
            if (modsInLastSession == null || currentActiveMods.Count != modsInLastSession.Count)
            {
                return true;
            }

            for (int i = 0; i < modsInLastSession.Count; i++)
            {
                // currentActiveMods[i] 可能為 null（Mod 載入異常時），跳過避免 NRE
                if (currentActiveMods[i] == null || !string.Equals(currentActiveMods[i].packageIdLowerCase, modsInLastSession[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
