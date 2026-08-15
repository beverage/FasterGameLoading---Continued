using System.Runtime.Serialization;
using HarmonyLib;
using NUnit.Framework;
using Verse;

namespace FasterGameLoading.Tests.EarlyModContentLoading
{
    [TestFixture]
    public class ModContentPack_ReloadContentInt_PatchTests
    {
        private static Harmony harmony;
        private static bool mockIsInMainThread = true;
        private static bool tryDrainCalled;

        private static bool MockIsInMainThread() => mockIsInMainThread;

        private static bool MockTryDrainMainThreadRequests()
        {
            tryDrainCalled = true;
            return false;
        }

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            harmony = new Harmony("FasterGameLoading.Tests.ModContentPack_ReloadContentInt_PatchTests");

            var isInMainThreadGetter = AccessTools.PropertyGetter(typeof(UnityData), nameof(UnityData.IsInMainThread));
            if (isInMainThreadGetter != null)
            {
                harmony.Patch(isInMainThreadGetter, prefix: new HarmonyMethod(AccessTools.Method(typeof(ModContentPack_ReloadContentInt_PatchTests), nameof(MockIsInMainThread))));
            }

            var tryDrainMethod = AccessTools.Method(typeof(ModContentLoaderTexture2D_LoadTexture_Patch), nameof(ModContentLoaderTexture2D_LoadTexture_Patch.TryDrainMainThreadRequests));
            if (tryDrainMethod != null)
            {
                harmony.Patch(tryDrainMethod, prefix: new HarmonyMethod(AccessTools.Method(typeof(ModContentPack_ReloadContentInt_PatchTests), nameof(MockTryDrainMainThreadRequests))));
            }
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            harmony?.UnpatchAll("FasterGameLoading.Tests.ModContentPack_ReloadContentInt_PatchTests");
        }

        [SetUp]
        public void SetUp()
        {
            ModContentPack_ReloadContentInt_Patch.loadedMods.Clear();
            mockIsInMainThread = true;
            tryDrainCalled = false;
        }

        [TearDown]
        public void TearDown()
        {
            ModContentPack_ReloadContentInt_Patch.loadedMods.Clear();
            mockIsInMainThread = true;
            tryDrainCalled = false;
        }

        private static ModContentPack CreateMockMod()
        {
            return (ModContentPack)FormatterServices.GetUninitializedObject(typeof(ModContentPack));
        }

        [Test]
        public void Prefix_WhenInMainThread_CallsTryDrainMainThreadRequests()
        {
            mockIsInMainThread = true;
            tryDrainCalled = false;
            var mod = CreateMockMod();

            bool shouldRun = ModContentPack_ReloadContentInt_Patch.Prefix(mod);

            Assert.That(tryDrainCalled, Is.True);
            Assert.That(shouldRun, Is.True);
        }

        [Test]
        public void Prefix_WhenNotInMainThread_DoesNotCallTryDrainMainThreadRequests()
        {
            mockIsInMainThread = false;
            tryDrainCalled = false;
            var mod = CreateMockMod();

            bool shouldRun = ModContentPack_ReloadContentInt_Patch.Prefix(mod);

            Assert.That(tryDrainCalled, Is.False);
            Assert.That(shouldRun, Is.True);
        }

        [Test]
        public void Prefix_WhenModNotLoaded_ReturnsTrue_AndWhenLoaded_ReturnsFalse()
        {
            var mod = CreateMockMod();

            // Unloaded mod -> returns true
            bool firstCheck = ModContentPack_ReloadContentInt_Patch.Prefix(mod);
            Assert.That(firstCheck, Is.True);

            // Postfix records loaded mod
            ModContentPack_ReloadContentInt_Patch.Postfix(mod);

            // Loaded mod -> returns false
            bool secondCheck = ModContentPack_ReloadContentInt_Patch.Prefix(mod);
            Assert.That(secondCheck, Is.False);
        }

        [Test]
        public void Postfix_RecordsTheContentPackForFutureCalls()
        {
            var mod = CreateMockMod();
            ModContentPack_ReloadContentInt_Patch.Postfix(mod);

            Assert.That(ModContentPack_ReloadContentInt_Patch.loadedMods.Contains(mod), Is.True);
        }

        [Test]
        public void CacheResetter_ResetAll_ClearsLoadedMods()
        {
            var mod = CreateMockMod();
            ModContentPack_ReloadContentInt_Patch.Postfix(mod);
            Assert.That(ModContentPack_ReloadContentInt_Patch.loadedMods.Contains(mod), Is.True);

            CacheResetter.ResetAll();

            Assert.That(ModContentPack_ReloadContentInt_Patch.loadedMods, Is.Empty);
        }
    }
}
