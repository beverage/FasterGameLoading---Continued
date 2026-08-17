using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using RimWorld.IO;
using UnityEngine;
using Verse;

namespace FasterGameLoading.Tests.TextureDownscaler
{
    /// <summary>
    /// ModContentLoaderTexture2D_LoadTexture_Patch 的 headless 測試。
    /// 主執行緒重導向佇列、WeakReference 快取、降質快取回退與 session 記錄
    /// 四條路徑在真實遊戲中都只在啟動期跑到，這裡以反射與 Harmony stub 重建其前置狀態。
    /// </summary>
    [TestFixture]
    public class ModContentLoaderTexture2D_LoadTexture_PatchTests
    {
        private static readonly Type PatchType = typeof(ModContentLoaderTexture2D_LoadTexture_Patch);

        private string tempDir;
        private FasterGameLoadingMod originalInstance;
        private bool originalStaticAtlasesBaking;
        private bool originalVerboseLogging;
        private bool? originalImageOptActive;
        private bool? originalGraphicsSettingsActive;

        /// <summary>不觸碰檔案系統的 VirtualFile 替身，只需要 FullPath 可讀。</summary>
        private sealed class FakeVirtualFile : VirtualFile
        {
            private readonly string path;

            public FakeVirtualFile(string path)
            {
                this.path = path;
            }

            public override string Name => Path.GetFileName(path);
            public override string FullPath => path;
            public override bool Exists => true;
            public override Stream CreateReadStream() => new MemoryStream(Array.Empty<byte>());
            public override byte[] ReadAllBytes() => Array.Empty<byte>();
            public override string ReadAllText() => string.Empty;
            public override string[] ReadAllLines() => Array.Empty<string>();
            public override long Length => 0L;
        }

        private static Texture2D NewDetachedTexture()
        {
            return (Texture2D)FormatterServices.GetUninitializedObject(typeof(Texture2D));
        }

        [SetUp]
        public void SetUp()
        {
            tempDir = Path.Combine(Path.GetTempPath(), "FGLLoadTexTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            originalStaticAtlasesBaking = FasterGameLoadingSettings.StaticAtlasesBaking;
            originalVerboseLogging = FasterGameLoadingSettings.VerboseLogging;
            originalInstance = FasterGameLoadingMod.Instance;
            originalImageOptActive = GetCompatFlag(typeof(ImageOptCompat));
            originalGraphicsSettingsActive = GetCompatFlag(typeof(GraphicsSettingsCompat));

            // 兩個相容性旗標一律固定為「未啟用」，否則 Prefix 會在第一個分支就短路。
            SetCompatFlag(typeof(ImageOptCompat), value: false);
            SetCompatFlag(typeof(GraphicsSettingsCompat), value: false);
            FasterGameLoadingSettings.StaticAtlasesBaking = false;
            FasterGameLoadingSettings.VerboseLogging = false;

            SetModInstance(CreateModWithCacheManager(new TextureCacheManager(tempDir)));

            ClearPatchState();

        }

        [TearDown]
        public void TearDown()
        {
            TestSetup.IsInMainThreadOverride = null;
            DrainQueueSilently();
            ClearPatchState();
            ResetBakingSkipListState();

            SetModInstance(originalInstance);
            FasterGameLoadingSettings.StaticAtlasesBaking = originalStaticAtlasesBaking;
            FasterGameLoadingSettings.VerboseLogging = originalVerboseLogging;
            SetCompatFlag(typeof(ImageOptCompat), originalImageOptActive);
            SetCompatFlag(typeof(GraphicsSettingsCompat), originalGraphicsSettingsActive);

            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch (IOException)
            {
                // 背景預載入工作可能仍持有檔案控制代碼；殘留暫存目錄不影響斷言結果。
            }
        }

        // ── 主執行緒重導向佇列 ──

        [Test]
        public void ProcessPendingMainThreadRequests_CapturesLoaderExceptionAndStillSignals()
        {
            // headless 環境沒有 Unity 圖形裝置，真正的 LoadTexture 一定失敗；
            // 這正是要驗證的情境：失敗也必須喚醒等待端。
            var request = EnqueueRequest(new FakeVirtualFile(Path.Combine(tempDir, "b.png")));

            ModContentLoaderTexture2D_LoadTexture_Patch.ProcessPendingMainThreadRequests();

            Assert.That(GetRequestField(request, "Result"), Is.Null);
            Assert.That(GetRequestField(request, "Exception"), Is.Not.Null);
            Assert.That(IsCompleted(request), Is.True, "即使載入失敗也必須喚醒等待端，否則呼叫端會空等到逾時。");
        }

        [Test]
        public void ProcessPendingMainThreadRequests_SkipsCancelledRequestWithoutInvokingLoader()
        {
            var request = EnqueueRequest(new FakeVirtualFile(Path.Combine(tempDir, "c.png")));
            CancelRequest(request);

            ModContentLoaderTexture2D_LoadTexture_Patch.ProcessPendingMainThreadRequests();

            // 若已取消的請求仍被送進載入器，Exception 欄位會被填上。
            Assert.That(GetRequestField(request, "Result"), Is.Null);
            Assert.That(GetRequestField(request, "Exception"), Is.Null);
            Assert.That(IsCompleted(request), Is.False);
        }

        [Test]
        public void TryDrainMainThreadRequests_DrainsQueue()
        {
            var request = EnqueueRequest(new FakeVirtualFile(Path.Combine(tempDir, "d.png")));

            ModContentLoaderTexture2D_LoadTexture_Patch.TryDrainMainThreadRequests();

            Assert.That(IsCompleted(request), Is.True);
            Assert.That(QueueCount(), Is.Zero);
        }

        [Test]
        public void TryDrainMainThreadRequests_IsReentrancySafe()
        {
            var drainingField = PatchType.GetField("_draining", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(drainingField, Is.Not.Null);

            EnqueueRequest(new FakeVirtualFile(Path.Combine(tempDir, "e.png")));

            drainingField.SetValue(obj: null, value: true);
            try
            {
                ModContentLoaderTexture2D_LoadTexture_Patch.TryDrainMainThreadRequests();
                Assert.That(QueueCount(), Is.EqualTo(1), "已在泵送中時再次呼叫必須直接返回，不得遞迴消費佇列。");
            }
            finally
            {
                drainingField.SetValue(obj: null, value: false);
            }
        }

        // ── Prefix：非主執行緒重導向 ──

        [Test]
        public void Prefix_OffMainThread_ReturnsTextureSuppliedByMainThreadPump()
        {
            var expected = NewDetachedTexture();
            TestSetup.IsInMainThreadOverride = () => false;

            // 以反射直接完成佇列中的請求，模擬 DelayedActions.Update() 在主執行緒泵送成功的情形。
            // （headless 下真正的 LoadTexture 必然失敗，無法用來測成功路徑。）
            var pump = Task.Run(() => CompleteFirstQueuedRequest(expected, deadlineMs: 900));

            Texture2D result = null;
            bool runOriginal = ModContentLoaderTexture2D_LoadTexture_Patch.Prefix(
                new FakeVirtualFile(Path.Combine(tempDir, "f.png")), out bool state, ref result);

            pump.Wait(2000);

            Assert.That(runOriginal, Is.False);
            Assert.That(state, Is.False);
            Assert.That(result, Is.SameAs(expected));
        }

        [Test]
        public void Prefix_OffMainThread_ReturnsNullWhenPumpReportsLoadFailure()
        {
            TestSetup.IsInMainThreadOverride = () => false;

            var pump = Task.Run(() =>
            {
                SpinWait.SpinUntil(() => QueueCount() > 0, TimeSpan.FromMilliseconds(900));
                ModContentLoaderTexture2D_LoadTexture_Patch.ProcessPendingMainThreadRequests();
            });

            Texture2D result = NewDetachedTexture();
            bool runOriginal = ModContentLoaderTexture2D_LoadTexture_Patch.Prefix(
                new FakeVirtualFile(Path.Combine(tempDir, "h.png")), out bool state, ref result);

            pump.Wait(2000);

            Assert.That(runOriginal, Is.False);
            Assert.That(state, Is.False);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void Prefix_OffMainThread_ReturnsNullWhenPumpNeverRuns()
        {
            TestSetup.IsInMainThreadOverride = () => false;

            Texture2D result = NewDetachedTexture();
            bool runOriginal = ModContentLoaderTexture2D_LoadTexture_Patch.Prefix(
                new FakeVirtualFile(Path.Combine(tempDir, "g.png")), out bool state, ref result);

            Assert.That(runOriginal, Is.False, "逾時後必須跳過原始方法，不得讓背景執行緒去碰 Unity 資源 API。");
            Assert.That(state, Is.False);
            Assert.That(result, Is.Null);
        }

        // ── Prefix：相容性旗標的短路分支 ──

        [Test]
        public void Prefix_WhenImageOptActive_DefersToOriginalLoader()
        {
            SetCompatFlag(typeof(ImageOptCompat), value: true);

            Texture2D result = null;
            bool runOriginal = ModContentLoaderTexture2D_LoadTexture_Patch.Prefix(
                new FakeVirtualFile(Path.Combine(tempDir, "Textures", "imageopt.png")), out bool state, ref result);

            Assert.That(runOriginal, Is.True);
            Assert.That(state, Is.False, "__state 必須為 false，否則 Postfix 會把 ImageOpt 載入的紋理誤登記為 FGL 的快取。");
            Assert.That(ModContentLoaderTexture2D_LoadTexture_Patch.loadedTexturesThisSession, Is.Empty);
        }

        [Test]
        public void Prefix_WhenGraphicsSettingsActive_DefersToOriginalLoader()
        {
            SetCompatFlag(typeof(GraphicsSettingsCompat), value: true);

            Texture2D result = null;
            bool runOriginal = ModContentLoaderTexture2D_LoadTexture_Patch.Prefix(
                new FakeVirtualFile(Path.Combine(tempDir, "Textures", "gsplus.png")), out bool state, ref result);

            Assert.That(runOriginal, Is.True);
            Assert.That(state, Is.False);
            Assert.That(ModContentLoaderTexture2D_LoadTexture_Patch.loadedTexturesThisSession, Is.Empty);
        }

        // ── session 載入清單 ──

        [Test]
        public void RecordTextureForSession_KeysEntryByTexturesRelativePath()
        {
            string fullPath = Path.Combine(tempDir, "Textures", "Things", "wall.png");

            InvokeRecordTextureForSession(fullPath);

            string expectedKey = fullPath.Substring(
                fullPath.Replace('\\', '/').IndexOf(FGLConsts.TexturesDirSlash, StringComparison.Ordinal));
            Assert.That(
                ModContentLoaderTexture2D_LoadTexture_Patch.loadedTexturesThisSession.TryGetValue(expectedKey, out var stored),
                Is.True);
            Assert.That(stored, Is.EqualTo(fullPath));
        }

        [Test]
        public void RecordTextureForSession_IgnoresPathsOutsideTexturesFolder()
        {
            InvokeRecordTextureForSession(Path.Combine(tempDir, "Sounds", "beep.png"));

            Assert.That(ModContentLoaderTexture2D_LoadTexture_Patch.loadedTexturesThisSession, Is.Empty);
        }

        // ── Prefix：WeakReference 快取命中 ──

        [Test]
        public void Prefix_ServesFromWeakReferenceCacheWithoutRunningOriginal()
        {
            string fullPath = Path.Combine(tempDir, "Textures", "cached.png");
            var cached = NewDetachedTexture();
            ModContentLoaderTexture2D_LoadTexture_Patch.savedTextures[fullPath] =
                new System.WeakReference<Texture2D>(cached);

            Texture2D result = null;
            bool runOriginal = ModContentLoaderTexture2D_LoadTexture_Patch.Prefix(
                new FakeVirtualFile(fullPath), out bool state, ref result);

            Assert.That(runOriginal, Is.False);
            Assert.That(state, Is.False);
            Assert.That(result, Is.SameAs(cached));
        }

        // 註：Prefix 的降質快取分支（TryServeFromDownscaleCache）在此環境下無法測試。
        // 該方法本體含 Texture2D 建構式、LoadImage、Compress、Apply 等 Unity ECall，
        // 而測試環境已由 TestSetup 掛上 MonoMod 的 JIT hook；JIT 該方法時 CLR 會拋出
        // 「ECall methods must be packaged into a system module」而非在呼叫時才失敗，
        // 因此連「進入方法後立刻回傳 false」的路徑也無法執行。

        // ── 路徑登記與烘焙排除 ──

        [Test]
        public void SaveTexturePath_RebindingSameTextureUpdatesReverseLookup()
        {
            string firstPath = Path.Combine(tempDir, "first.png");
            string secondPath = Path.Combine(tempDir, "second.png");
            var texture = NewDetachedTexture();

            InvokeSaveTexturePath(firstPath, texture);
            InvokeSaveTexturePath(secondPath, texture);

            Assert.That(
                ModContentLoaderTexture2D_LoadTexture_Patch.TryGetSavedTexturePath(texture, out string resolved),
                Is.True);
            Assert.That(resolved, Is.EqualTo(secondPath),
                "同一 Texture2D 換路徑重新登記後，反向查表必須指向新路徑。");
        }

        [Test]
        public void SaveTexturePath_ReplacingTextureAtSamePathDetachesOldEntry()
        {
            string fullPath = Path.Combine(tempDir, "same.png");
            var oldTexture = NewDetachedTexture();
            var newTexture = NewDetachedTexture();

            InvokeSaveTexturePath(fullPath, oldTexture);
            InvokeSaveTexturePath(fullPath, newTexture);

            Assert.That(
                ModContentLoaderTexture2D_LoadTexture_Patch.TryGetSavedTexturePath(oldTexture, out _),
                Is.False);
            Assert.That(
                ModContentLoaderTexture2D_LoadTexture_Patch.TryGetSavedTexturePath(newTexture, out string resolved),
                Is.True);
            Assert.That(resolved, Is.EqualTo(fullPath));
        }

        [Test]
        public void TryGetSavedTexturePath_UnknownTextureReturnsFalse()
        {
            Assert.That(
                ModContentLoaderTexture2D_LoadTexture_Patch.TryGetSavedTexturePath(NewDetachedTexture(), out string resolved),
                Is.False);
            Assert.That(resolved, Is.Null);
        }

        [Test]
        public void RegisterSkippedBakingTextureIfApplicable_RegistersInstanceAndFileName()
        {
            string modRoot = Path.Combine(tempDir, "TargetMod").Replace('\\', '/');
            SeedBakingSkipListRoot(modRoot);
            FasterGameLoadingSettings.StaticAtlasesBaking = true;

            string texturePath = modRoot + "/Textures/Alien/head.png";
            var texture = NewDetachedTexture();

            ModContentLoaderTexture2D_LoadTexture_Patch.RegisterSkippedBakingTextureIfApplicable(texturePath, texture);

            Assert.That(ModContentLoaderTexture2D_LoadTexture_Patch.skippedBakingTextures.ContainsKey(texture), Is.True);
            Assert.That(ModContentLoaderTexture2D_LoadTexture_Patch.skippedBakingTextureNames.ContainsKey("head"), Is.True);
        }

        [Test]
        public void RegisterSkippedBakingTextureIfApplicable_IgnoresPathsOutsideTargetMods()
        {
            string modRoot = Path.Combine(tempDir, "TargetMod").Replace('\\', '/');
            SeedBakingSkipListRoot(modRoot);
            FasterGameLoadingSettings.StaticAtlasesBaking = true;

            var texture = NewDetachedTexture();
            ModContentLoaderTexture2D_LoadTexture_Patch.RegisterSkippedBakingTextureIfApplicable(
                Path.Combine(tempDir, "OtherMod", "Textures", "rock.png"), texture);

            Assert.That(ModContentLoaderTexture2D_LoadTexture_Patch.skippedBakingTextures, Is.Empty);
            Assert.That(ModContentLoaderTexture2D_LoadTexture_Patch.skippedBakingTextureNames, Is.Empty);
        }

        // ── Postfix ──

        [Test]
        public void Postfix_SavesPathWhenOriginalLoaderRan()
        {
            string fullPath = Path.Combine(tempDir, "Textures", "post.png");
            var texture = NewDetachedTexture();

            ModContentLoaderTexture2D_LoadTexture_Patch.Postfix(
                new FakeVirtualFile(fullPath), __state: true, texture);

            Assert.That(
                ModContentLoaderTexture2D_LoadTexture_Patch.TryGetSavedTexturePath(texture, out string resolved),
                Is.True);
            Assert.That(resolved, Is.EqualTo(fullPath));
        }

        [Test]
        public void Postfix_DoesNotSavePathWhenPrefixAlreadyServedTexture()
        {
            string fullPath = Path.Combine(tempDir, "Textures", "served.png");
            var texture = NewDetachedTexture();

            ModContentLoaderTexture2D_LoadTexture_Patch.Postfix(
                new FakeVirtualFile(fullPath), __state: false, texture);

            Assert.That(
                ModContentLoaderTexture2D_LoadTexture_Patch.TryGetSavedTexturePath(texture, out _),
                Is.False);
        }

        [Test]
        public void Postfix_SkipsProtectedModTexturePath()
        {
            string modRoot = Path.Combine(tempDir, "TargetMod").Replace('\\', '/');
            SeedBakingSkipListRoot(modRoot);
            string fullPath = modRoot + "/Textures/Alien/body.png";
            var texture = NewDetachedTexture();

            ModContentLoaderTexture2D_LoadTexture_Patch.Postfix(
                new FakeVirtualFile(fullPath), __state: true, texture);

            Assert.That(
                ModContentLoaderTexture2D_LoadTexture_Patch.TryGetSavedTexturePath(texture, out _),
                Is.False,
                "排除烘焙的 Mod 紋理不得進入 WeakReference 快取，否則下次載入會沿用同一實體而繞過排除判定。");
        }

        [Test]
        public void Postfix_NullResultIsIgnored()
        {
            Assert.DoesNotThrow(() => ModContentLoaderTexture2D_LoadTexture_Patch.Postfix(
                new FakeVirtualFile(Path.Combine(tempDir, "none.png")), __state: true, __result: null));
        }

        // ── 背景預載入 ──

        [Test]
        public void StartPreloadCachedTextures_ReadsCacheFilesIntoMemory()
        {
            string originalPath = Path.Combine(tempDir, "Textures", "pre.png");
            string cachePath = Path.Combine(tempDir, "pre_cache.png");
            var payload = new byte[] { 9, 8, 7, 6 };
            File.WriteAllBytes(cachePath, payload);

            FasterGameLoadingMod.Instance.CacheManager.SetCacheEntry(originalPath, cachePath);

            ModContentLoaderTexture2D_LoadTexture_Patch.StartPreloadCachedTextures();

            // 預載入刻意延遲 TexturePreloadDelayMs 才開始，避免與啟動期 XML I/O 爭頻寬。
            SpinWait.SpinUntil(
                () => ModContentLoaderTexture2D_LoadTexture_Patch.preloadedCacheBytes.ContainsKey(cachePath),
                TimeSpan.FromSeconds(5));

            Assert.That(
                ModContentLoaderTexture2D_LoadTexture_Patch.preloadedCacheBytes.TryGetValue(cachePath, out var loaded),
                Is.True);
            Assert.That(loaded, Is.EqualTo(payload));
        }

        [Test]
        public void StartPreloadCachedTextures_EmptyCacheStartsNoBackgroundWork()
        {
            ModContentLoaderTexture2D_LoadTexture_Patch.preloadedCacheBytes["stale"] = new byte[] { 1 };

            ModContentLoaderTexture2D_LoadTexture_Patch.StartPreloadCachedTextures();

            Assert.That(ModContentLoaderTexture2D_LoadTexture_Patch.preloadedCacheBytes, Is.Empty,
                "即使沒有快取項目也必須先清空舊的預載入位元組，避免沿用上個 session 的內容。");
        }

        [Test]
        public void StartPreloadCachedTextures_WithoutModInstanceDoesNotThrow()
        {
            SetModInstance(null);

            Assert.DoesNotThrow(ModContentLoaderTexture2D_LoadTexture_Patch.StartPreloadCachedTextures);
        }

        // ── 測試輔助 ──

        /// <summary>
        /// 取出佇列中的第一個請求，直接填入指定紋理並喚醒等待端。
        /// 這是 DelayedActions.Update() 泵送成功時的等效行為，但不經過需要 Unity 圖形裝置的載入器。
        /// </summary>
        private static bool CompleteFirstQueuedRequest(Texture2D result, int deadlineMs)
        {
            var queue = GetQueue();
            var tryDequeue = queue.GetType().GetMethod("TryDequeue");
            var args = new object[] { null };

            if (!SpinWait.SpinUntil(() => (bool)tryDequeue.Invoke(queue, args), deadlineMs))
            {
                return false;
            }

            var request = args[0];
            request.GetType().GetField("Result").SetValue(request, result);
            ((ManualResetEventSlim)GetRequestField(request, "CompletedEvent")).Set();
            return true;
        }

        private static object GetQueue()
        {
            return PatchType.GetField("mainThreadLoadRequests", BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null);
        }

        private static int QueueCount()
        {
            var queue = GetQueue();
            return (int)queue.GetType().GetProperty("Count").GetValue(queue);
        }

        private static object EnqueueRequest(VirtualFile file)
        {
            var requestType = PatchType.GetNestedType("LoadRequest", BindingFlags.NonPublic);
            var request = Activator.CreateInstance(requestType, nonPublic: true);
            requestType.GetField("File").SetValue(request, file);

            var queue = GetQueue();
            queue.GetType().GetMethod("Enqueue").Invoke(queue, new object[] { request });
            return request;
        }

        private static object GetRequestField(object request, string name)
        {
            return request.GetType().GetField(name).GetValue(request);
        }

        private static bool IsCompleted(object request)
        {
            var completedEvent = (ManualResetEventSlim)GetRequestField(request, "CompletedEvent");
            return completedEvent.IsSet;
        }

        private static void CancelRequest(object request)
        {
            request.GetType().GetMethod("Cancel").Invoke(request, parameters: null);
        }

        private static void DrainQueueSilently()
        {
            var queue = GetQueue();
            var tryDequeue = queue.GetType().GetMethod("TryDequeue");
            var args = new object[] { null };
            while ((bool)tryDequeue.Invoke(queue, args))
            {
                args[0] = null;
            }
        }

        private static void InvokeRecordTextureForSession(string fullPath)
        {
            PatchType.GetMethod("RecordTextureForSession", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(obj: null, new object[] { fullPath });
        }

        private static void InvokeSaveTexturePath(string fullPath, Texture2D texture)
        {
            PatchType.GetMethod("SaveTexturePath", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(obj: null, new object[] { fullPath, texture });
        }

        private static void ClearPatchState()
        {
            ModContentLoaderTexture2D_LoadTexture_Patch.savedTextures.Clear();
            ModContentLoaderTexture2D_LoadTexture_Patch.loadedTexturesThisSession.Clear();
            ModContentLoaderTexture2D_LoadTexture_Patch.preloadedCacheBytes.Clear();
            ModContentLoaderTexture2D_LoadTexture_Patch.skippedBakingTextures.Clear();
            ModContentLoaderTexture2D_LoadTexture_Patch.skippedBakingTextureNames.Clear();
        }

        /// <summary>直接把根目錄塞進排除名單並鎖定初始化旗標，繞過需要 RunningMods 的探測流程。</summary>
        private static void SeedBakingSkipListRoot(string root)
        {
            var roots = (HashSet<string>)typeof(AdaptiveBakingSkipList)
                .GetField("targetModRoots", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            roots.Clear();
            roots.Add(root);
            typeof(AdaptiveBakingSkipList)
                .GetField("rootsInitialized", BindingFlags.NonPublic | BindingFlags.Static)
                .SetValue(obj: null, value: true);
        }

        private static void ResetBakingSkipListState()
        {
            var roots = (HashSet<string>)typeof(AdaptiveBakingSkipList)
                .GetField("targetModRoots", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            roots.Clear();
            typeof(AdaptiveBakingSkipList)
                .GetField("rootsInitialized", BindingFlags.NonPublic | BindingFlags.Static)
                .SetValue(obj: null, value: false);
        }

        private static bool? GetCompatFlag(Type compatType)
        {
            return (bool?)compatType.GetField("isActive", BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null);
        }

        private static void SetCompatFlag(Type compatType, bool? value)
        {
            compatType.GetField("isActive", BindingFlags.NonPublic | BindingFlags.Static)
                .SetValue(obj: null, value: value);
        }

        private static FasterGameLoadingMod CreateModWithCacheManager(TextureCacheManager cacheManager)
        {
            // Mod 的建構式會建立 GameObject 並套用 Harmony，headless 下無法執行；
            // 這裡直接取得未初始化實體，只補上測試需要的 CacheManager。
            var mod = (FasterGameLoadingMod)FormatterServices.GetUninitializedObject(typeof(FasterGameLoadingMod));
            typeof(FasterGameLoadingMod)
                .GetProperty(nameof(FasterGameLoadingMod.CacheManager))
                .SetValue(mod, cacheManager);
            return mod;
        }

        private static void SetModInstance(FasterGameLoadingMod mod)
        {
            typeof(FasterGameLoadingMod)
                .GetProperty(nameof(FasterGameLoadingMod.Instance), BindingFlags.Public | BindingFlags.Static)
                .SetValue(obj: null, value: mod);
        }
    }
}
