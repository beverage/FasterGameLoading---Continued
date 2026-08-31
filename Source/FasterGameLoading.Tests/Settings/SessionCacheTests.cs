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
        private static readonly string[] ExpectedRetainedXPaths = { "Defs/ThingDef/label" };

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
            SessionCache.xmlPathsSinceLastSession = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
            SessionCache.modsInLastSession = new List<string>();
            SessionCache.xmlMetadataHashByMod = new Dictionary<string, long>(StringComparer.Ordinal);
            SessionCache.historicalBakeSpeeds = new List<float>();
            SessionCache.xmlCombinedHashSinceLastSession = 0;
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
            SessionCache.xmlPathsSinceLastSession["xpathA"] = 0;
            SessionCache.xmlMetadataHashByMod["fgl.mod"] = 12345L;

            Scribe.mode = LoadSaveMode.PostLoadInit;
            SessionCache.ExposeData();

            Assert.That(SessionCache.loadedTexturesSinceLastSession, Has.Count.EqualTo(1));
            Assert.That(SessionCache.loadedTexturesSinceLastSession["texA"], Is.EqualTo("pathA"));
            Assert.That(SessionCache.loadedTypesByFullNameSinceLastSession, Has.Count.EqualTo(1));
            Assert.That(SessionCache.loadedTypesByFullNameSinceLastSession["typeA"], Is.EqualTo("assemblyA"));
            Assert.That(SessionCache.xmlPathsSinceLastSession, Has.Count.EqualTo(1));
            Assert.That(SessionCache.xmlMetadataHashByMod, Has.Count.EqualTo(1));
        }

        [Test]
        public void ExposeData_WhenModsChanged_ClearsCache()
        {
            mockActiveMods.Add(CreateMockModMetaData("ludeon.rimworld"));
            mockActiveMods.Add(CreateMockModMetaData("fgl.mod.v2"));

            SessionCache.modsInLastSession = new List<string> { "ludeon.rimworld", "fgl.mod.v1" };
            SessionCache.loadedTexturesSinceLastSession["texA"] = "pathA";
            SessionCache.loadedTypesByFullNameSinceLastSession["typeA"] = "assemblyA";
            SessionCache.xmlPathsSinceLastSession["xpathA"] = 0;
            SessionCache.xmlMetadataHashByMod["fgl.mod.v1"] = 12345L;

            Scribe.mode = LoadSaveMode.PostLoadInit;
            SessionCache.ExposeData();

            Assert.That(SessionCache.loadedTexturesSinceLastSession, Is.Empty);
            Assert.That(SessionCache.loadedTypesByFullNameSinceLastSession, Is.Empty);
            Assert.That(SessionCache.xmlPathsSinceLastSession, Is.Empty);
            Assert.That(SessionCache.xmlMetadataHashByMod, Is.Empty);
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

        // ── RestoreAfterLoad：舊存檔缺欄位時的補齊行為 ──
        // 直接以反射呼叫，因為 Scribe_Collections.Look 只有在真正的存檔讀寫循環中
        // 才會把 tempTypes／tempXmlPaths 填成非 null，測試環境沒有存檔可讀。

        [Test]
        public void RestoreAfterLoad_NullCollectionsAreReplacedWithEmptyOnes()
        {
            mockActiveMods.Add(CreateMockModMetaData("ludeon.rimworld"));
            SessionCache.modsInLastSession = null;
            SessionCache.loadedTexturesSinceLastSession = null;
            SessionCache.loadedTypesByFullNameSinceLastSession = null;
            SessionCache.xmlPathsSinceLastSession = null;
            SessionCache.xmlMetadataHashByMod = null;
            SessionCache.historicalBakeSpeeds = null;

            InvokeRestoreAfterLoad(tempTypes: null, tempXmlPaths: null);

            Assert.That(SessionCache.loadedTexturesSinceLastSession, Is.Not.Null.And.Empty);
            Assert.That(SessionCache.loadedTypesByFullNameSinceLastSession, Is.Not.Null.And.Empty);
            Assert.That(SessionCache.xmlPathsSinceLastSession, Is.Not.Null.And.Empty);
            Assert.That(SessionCache.modsInLastSession, Is.Not.Null.And.Empty);
            Assert.That(SessionCache.xmlMetadataHashByMod, Is.Not.Null.And.Empty);
            Assert.That(SessionCache.historicalBakeSpeeds, Is.Not.Null.And.Empty);
        }

        [Test]
        public void RestoreAfterLoad_LoadedTypesFromSaveReplaceRuntimeCache()
        {
            mockActiveMods.Add(CreateMockModMetaData("ludeon.rimworld"));
            SessionCache.modsInLastSession = new List<string> { "ludeon.rimworld" };
            SessionCache.loadedTypesByFullNameSinceLastSession["stale"] = "old";

            var fromSave = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Verse.ThingDef"] = "Assembly-CSharp",
            };

            InvokeRestoreAfterLoad(fromSave, tempXmlPaths: null);

            Assert.That(SessionCache.loadedTypesByFullNameSinceLastSession, Has.Count.EqualTo(1));
            Assert.That(SessionCache.loadedTypesByFullNameSinceLastSession["Verse.ThingDef"], Is.EqualTo("Assembly-CSharp"));
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

                InvokeRestoreAfterLoad(tempTypes: null, tempXmlPaths: null);

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

        // ── RebuildXPathMissCache：哪些 XPath 可以沿用上個 session 的「查無此節點」結論 ──

        [Test]
        public void RebuildXPathMissCache_KeepsOnlyCacheableMissesOutsideTheDenyList()
        {
            SessionCache.xmlPathsSinceLastSession["stale"] = 0;

            var fromSave = new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                ["Defs/ThingDef/label"] = false,          // 可快取的未命中：保留
                ["Defs/ThingDef/description"] = true,     // 上次有命中：不可當作未命中
                ["settingsKey"] = false,                  // 非定位用 XPath：不可快取
                ["Defs/AT_Tag_Something/x"] = false,      // 已知的錯誤快取來源
                ["Defs/KeyedSettings/x"] = false,
                ["Defs/FactionDef/x"] = false,
                ["Defs/ThingDef[@Name='X']/x"] = false,   // 帶屬性篩選：不可快取
            };

            InvokeRebuildXPathMissCache(fromSave);

            Assert.That(SessionCache.xmlPathsSinceLastSession.Keys, Is.EquivalentTo(ExpectedRetainedXPaths));
        }

        [Test]
        public void RebuildXPathMissCache_NullSaveDataLeavesRuntimeCacheUntouched()
        {
            SessionCache.xmlPathsSinceLastSession["kept"] = 0;

            InvokeRebuildXPathMissCache(tempXmlPaths: null);

            Assert.That(SessionCache.xmlPathsSinceLastSession.ContainsKey("kept"), Is.True,
                "存檔中沒有 XPath 區段時（舊版存檔）不該把本次已收集的內容清掉。");
        }

        private static void InvokeRestoreAfterLoad(Dictionary<string, string> tempTypes, Dictionary<string, bool> tempXmlPaths)
        {
            AccessTools.Method(typeof(SessionCache), "RestoreAfterLoad")
                .Invoke(obj: null, new object[] { tempTypes, tempXmlPaths });
        }

        private static void InvokeRebuildXPathMissCache(Dictionary<string, bool> tempXmlPaths)
        {
            AccessTools.Method(typeof(SessionCache), "RebuildXPathMissCache")
                .Invoke(obj: null, new object[] { tempXmlPaths });
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
