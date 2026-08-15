using System.Collections.Concurrent;
using System.Collections.Generic;
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

            var activeModsGetter = AccessTools.PropertyGetter(typeof(ModsConfig), nameof(ModsConfig.ActiveModsInLoadOrder));
            if (activeModsGetter != null)
            {
                harmony.Patch(activeModsGetter, prefix: new HarmonyMethod(AccessTools.Method(typeof(SessionCacheTests), nameof(MockActiveModsInLoadOrder))));
            }
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
            SessionCache.loadedTexturesSinceLastSession = new Dictionary<string, string>();
            SessionCache.loadedTypesByFullNameSinceLastSession = new ConcurrentDictionary<string, string>();
            SessionCache.xmlPathsSinceLastSession = new ConcurrentDictionary<string, byte>();
            SessionCache.modsInLastSession = new List<string>();
            SessionCache.xmlMetadataHashByMod = new Dictionary<string, long>();
            SessionCache.xmlContentHashByMod = new Dictionary<string, long>();
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
    }
}
