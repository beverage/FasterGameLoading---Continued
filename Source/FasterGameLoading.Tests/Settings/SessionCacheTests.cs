using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using HarmonyLib;
using NUnit.Framework;
using Verse;

namespace FasterGameLoading.Tests.Settings
{
    [TestFixture]
    public class SessionCacheTests
    {
        private static Harmony harmony;
        private static List<ModMetaData> mockActiveMods = new();

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            harmony = new Harmony("FasterGameLoading.Tests.Settings.SessionCacheTests");

            try
            {
                var modsConfigType = AccessTools.TypeByName("Verse.ModsConfig");
                var activeModsGetter = modsConfigType != null
                    ? AccessTools.PropertyGetter(modsConfigType, "ActiveModsInLoadOrder")
                    : null;
                if (activeModsGetter != null)
                {
                    harmony.Patch(activeModsGetter, prefix: new HarmonyMethod(AccessTools.Method(typeof(SessionCacheTests), nameof(MockActiveModsInLoadOrder))));
                }
            }
            catch { }
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            harmony?.UnpatchAll("FasterGameLoading.Tests.Settings.SessionCacheTests");
        }

        [SetUp]
        public void SetUp()
        {
            mockActiveMods.Clear();
            ResetSessionCache();
        }

        [TearDown]
        public void TearDown()
        {
            mockActiveMods.Clear();
            ResetSessionCache();
            Scribe.mode = LoadSaveMode.Inactive;
        }

        private static bool MockActiveModsInLoadOrder(ref IEnumerable<ModMetaData> __result)
        {
            __result = mockActiveMods;
            return false;
        }

        private static ModMetaData CreateMockModMetaData(string packageId)
        {
            var meta = (ModMetaData)FormatterServices.GetUninitializedObject(typeof(ModMetaData));
            var field = AccessTools.Field(typeof(ModMetaData), "packageIdLowerCase") ?? AccessTools.Field(typeof(ModMetaData), "packageId");
            field?.SetValue(meta, packageId.ToLowerInvariant());
            return meta;
        }

        private static void ResetSessionCache()
        {
            SessionCache.loadedTexturesSinceLastSession = new Dictionary<string, string>(StringComparer.Ordinal);
            SessionCache.loadedTypesByFullNameSinceLastSession = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
            SessionCache.modsInLastSession = new List<string>();
            SessionCache.historicalBakeSpeeds = new List<float>();
            // 預設與本次組件一致，讓只驗證 mod 清單比對的測試不受組件指紋影響。
            SessionCache.typeCacheAssemblyFingerprint = SessionCache.ComputeCurrentAssemblyFingerprint();
        }

        [Test]
        public void Weights_LengthMatchesHistorySize()
        {
            Assert.That(SessionCache.WEIGHTS.Length, Is.EqualTo(4));
            Assert.That(SessionCache.WEIGHTS.Length, Is.EqualTo(SessionCache.HISTORY_SIZE));
            Assert.That(SessionCache.WEIGHTS.Sum(), Is.EqualTo(1.0f).Within(0.001f));
        }

        [Test]
        public void ExposeData_WhenModsUnchanged_PreservesCachedEntries()
        {
            mockActiveMods.Add(CreateMockModMetaData("ludeon.rimworld"));
            mockActiveMods.Add(CreateMockModMetaData("fgl.mod"));

            SessionCache.modsInLastSession = new List<string> { "ludeon.rimworld", "fgl.mod" };
            SessionCache.loadedTexturesSinceLastSession["texA"] = "pathA";
            SessionCache.loadedTypesByFullNameSinceLastSession["typeA"] = "assemblyA";

            Scribe.mode = LoadSaveMode.PostLoadInit;
            SessionCache.ExposeData();

            Assert.That(SessionCache.loadedTexturesSinceLastSession, Has.Count.EqualTo(1));
            Assert.That(SessionCache.loadedTexturesSinceLastSession["texA"], Is.EqualTo("pathA"));
            Assert.That(SessionCache.loadedTypesByFullNameSinceLastSession, Has.Count.EqualTo(1));
            Assert.That(SessionCache.loadedTypesByFullNameSinceLastSession["typeA"], Is.EqualTo("assemblyA"));
        }

        [Test]
        public void ExposeData_WhenModsChanged_ClearsCache()
        {
            mockActiveMods.Add(CreateMockModMetaData("ludeon.rimworld"));
            mockActiveMods.Add(CreateMockModMetaData("fgl.mod.v2"));

            SessionCache.modsInLastSession = new List<string> { "ludeon.rimworld", "fgl.mod.v1" };
            SessionCache.loadedTexturesSinceLastSession["texA"] = "pathA";
            SessionCache.loadedTypesByFullNameSinceLastSession["typeA"] = "assemblyA";

            Scribe.mode = LoadSaveMode.PostLoadInit;
            SessionCache.ExposeData();

            Assert.That(SessionCache.loadedTexturesSinceLastSession, Is.Empty);
            Assert.That(SessionCache.loadedTypesByFullNameSinceLastSession, Is.Empty);
        }

        [Test]
        public void ExposeData_WhenModCountDiffers_ClearsCache()
        {
            mockActiveMods.Add(CreateMockModMetaData("ludeon.rimworld"));
            mockActiveMods.Add(CreateMockModMetaData("fgl.mod"));

            SessionCache.modsInLastSession = new List<string> { "ludeon.rimworld" };
            SessionCache.loadedTexturesSinceLastSession["texA"] = "pathA";

            Scribe.mode = LoadSaveMode.PostLoadInit;
            SessionCache.ExposeData();

            Assert.That(SessionCache.loadedTexturesSinceLastSession, Is.Empty);
        }

        // ── 真實存讀檔循環 ──
        // 依 LoadedModManager.WriteModSettings／ReadModSettings 的流程走完 Saving → LoadingVars →
        // ResolvingCrossRefs → PostLoadInit。只呼叫 PostLoadInit 那一輪會漏掉「資料只在 LoadingVars
        // 讀進區域變數」這類跨輪次的錯誤（過去型別對照就因此從未被讀回來）。

        [Test]
        public void SettingsRoundTrip_RestoresPersistedTypeMapping()
        {
            mockActiveMods.Add(CreateMockModMetaData("ludeon.rimworld"));
            SessionCache.modsInLastSession = new List<string> { "ludeon.rimworld" };
            SessionCache.loadedTypesByFullNameSinceLastSession["ThingDef"] = "Verse.ThingDef";
            string path = Path.Combine(Path.GetTempPath(), $"FGL_RoundTrip_{Guid.NewGuid():N}.xml");

            try
            {
                var saved = new FasterGameLoadingSettings();
                Scribe.saver.InitSaving(path, "SettingsBlock");
                try
                {
                    Scribe_Deep.Look(ref saved, "ModSettings");
                }
                finally
                {
                    Scribe.saver.FinalizeSaving();
                }

                ResetSessionCache();
                SessionCache.typeCacheAssemblyFingerprint = null;

                FasterGameLoadingSettings loaded = null;
                Scribe.loader.InitLoading(path);
                try
                {
                    Scribe_Deep.Look(ref loaded, "ModSettings");
                }
                finally
                {
                    Scribe.loader.FinalizeLoading();
                }

                Assert.That(SessionCache.loadedTypesByFullNameSinceLastSession.TryGetValue("ThingDef", out var fullName), Is.True,
                    "上次 session 存下的型別對照必須在讀檔後還原。");
                Assert.That(fullName, Is.EqualTo("Verse.ThingDef"));
                Assert.That(SessionCache.typeCacheAssemblyFingerprint, Is.EqualTo(SessionCache.ComputeCurrentAssemblyFingerprint()));
            }
            finally
            {
                if (Scribe.mode is not LoadSaveMode.Inactive)
                {
                    Scribe.ForceStop();
                }
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        // ── 組件指紋：mod 清單不變但組件內容更新時，型別對照必須失效 ──

        [Test]
        public void ExposeData_WhenAssemblyFingerprintChanged_ClearsOnlyTypeCache()
        {
            mockActiveMods.Add(CreateMockModMetaData("ludeon.rimworld"));
            SessionCache.modsInLastSession = new List<string> { "ludeon.rimworld" };
            SessionCache.loadedTexturesSinceLastSession["texA"] = "pathA";
            SessionCache.loadedTypesByFullNameSinceLastSession["typeA"] = "Old.Namespace.TypeA";
            SessionCache.typeCacheAssemblyFingerprint = "fingerprint-of-an-older-build";

            Scribe.mode = LoadSaveMode.PostLoadInit;
            SessionCache.ExposeData();

            Assert.That(SessionCache.loadedTypesByFullNameSinceLastSession, Is.Empty);
            Assert.That(SessionCache.loadedTexturesSinceLastSession, Has.Count.EqualTo(1),
                "組件更新不影響貼圖路徑，不應連帶清掉貼圖快取。");
        }

        [Test]
        public void ExposeData_WhenFingerprintMissingFromOldSettings_ClearsTypeCache()
        {
            mockActiveMods.Add(CreateMockModMetaData("ludeon.rimworld"));
            SessionCache.modsInLastSession = new List<string> { "ludeon.rimworld" };
            SessionCache.loadedTypesByFullNameSinceLastSession["typeA"] = "Some.TypeA";
            SessionCache.typeCacheAssemblyFingerprint = null;

            Scribe.mode = LoadSaveMode.PostLoadInit;
            SessionCache.ExposeData();

            Assert.That(SessionCache.loadedTypesByFullNameSinceLastSession, Is.Empty);
        }

        [Test]
        public void ComputeAssemblyFingerprint_IsOrderIndependentAndSensitiveToMembership()
        {
            var a = typeof(int).Assembly;
            var b = typeof(SessionCache).Assembly;

            Assert.That(SessionCache.ComputeAssemblyFingerprint(new[] { a, b }),
                Is.EqualTo(SessionCache.ComputeAssemblyFingerprint(new[] { b, a })));
            Assert.That(SessionCache.ComputeAssemblyFingerprint(new[] { a }),
                Is.Not.EqualTo(SessionCache.ComputeAssemblyFingerprint(new[] { a, b })));
        }

        // ── RestoreAfterLoad：舊存檔缺欄位時的補齊行為 ──

        [Test]
        public void RestoreAfterLoad_NullCollectionsAreReplacedWithEmptyOnes()
        {
            mockActiveMods.Add(CreateMockModMetaData("ludeon.rimworld"));
            SessionCache.modsInLastSession = null;
            SessionCache.loadedTexturesSinceLastSession = null;
            SessionCache.loadedTypesByFullNameSinceLastSession = null;
            SessionCache.historicalBakeSpeeds = null;

            InvokeRestoreAfterLoad();

            Assert.That(SessionCache.loadedTexturesSinceLastSession, Is.Not.Null.And.Empty);
            Assert.That(SessionCache.loadedTypesByFullNameSinceLastSession, Is.Not.Null.And.Empty);
            Assert.That(SessionCache.modsInLastSession, Is.Not.Null.And.Empty);
            Assert.That(SessionCache.historicalBakeSpeeds, Is.Not.Null.And.Empty);
        }

        [Test]
        public void RestoreAfterLoad_ClearsTextureCacheDirectoryWhenModSetChanged()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "FGLSessionCache_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            var cacheManager = new TextureCacheManager(tempDir);
            cacheManager.SetCacheEntry(Path.Combine(tempDir, "a.png"), Path.Combine(tempDir, "a_cache.png"));

            var originalInstance = FasterGameLoadingMod.Instance;
            SetModInstance(CreateModWithCacheManager(cacheManager));
            try
            {
                mockActiveMods.Add(CreateMockModMetaData("newly.added.mod"));
                SessionCache.modsInLastSession = new List<string>();

                InvokeRestoreAfterLoad();

                Assert.That(cacheManager.CacheCount, Is.Zero,
                    "Mod 組合變更時降質快取必然過期，磁碟快取與對照表都要清掉。");
                Assert.That(Directory.Exists(tempDir), Is.False);
            }
            finally
            {
                SetModInstance(originalInstance);
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }

        private static void InvokeRestoreAfterLoad()
        {
            AccessTools.Method(typeof(SessionCache), "RestoreAfterLoad")
                .Invoke(obj: null, parameters: null);
        }

        private static FasterGameLoadingMod CreateModWithCacheManager(TextureCacheManager cacheManager)
        {
            var mod = (FasterGameLoadingMod)FormatterServices.GetUninitializedObject(typeof(FasterGameLoadingMod));
            typeof(FasterGameLoadingMod).GetProperty(nameof(FasterGameLoadingMod.CacheManager))
                .SetValue(mod, cacheManager);
            return mod;
        }

        private static void SetModInstance(FasterGameLoadingMod mod)
        {
            AccessTools.Property(typeof(FasterGameLoadingMod), nameof(FasterGameLoadingMod.Instance))
                .SetValue(obj: null, value: mod);
        }
    }
}
