using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Verse;
namespace FasterGameLoading
{
    /// <summary>
    /// 管理降質紋理快取的生命週期與對照表。
    /// </summary>
    public class TextureCacheManager
    {
        /// <summary>原始路徑 → 降質快取路徑的對照表（會透過 Scribe 持久化）。</summary>
        internal Dictionary<string, string> resizedTextureCache = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>原始路徑 → 降質快取路徑的對照表（唯讀檢視；寫入請走 SetCacheEntry）。</summary>
        public IReadOnlyDictionary<string, string> ResizedTextureCache => resizedTextureCache;
        private readonly object cacheLock = new object();
        private readonly ConcurrentDictionary<string, string> md5HashCache = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        /// <summary>
        /// 每執行緒重用的 MD5 實例。MD5 非執行緒安全，故以 ThreadLocal 隔離；
        /// 重用避免每次 GetCachePath（首次某路徑時計算雜湊）都 allocate 新 MD5 與其原生資源。
        /// </summary>
        private static readonly ThreadLocal<MD5> md5PerThread =
            new ThreadLocal<MD5>(() => MD5.Create());
        private readonly string baseCacheDir;

        /// <summary>紋理快取的根目錄。</summary>
        public string CacheDirectory => baseCacheDir ?? Path.Combine(GenFilePaths.SaveDataFolderPath, FGLConsts.ModName, FGLConsts.TextureCacheDir);

        public string BuildCacheDirectory(string suffix)
        {
            if (baseCacheDir != null)
            {
                return Path.Combine(Path.GetDirectoryName(baseCacheDir), suffix);
            }
            return Path.Combine(GenFilePaths.SaveDataFolderPath, FGLConsts.ModName, suffix);
        }

        private string activeCacheDirectory;

        public TextureCacheManager()
        {
            activeCacheDirectory = CacheDirectory;
        }

        internal TextureCacheManager(string customBaseDir)
        {
            this.baseCacheDir = customBaseDir;
            activeCacheDirectory = CacheDirectory;
        }

        /// <summary>
        /// 根據原始檔案路徑產生 MD5 快取檔案路徑。
        /// 快取鍵結合路徑、檔案大小和最後修改時間，確保原始檔案變更時自動失效。
        /// </summary>
        public string GetCachePath(string originalPath)
        {
            return md5HashCache.GetOrAdd(GetCacheKey(originalPath), ComputeCachePathFromKey);
        }

        /// <summary>
        /// 直接根據目前檔案狀態計算快取路徑，不寫入 md5HashCache，
        /// 供 IsCacheFresh 比較用，避免經 GetCachePath 重算時的 TOCTOU 風險。
        /// </summary>
        private string ComputeCachePathDirect(string originalPath)
        {
            return ComputeCachePathFromKey(GetCacheKey(originalPath));
        }

        private string ComputeCachePathFromKey(string key)
        {
            var md5 = md5PerThread.Value;
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(key));
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash)
            {
                sb.Append(b.ToString("x2"));
            }
            return Path.Combine(activeCacheDirectory, sb.ToString() + ".png");
        }

        private static string GetCacheKey(string originalPath)
        {
            try
            {
                var file = new FileInfo(originalPath);
                if (file.Exists)
                {
                    // 快取鍵必須與執行時語系無關，故數值一律以 InvariantCulture 格式化，
                    // 否則不同語系下同一檔案會產生不同的鍵而重複降質。
                    return originalPath.NormalizePath()
                        + "|" + file.Length.ToString(CultureInfo.InvariantCulture)
                        + "|" + file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
                }
            }
            catch (IOException ex)
            {
                // 無法讀取檔案資訊（路徑過長、權限不足等），改用純路徑作為快取鍵
                if (FasterGameLoadingSettings.VerboseLogging)
                {
                    FGLLog.Warning($"IOException when getting cache key for: {originalPath}", ex);
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                // 權限不足，改用純路徑作為快取鍵
                if (FasterGameLoadingSettings.VerboseLogging)
                {
                    FGLLog.Warning($"UnauthorizedAccessException when getting cache key for: {originalPath}", ex);
                }
            }
            return originalPath;
        }

        /// <summary>目前快取的紋理數量（執行緒安全）。</summary>
        public int CacheCount
        {
            get
            {
                lock (cacheLock)
                {
                    return resizedTextureCache.Count;
                }
            }
        }

        /// <summary>
        /// 嘗試取得指定原始路徑對應的快取紋理路徑。
        /// 自動檢查快取是否過期（原始檔案比快取檔案新時視為失效）。
        /// 磁碟 I/O（包括 SetLastWriteTimeUtc）在鎖定範圍外執行，避免阻塞並發紋理載入。
        /// </summary>
        public bool TryGetCachedTexturePath(string originalPath, out string cachePath)
        {
            // 第一步：在鎖內讀取對照表，取得候選快取路徑
            string candidatePath;
            bool hadEntry;
            lock (cacheLock)
            {
                hadEntry = resizedTextureCache.TryGetValue(originalPath, out candidatePath);
            }

            if (!hadEntry || candidatePath == null)
            {
                cachePath = null;
                return false;
            }

            // 第二步：在鎖外執行磁碟 I/O（存在性檢查 + 時間更新）
            bool fresh = File.Exists(candidatePath) && IsCacheFresh(originalPath, candidatePath);

            if (fresh)
            {
                cachePath = candidatePath;
                return true;
            }

            // 快取失效：移除對照表項目
            lock (cacheLock)
            {
                // 再次確認項目仍指向同一路徑，避免在鎖外期間已被更新
                if (resizedTextureCache.TryGetValue(originalPath, out var currentPath)
                    && string.Equals(currentPath, candidatePath, StringComparison.OrdinalIgnoreCase))
                {
                    resizedTextureCache.Remove(originalPath);
                }
            }

            cachePath = null;
            return false;
        }

        /// <summary>
        /// 檢查快取是否比原始檔案更新。若無法讀取檔案時間則視為失效。
        /// 此方法在 cacheLock 鎖定範圍外呼叫，可安全執行阻塞式磁碟 I/O。
        /// </summary>
        private bool IsCacheFresh(string originalPath, string cachePath)
        {
            try
            {
                if (!File.Exists(originalPath))
                {
                    return true;
                }

                var originalTime = File.GetLastWriteTimeUtc(originalPath);
                var cacheTime = File.GetLastWriteTimeUtc(cachePath);

                if (cacheTime >= originalTime)
                {
                    return true;
                }

                // 原始檔案的修改時間比快取新。若快取路徑的檔名雜湊（基於目前檔案長度）與已儲存的快取路徑一致，
                // 代表檔案長度並未改變（實質內容未變），此時只需將快取檔案的時間更新為原始檔案時間即可。
                var currentExpectedPath = ComputeCachePathDirect(originalPath);
                if (string.Equals(currentExpectedPath, cachePath, StringComparison.OrdinalIgnoreCase))
                {
                    File.SetLastWriteTimeUtc(cachePath, originalTime);
                    return true;
                }

                return false;
            }
            catch (IOException ex)
            {
                if (FasterGameLoadingSettings.VerboseLogging)
                {
                    FGLLog.Warning($"IOException checking cache freshness for: {originalPath} and {cachePath}", ex);
                }
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                if (FasterGameLoadingSettings.VerboseLogging)
                {
                    FGLLog.Warning($"UnauthorizedAccessException checking cache freshness for: {originalPath} and {cachePath}", ex);
                }
                return false;
            }
        }

        /// <summary>移除指定原始路徑的快取項目。</summary>
        public void RemoveCachedTexturePath(string originalPath)
        {
            lock (cacheLock)
            {
                resizedTextureCache.Remove(originalPath);
            }
        }

        /// <summary>清除所有紋理快取（檔案 + 記憶體對照表）。</summary>
        public void ClearCache()
        {
            try
            {
                if (Directory.Exists(CacheDirectory))
                {
                    Directory.Delete(CacheDirectory, recursive: true);
                }
                lock (cacheLock)
                {
                    resizedTextureCache.Clear();
                }
                FGLLog.Message("Texture cache cleared.");
            }
            catch (IOException ex)
            {
                FGLLog.Error("Failed to clear texture cache:", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                FGLLog.Error("Failed to clear texture cache:", ex);
            }
        }

        /// <summary>初始化縮放工作暫存目錄與快取對照表。</summary>
        public void SetupResizeStagingDirectory(string stagingDirectory)
        {
            md5HashCache.Clear();
            activeCacheDirectory = stagingDirectory;
            try
            {
                if (Directory.Exists(stagingDirectory))
                {
                    Directory.Delete(stagingDirectory, recursive: true);
                }
                Directory.CreateDirectory(stagingDirectory);
            }
            catch (Exception ex)
            {
                FGLLog.Error($"Failed to setup staging directory: {stagingDirectory}", ex);
            }
            lock (cacheLock)
            {
                resizedTextureCache.Clear();
            }
        }

        /// <summary>還原快取狀態到上一次的快取對照表與目錄配置。</summary>
        // MA0016: 這裡刻意收下具體的 Dictionary。傳入的必定是
        // GetResizedTextureCacheCopy 產出的私有快照，會直接成為新的內部對照表；
        // 改收唯讀介面只會逼出一次多餘的複製，且無法防止任何實際存在的誤用。
#pragma warning disable MA0016
        public void RestorePreviousCacheState(
            Dictionary<string, string> previousCacheMap,
            string previousCacheDirectory,
            string stagingDirectory)
#pragma warning restore MA0016
        {
            lock (cacheLock) { resizedTextureCache = previousCacheMap; }
            activeCacheDirectory = previousCacheDirectory;
            md5HashCache.Clear();
            try
            {
                if (Directory.Exists(stagingDirectory))
                {
                    Directory.Delete(stagingDirectory, recursive: true);
                }
            }
            catch (Exception ex)
            {
                FGLLog.Warning($"Failed to clean up staging directory: {stagingDirectory} error: {ex.Message}");
            }
        }

        /// <summary>將暫存目錄替換為正式快取目錄，並重建相對路徑對照表。</summary>
        public bool ReplaceTextureCacheDirectory(string stagingDirectory)
        {
            if (string.IsNullOrEmpty(stagingDirectory) || !Directory.Exists(stagingDirectory))
            {
                return false;
            }

            string backupDirectory = CacheDirectory + "_Backup";
            bool movedPreviousCache = false;
            bool movedStagingCache = false;
            try
            {
                if (Directory.Exists(backupDirectory))
                {
                    Directory.Delete(backupDirectory, recursive: true);
                }
                if (Directory.Exists(CacheDirectory))
                {
                    Directory.Move(CacheDirectory, backupDirectory);
                    movedPreviousCache = true;
                }
                Directory.Move(stagingDirectory, CacheDirectory);
                movedStagingCache = true;

                RebuildCacheMapForActiveDirectory();

                if (movedPreviousCache && Directory.Exists(backupDirectory))
                {
                    Directory.Delete(backupDirectory, recursive: true);
                }
                return true;
            }
            catch (Exception ex)
            {
                RollbackPromotion(movedStagingCache, movedPreviousCache, backupDirectory);
                FGLLog.Error("Failed to replace texture cache directory; previous cache was restored.", ex);
                return false;
            }
        }

        /// <summary>
        /// 快取目錄升級成功後，把對照表中的每個快取檔路徑重新指向新的正式目錄，
        /// 並清掉以舊目錄為前綴的 MD5 路徑快取。
        /// </summary>
        private void RebuildCacheMapForActiveDirectory()
        {
            var updatedCacheMap = new Dictionary<string, string>(StringComparer.Ordinal);
            lock (cacheLock)
            {
                foreach (var kvp in resizedTextureCache)
                {
                    updatedCacheMap[kvp.Key] = Path.Combine(CacheDirectory, Path.GetFileName(kvp.Value));
                }
                resizedTextureCache = updatedCacheMap;
            }
            activeCacheDirectory = CacheDirectory;
            md5HashCache.Clear();
        }

        /// <summary>
        /// 升級失敗時的回滾：先移除已就位的暫存目錄，再把備份目錄搬回正式位置。
        /// 回滾本身失敗只記錄，不再向上拋出，避免掩蓋原始失敗原因。
        /// </summary>
        private void RollbackPromotion(bool movedStagingCache, bool movedPreviousCache, string backupDirectory)
        {
            try
            {
                if (movedStagingCache && Directory.Exists(CacheDirectory))
                {
                    Directory.Delete(CacheDirectory, recursive: true);
                }
                if (movedPreviousCache && Directory.Exists(backupDirectory) && !Directory.Exists(CacheDirectory))
                {
                    Directory.Move(backupDirectory, CacheDirectory);
                }
            }
            catch (Exception rollbackEx)
            {
                FGLLog.Error("Failed to restore previous texture cache directory.", rollbackEx);
            }
        }

        /// <summary>提供向內部字典新增項目的執行緒安全介面。</summary>
        public void SetCacheEntry(string originalPath, string cachePath)
        {
            lock (cacheLock)
            {
                resizedTextureCache[originalPath] = cachePath;
            }
        }

        /// <summary>以執行緒安全方式回傳快取對照表的快照副本。</summary>
        // MA0016: 刻意回傳具體的 Dictionary。回傳的是全新的私有副本，
        // 呼叫端修改它不會影響內部狀態；而 RestorePreviousCacheState 需要以它
        // 直接成為新的內部對照表，改回唯讀介面只會逼出一次多餘的複製。
#pragma warning disable MA0016
        public Dictionary<string, string> GetResizedTextureCacheCopy()
#pragma warning restore MA0016
        {
            lock (cacheLock)
            {
                return new Dictionary<string, string>(resizedTextureCache, StringComparer.Ordinal);
            }
        }

        /// <summary>
        /// 清理過期與無效的快取檔案及對照項目。
        /// </summary>
        public void CleanupObsoleteCacheFiles()
        {
            try
            {
                string activeDir = CacheDirectory;
                if (!Directory.Exists(activeDir))
                {
                    return;
                }

                string[] files = Directory.GetFiles(activeDir, "*.png");
                List<KeyValuePair<string, string>> cacheEntries;
                lock (cacheLock)
                {
                    cacheEntries = new List<KeyValuePair<string, string>>(resizedTextureCache);
                }

                var validCacheFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var keysToRemove = TriageCacheEntries(cacheEntries, validCacheFiles, out int deletedObsoleteFiles);

                RemoveCacheEntries(keysToRemove);

                // 刪除未引用檔案前，重新取得最新的有效路徑集合，
                // 防止快照拍攝後由 SetCacheEntry 併發新增的項目被誤刪。
                CollectValidCacheFilePaths(validCacheFiles);

                int deletedUnreferencedFiles = DeleteUnreferencedFiles(files, validCacheFiles);

                if (keysToRemove.Count > 0 || deletedObsoleteFiles > 0 || deletedUnreferencedFiles > 0)
                {
                    FGLLog.Message($"Cache cleanup completed. Removed {keysToRemove.Count.ToString(CultureInfo.InvariantCulture)} obsolete cache map entries, deleted {deletedObsoleteFiles.ToString(CultureInfo.InvariantCulture)} obsolete files and {deletedUnreferencedFiles.ToString(CultureInfo.InvariantCulture)} unreferenced files.");
                }
            }
            catch (Exception ex)
            {
                FGLLog.Error("Error during obsolete cache files cleanup:", ex);
            }
        }

        /// <summary>
        /// 逐一檢視快取對照表的項目，回傳應從對照表移除的鍵，
        /// 並把仍然有效的快取檔路徑收集到 <paramref name="validCacheFiles"/>。
        /// 原始檔已不存在者，其快取檔會就地刪除。
        /// </summary>
        private static List<string> TriageCacheEntries(
            List<KeyValuePair<string, string>> cacheEntries,
            HashSet<string> validCacheFiles,
            out int deletedObsoleteFiles)
        {
            var keysToRemove = new List<string>();
            deletedObsoleteFiles = 0;

            foreach (var entry in cacheEntries)
            {
                if (!SafeFileExists(entry.Key))
                {
                    keysToRemove.Add(entry.Key);
                    try
                    {
                        if (File.Exists(entry.Value))
                        {
                            File.Delete(entry.Value);
                            deletedObsoleteFiles++;
                        }
                    }
                    catch (Exception ex)
                    {
                        FGLLog.Warning($"Failed to delete obsolete cache file {entry.Value}: {ex.Message}");
                    }
                }
                else if (!SafeFileExists(entry.Value))
                {
                    keysToRemove.Add(entry.Key);
                }
                else
                {
                    AddResolvedPath(validCacheFiles, entry.Value);
                }
            }

            return keysToRemove;
        }

        /// <summary>以執行緒安全方式從快取對照表移除指定的鍵。</summary>
        private void RemoveCacheEntries(List<string> keysToRemove)
        {
            if (keysToRemove.Count is 0)
            {
                return;
            }

            lock (cacheLock)
            {
                foreach (var key in keysToRemove)
                {
                    resizedTextureCache.Remove(key);
                }
            }
        }

        /// <summary>把目前對照表中所有快取檔路徑補進有效路徑集合。</summary>
        private void CollectValidCacheFilePaths(HashSet<string> validCacheFiles)
        {
            lock (cacheLock)
            {
                foreach (var kvp in resizedTextureCache)
                {
                    AddResolvedPath(validCacheFiles, kvp.Value);
                }
            }
        }

        /// <summary>刪除快取目錄中未被對照表引用的檔案，回傳實際刪除數量。</summary>
        private static int DeleteUnreferencedFiles(string[] files, HashSet<string> validCacheFiles)
        {
            int deleted = 0;
            foreach (var file in files)
            {
                try
                {
                    string fullPath = Path.GetFullPath(file);
                    if (!validCacheFiles.Contains(fullPath))
                    {
                        File.Delete(file);
                        deleted++;
                    }
                }
                catch (Exception ex)
                {
                    FGLLog.Warning($"Failed to delete unreferenced cache file {file}: {ex.Message}");
                }
            }
            return deleted;
        }

        /// <summary>
        /// 檢查檔案是否存在；路徑無效或權限不足時一律視為「不存在」，
        /// 交由呼叫端把對應的快取項目當作失效處理。
        /// </summary>
        private static bool SafeFileExists(string path)
        {
            try
            {
                return File.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>把路徑正規化後加入集合；無法正規化時退回使用原始字串。</summary>
        private static void AddResolvedPath(HashSet<string> set, string path)
        {
            try
            {
                set.Add(Path.GetFullPath(path));
            }
            catch
            {
                set.Add(path);
            }
        }
    }
}
