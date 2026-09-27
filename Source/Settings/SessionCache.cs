using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        /// 上一次 session 中所有已查詢的完整型別名稱映射。
        /// </summary>
        internal static ConcurrentDictionary<string, string> loadedTypesByFullNameSinceLastSession { get; set; } = new(StringComparer.Ordinal);

        /// <summary>
        /// 產生 <see cref="loadedTypesByFullNameSinceLastSession"/> 那個 session 的組件指紋。
        /// 與本次指紋不符（mod 更新、遊戲更新）時，型別對照可能已指向不存在的類別，必須整份捨棄。
        /// </summary>
        internal static string typeCacheAssemblyFingerprint { get; set; }

        /// <summary>
        /// 上一次 session 中啟用的 mod 列表（packageIdLowerCase）。
        /// </summary>
        internal static List<string> modsInLastSession { get; set; } = new();

        /// <summary>
        /// 歷次靜態圖集烘焙速度記錄（用於自適應批次調整）。
        /// </summary>
        internal static List<float> historicalBakeSpeeds { get; set; } = new();

        /// <summary>
        /// 加權移動平均的權重。
        /// </summary>
        internal static readonly float[] WEIGHTS = { 0.4f, 0.3f, 0.2f, 0.1f };

        /// <summary>
        /// 保留的歷史記錄筆數上限。
        /// 需與 WEIGHTS 長度一致，確保加權平均能正確計算。
        /// </summary>
        internal const int HISTORY_SIZE = 4;

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
            Dictionary<string, string> tempTypes = null;
            if (Scribe.mode is LoadSaveMode.Saving)
            {
                tempTypes = new Dictionary<string, string>(loadedTypesByFullNameSinceLastSession, StringComparer.Ordinal);
            }
            Scribe_Collections.Look(ref tempTypes, FGLConsts.LoadedTypesKey, LookMode.Value, LookMode.Value);
            // 讀檔分成 LoadingVars 與 PostLoadInit 兩輪，各自重新呼叫本方法：
            // 值型別字典只在 LoadingVars 那輪被填入 tempTypes，PostLoadInit 那輪的區域變數必為 null。
            // 因此必須在同一輪就寫回靜態欄位，否則上次存下的對照永遠不會被讀回來。
            if (Scribe.mode is LoadSaveMode.LoadingVars && tempTypes != null)
            {
                loadedTypesByFullNameSinceLastSession = new ConcurrentDictionary<string, string>(tempTypes, StringComparer.Ordinal);
            }

            var fingerprint = Scribe.mode is LoadSaveMode.Saving
                ? ComputeCurrentAssemblyFingerprint()
                : typeCacheAssemblyFingerprint;
            Scribe_Values.Look(ref fingerprint, FGLConsts.TypeCacheAssemblyFingerprintKey);
            var mods = modsInLastSession;
            Scribe_Collections.Look(ref mods, FGLConsts.ModsInLastSessionKey, LookMode.Value);
            var bakeSpeeds = historicalBakeSpeeds;
            Scribe_Collections.Look(ref bakeSpeeds, FGLConsts.HistoricalBakeSpeedsKey, LookMode.Value);

            typeCacheAssemblyFingerprint = fingerprint;
            modsInLastSession = mods;
            historicalBakeSpeeds = bakeSpeeds;

            if (Scribe.mode is LoadSaveMode.PostLoadInit)
            {
                RestoreAfterLoad();
            }
        }

        /// <summary>
        /// PostLoadInit 階段的還原：補齊空集合；mod 組合或組件變更時捨棄型別對照，
        /// mod 組合變更時另外移除已不屬於執行中 mod 的降質快取項目。
        /// </summary>
        private static void RestoreAfterLoad()
        {
            loadedTypesByFullNameSinceLastSession ??= new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
            modsInLastSession ??= new List<string>();
            historicalBakeSpeeds ??= new List<float>();

            if (DetectModSetChange())
            {
                loadedTypesByFullNameSinceLastSession.Clear();
                // 降質快取以「原始路徑＋檔案大小＋修改時間」自我驗證，mod 清單變動不代表快取過期；
                // 只移除原始檔已不屬於任何執行中 mod 的項目，對應的快取檔交由啟動後的背景清理刪除。
                FasterGameLoadingMod.Instance?.CacheManager?.RemoveEntriesOutside(RunningModRootDirectories());
            }
            else if (ComputeCurrentAssemblyFingerprint() is not { } currentFingerprint
                || !string.Equals(typeCacheAssemblyFingerprint, currentFingerprint, StringComparison.Ordinal))
            {
                // mod 清單相同但組件內容變了（mod 或遊戲更新）：型別可能已改名或搬移，舊對照不可再用。
                // 本次指紋算不出來時也一律捨棄，否則兩端都是 null 會被誤判為一致。
                loadedTypesByFullNameSinceLastSession.Clear();
            }
        }

        /// <summary>所有執行中 mod 的根目錄；讀檔時（mod 建構子內）清單已建立完成。</summary>
        private static IEnumerable<string> RunningModRootDirectories()
        {
            foreach (var mod in LoadedModManager.RunningMods)
            {
                if (mod != null)
                {
                    yield return mod.RootDir;
                }
            }
        }

        /// <summary>
        /// 以遊戲本體與所有執行中 mod 的組件（名稱 + MVID）計算指紋。
        /// 只取這些在 mod 類別建構前就已固定的組件，確保讀檔與存檔兩端算出的集合一致；
        /// 無法計算時回傳 null，讓比對失敗而捨棄快取（fail-closed）。
        /// </summary>
        internal static string ComputeCurrentAssemblyFingerprint()
        {
            try
            {
                var assemblies = new List<Assembly> { typeof(GenTypes).Assembly };
                foreach (var mod in LoadedModManager.RunningMods)
                {
                    var loaded = mod?.assemblies?.loadedAssemblies;
                    if (loaded != null)
                    {
                        assemblies.AddRange(loaded);
                    }
                }
                return ComputeAssemblyFingerprint(assemblies);
            }
            catch (Exception ex)
            {
                FGLLog.Warning("Failed to compute assembly fingerprint for the type cache:", ex);
                return null;
            }
        }

        /// <summary>
        /// 純函式：以與載入順序無關的方式（依字串排序）把組件的 FullName 與 MVID 折成 MD5 十六進位字串。
        /// MVID 在每次重新編譯時都會改變，因此能偵測到版本號未變的 mod 更新。
        /// </summary>
        internal static string ComputeAssemblyFingerprint(IEnumerable<Assembly> assemblies)
        {
            var entries = assemblies
                .Where(static a => a != null && !a.IsDynamic)
                .Select(static a => a.FullName + "|" + a.ManifestModule.ModuleVersionId.ToString("N"))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static s => s, StringComparer.Ordinal);

            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(string.Join('\n', entries)));
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
        }

        /// <summary>
        /// 比對目前啟用的 mod 清單與上次 session 的記錄是否一致。
        /// 逐項比對而非算雜湊，以避免 GetHashCode 隨機種子碰撞與 MD5 重複記憶體配發。
        /// </summary>
        private static bool DetectModSetChange()
        {
            if (modsInLastSession == null) return true;

            var activeMods = ModsConfig.ActiveModsInLoadOrder;
            if (activeMods == null) return true;

            int index = 0;
            foreach (var mod in activeMods)
            {
                if (index >= modsInLastSession.Count) return true;
                if (mod == null || !string.Equals(mod.packageIdLowerCase, modsInLastSession[index], StringComparison.Ordinal))
                {
                    return true;
                }
                index++;
            }

            return index != modsInLastSession.Count;
        }
    }
}
