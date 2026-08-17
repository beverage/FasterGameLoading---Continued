using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace FasterGameLoading.Tests.TextureDownscaler
{
    /// <summary>
    /// TextureCacheManager 的目錄生命週期測試：暫存目錄建立、升級為正式快取、
    /// 失敗回滾、狀態還原，以及清理流程中各條容錯分支。
    /// 這些路徑在遊戲中只有在使用者按下「降質全部紋理」時才會跑到。
    /// </summary>
    [TestFixture]
    public class TextureCacheManagerLifecycleTests
    {
        private string rootDir;
        private string cacheDir;
        private TextureCacheManager manager;

        [SetUp]
        public void SetUp()
        {
            rootDir = Path.Combine(Path.GetTempPath(), "FGLCacheLifecycle_" + Guid.NewGuid().ToString("N"));
            cacheDir = Path.Combine(rootDir, FGLConsts.TextureCacheDir);
            Directory.CreateDirectory(rootDir);
            manager = new TextureCacheManager(cacheDir);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(rootDir))
                {
                    Directory.Delete(rootDir, recursive: true);
                }
            }
            catch (IOException ex)
            {
                Assert.Fail($"清理測試暫存目錄失敗：{rootDir}\n{ex}");
            }
        }

        // ── 目錄解析 ──

        [Test]
        public void BuildCacheDirectory_WithCustomBase_ResolvesSiblingOfCacheDirectory()
        {
            string staging = manager.BuildCacheDirectory(FGLConsts.TextureCacheStagingDir);

            Assert.That(staging, Is.EqualTo(Path.Combine(rootDir, FGLConsts.TextureCacheStagingDir)));
            Assert.That(manager.CacheDirectory, Is.EqualTo(cacheDir));
        }

        // 註：無自訂根目錄的預設建構式無法在此測試 —— CacheDirectory 會讀
        // GenFilePaths.SaveDataFolderPath，該路徑在 headless 環境下取不到。

        // ── 快取新鮮度 ──

        [Test]
        public void TryGetCachedTexturePath_MissingOriginalFileKeepsCacheEntry()
        {
            string originalPath = Path.Combine(rootDir, "gone.png");
            string cachePath = Path.Combine(rootDir, "gone_cache.png");
            File.WriteAllBytes(cachePath, new byte[] { 1 });
            manager.SetCacheEntry(originalPath, cachePath);

            // 原始檔已不在（例如 Mod 被移除）：不應在此判定為過期，
            // 過期與否交由 CleanupObsoleteCacheFiles 統一處理。
            Assert.That(manager.TryGetCachedTexturePath(originalPath, out string resolved), Is.True);
            Assert.That(resolved, Is.EqualTo(cachePath));
        }

        [Test]
        public void TryGetCachedTexturePath_NewerOriginalWithSameIdentityRefreshesCacheTimestamp()
        {
            Directory.CreateDirectory(cacheDir);
            string originalPath = Path.Combine(rootDir, "same-length.png");
            var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            File.WriteAllBytes(originalPath, new byte[] { 1, 2, 3 });
            File.SetLastWriteTimeUtc(originalPath, baseTime);

            // 快取路徑必須是由目前檔案身分推導出的雜湊路徑，才會命中「內容未變」的快速通道。
            string cachePath = manager.GetCachePath(originalPath);
            File.WriteAllBytes(cachePath, new byte[] { 9 });
            File.SetLastWriteTimeUtc(cachePath, baseTime.AddMinutes(-5));
            manager.SetCacheEntry(originalPath, cachePath);

            Assert.That(manager.TryGetCachedTexturePath(originalPath, out string resolved), Is.True);
            Assert.That(resolved, Is.EqualTo(cachePath));
            Assert.That(File.GetLastWriteTimeUtc(cachePath), Is.EqualTo(baseTime),
                "檔案身分未變時應把快取檔的時間戳對齊原始檔，而不是重新降質一次。");
        }

        // ── 對照表操作 ──

        [Test]
        public void RemoveCachedTexturePath_DropsSingleEntry()
        {
            manager.SetCacheEntry(Path.Combine(rootDir, "a.png"), Path.Combine(rootDir, "a_cache.png"));
            manager.SetCacheEntry(Path.Combine(rootDir, "b.png"), Path.Combine(rootDir, "b_cache.png"));

            manager.RemoveCachedTexturePath(Path.Combine(rootDir, "a.png"));

            Assert.That(manager.CacheCount, Is.EqualTo(1));
            Assert.That(manager.ResizedTextureCache.ContainsKey(Path.Combine(rootDir, "b.png")), Is.True);
        }

        [Test]
        public void GetResizedTextureCacheCopy_ReturnsDetachedSnapshot()
        {
            string originalPath = Path.Combine(rootDir, "snap.png");
            manager.SetCacheEntry(originalPath, Path.Combine(rootDir, "snap_cache.png"));

            var snapshot = manager.GetResizedTextureCacheCopy();
            snapshot["injected"] = "value";

            Assert.That(manager.CacheCount, Is.EqualTo(1));
            Assert.That(manager.ResizedTextureCache.ContainsKey("injected"), Is.False);
        }

        // ── 暫存目錄與還原 ──

        [Test]
        public void SetupResizeStagingDirectory_RecreatesDirectoryAndClearsMap()
        {
            string staging = manager.BuildCacheDirectory(FGLConsts.TextureCacheStagingDir);
            Directory.CreateDirectory(staging);
            File.WriteAllBytes(Path.Combine(staging, "leftover.png"), new byte[] { 1 });
            manager.SetCacheEntry(Path.Combine(rootDir, "x.png"), Path.Combine(staging, "x_cache.png"));

            manager.SetupResizeStagingDirectory(staging);

            Assert.That(Directory.Exists(staging), Is.True);
            Assert.That(Directory.GetFiles(staging), Is.Empty, "上一輪殘留的暫存檔必須清空，否則會被誤認為本輪產物而升級為正式快取。");
            Assert.That(manager.CacheCount, Is.Zero);
        }

        [Test]
        public void RestorePreviousCacheState_RestoresMapAndDeletesStaging()
        {
            string staging = manager.BuildCacheDirectory(FGLConsts.TextureCacheStagingDir);
            string originalPath = Path.Combine(rootDir, "keep.png");
            manager.SetCacheEntry(originalPath, Path.Combine(cacheDir, "keep_cache.png"));
            var previous = manager.GetResizedTextureCacheCopy();

            manager.SetupResizeStagingDirectory(staging);
            Assert.That(manager.CacheCount, Is.Zero);

            manager.RestorePreviousCacheState(previous, cacheDir, staging);

            Assert.That(manager.CacheCount, Is.EqualTo(1));
            Assert.That(manager.ResizedTextureCache.ContainsKey(originalPath), Is.True);
            Assert.That(Directory.Exists(staging), Is.False);
            Assert.That(manager.GetCachePath(originalPath),
                Does.StartWith(cacheDir), "還原後新算出的快取路徑必須回到正式目錄，不能再指向已刪除的暫存目錄。");
        }

        // ── 升級與回滾 ──

        [Test]
        public void ReplaceTextureCacheDirectory_PromotesStagingAndRepointsCacheMap()
        {
            string staging = manager.BuildCacheDirectory(FGLConsts.TextureCacheStagingDir);
            Directory.CreateDirectory(cacheDir);
            File.WriteAllBytes(Path.Combine(cacheDir, "old.png"), new byte[] { 1 });
            Directory.CreateDirectory(staging);
            File.WriteAllBytes(Path.Combine(staging, "new_cache.png"), new byte[] { 2 });

            string originalPath = Path.Combine(rootDir, "promote.png");
            manager.SetCacheEntry(originalPath, Path.Combine(staging, "new_cache.png"));

            bool replaced = manager.ReplaceTextureCacheDirectory(staging);

            Assert.That(replaced, Is.True);
            Assert.That(Directory.Exists(staging), Is.False);
            Assert.That(Directory.Exists(cacheDir + "_Backup"), Is.False, "升級成功後備份目錄必須清掉，否則快取空間會翻倍。");
            Assert.That(File.Exists(Path.Combine(cacheDir, "new_cache.png")), Is.True);
            Assert.That(File.Exists(Path.Combine(cacheDir, "old.png")), Is.False);
            Assert.That(manager.ResizedTextureCache[originalPath],
                Is.EqualTo(Path.Combine(cacheDir, "new_cache.png")),
                "對照表必須改指向正式目錄，否則升級後每一筆快取都會查無檔案。");
        }

        [Test]
        public void ReplaceTextureCacheDirectory_EmptyStagingPathIsRejected()
        {
            Assert.That(manager.ReplaceTextureCacheDirectory(string.Empty), Is.False);
        }

        [Test]
        public void ReplaceTextureCacheDirectory_LockedCacheDirectoryFailsAndKeepsExistingCache()
        {
            string staging = manager.BuildCacheDirectory(FGLConsts.TextureCacheStagingDir);
            Directory.CreateDirectory(cacheDir);
            Directory.CreateDirectory(staging);
            File.WriteAllBytes(Path.Combine(staging, "new_cache.png"), new byte[] { 2 });

            string lockedFile = Path.Combine(cacheDir, "locked.png");
            File.WriteAllBytes(lockedFile, new byte[] { 1 });

            bool replaced;
            using (var handle = new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                // 開啟中的檔案會讓 Directory.Move 失敗，觸發升級流程的回滾分支。
                replaced = manager.ReplaceTextureCacheDirectory(staging);
            }

            Assert.That(replaced, Is.False);
            Assert.That(File.Exists(lockedFile), Is.True, "升級失敗後原本的快取目錄必須原封不動。");
        }

        [Test]
        public void ReplaceTextureCacheDirectory_LockedStagingRollsBackPreviousCacheFromBackup()
        {
            string staging = manager.BuildCacheDirectory(FGLConsts.TextureCacheStagingDir);
            string backupDir = cacheDir + "_Backup";

            Directory.CreateDirectory(cacheDir);
            string survivor = Path.Combine(cacheDir, "survivor.png");
            File.WriteAllBytes(survivor, new byte[] { 1 });

            // 上一輪中斷留下的備份目錄：升級流程必須先把它清掉才動手。
            Directory.CreateDirectory(backupDir);
            File.WriteAllBytes(Path.Combine(backupDir, "stale_backup.png"), new byte[] { 9 });

            Directory.CreateDirectory(staging);
            string lockedStagingFile = Path.Combine(staging, "locked.png");
            File.WriteAllBytes(lockedStagingFile, new byte[] { 2 });

            bool replaced;
            using (var handle = new FileStream(lockedStagingFile, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                // 正式目錄已搬去備份、暫存目錄卻搬不動：必須把備份搬回原位，
                // 否則使用者會在一次失敗的降質後完全失去既有快取。
                replaced = manager.ReplaceTextureCacheDirectory(staging);
            }

            Assert.That(replaced, Is.False);
            Assert.That(Directory.Exists(backupDir), Is.False);
            Assert.That(File.Exists(survivor), Is.True, "回滾後原本的快取檔必須回到正式目錄。");
        }

        // ── 清理流程的容錯分支 ──

        [Test]
        public void CleanupObsoleteCacheFiles_MissingCacheDirectoryIsNoOp()
        {
            manager.SetCacheEntry(Path.Combine(rootDir, "a.png"), Path.Combine(cacheDir, "a_cache.png"));

            Assert.That(Directory.Exists(cacheDir), Is.False);
            manager.CleanupObsoleteCacheFiles();

            Assert.That(manager.CacheCount, Is.EqualTo(1), "快取目錄還沒建立時不該動到對照表。");
        }

        [Test]
        public void CleanupObsoleteCacheFiles_DropsEntryWhoseCacheFileVanished()
        {
            Directory.CreateDirectory(cacheDir);
            string originalPath = Path.Combine(rootDir, "present.png");
            File.WriteAllBytes(originalPath, new byte[] { 1 });
            manager.SetCacheEntry(originalPath, Path.Combine(cacheDir, "never_written.png"));

            manager.CleanupObsoleteCacheFiles();

            Assert.That(manager.CacheCount, Is.Zero);
        }

        [Test]
        public void CleanupObsoleteCacheFiles_AllEntriesValidLeavesEverythingInPlace()
        {
            Directory.CreateDirectory(cacheDir);
            string originalPath = Path.Combine(rootDir, "valid.png");
            string cachePath = Path.Combine(cacheDir, "valid_cache.png");
            File.WriteAllBytes(originalPath, new byte[] { 1 });
            File.WriteAllBytes(cachePath, new byte[] { 2 });
            manager.SetCacheEntry(originalPath, cachePath);

            manager.CleanupObsoleteCacheFiles();

            Assert.That(manager.CacheCount, Is.EqualTo(1));
            Assert.That(File.Exists(cachePath), Is.True);
        }

        [Test]
        public void CleanupObsoleteCacheFiles_UndeletableUnreferencedFileIsSkipped()
        {
            Directory.CreateDirectory(cacheDir);
            string strayPath = Path.Combine(cacheDir, "stray.png");
            File.WriteAllBytes(strayPath, new byte[] { 3 });

            using (var handle = new FileStream(strayPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                // 檔案被佔用時刪除會失敗；清理流程必須容錯而非整批中止。
                Assert.DoesNotThrow(manager.CleanupObsoleteCacheFiles);
            }

            Assert.That(File.Exists(strayPath), Is.True);
        }

        [Test]
        public void CleanupObsoleteCacheFiles_UndeletableObsoleteFileStillDropsMapEntry()
        {
            Directory.CreateDirectory(cacheDir);
            string originalPath = Path.Combine(rootDir, "deleted-source.png"); // 刻意不建立
            string cachePath = Path.Combine(cacheDir, "orphan_cache.png");
            File.WriteAllBytes(cachePath, new byte[] { 4 });
            manager.SetCacheEntry(originalPath, cachePath);

            using (var handle = new FileStream(cachePath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                manager.CleanupObsoleteCacheFiles();
            }

            Assert.That(manager.CacheCount, Is.Zero,
                "即使快取檔刪不掉，對照表中指向已消失原始檔的項目仍必須移除。");
            Assert.That(File.Exists(cachePath), Is.True);
        }

        [Test]
        public void CleanupObsoleteCacheFiles_EntriesAddedAfterSnapshotAreNotDeleted()
        {
            Directory.CreateDirectory(cacheDir);
            string originalPath = Path.Combine(rootDir, "late.png");
            string cachePath = Path.Combine(cacheDir, "late_cache.png");
            File.WriteAllBytes(originalPath, new byte[] { 1 });
            File.WriteAllBytes(cachePath, new byte[] { 2 });

            var beforeCleanup = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [originalPath] = cachePath,
            };
            manager.RestorePreviousCacheState(beforeCleanup, cacheDir, Path.Combine(rootDir, "no-such-staging"));

            manager.CleanupObsoleteCacheFiles();

            Assert.That(File.Exists(cachePath), Is.True);
            Assert.That(manager.CacheCount, Is.EqualTo(1));
        }
    }
}
