using HarmonyLib;
using NUnit.Framework;
using Verse;

namespace FasterGameLoading.Tests.AdaptiveAtlasBaking
{
    [TestFixture]
    public class GlobalTextureAtlasManager_BakeStaticAtlases_PatchTests
    {
        private static Harmony harmony;
        private bool previousDelay;
        private bool previousStaticBake;
        private bool previousAllLoaded;
        private bool previousFailed;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            harmony = new Harmony("FasterGameLoading.Tests.AtlasPatch");
            var emitMethod = AccessTools.Method(typeof(FGLLog), "Emit");
            var prefixSkip = AccessTools.Method(typeof(GlobalTextureAtlasManager_BakeStaticAtlases_PatchTests), nameof(PrefixSkip));
            if (emitMethod != null)
            {
                harmony.Patch(emitMethod, prefix: new HarmonyMethod(prefixSkip));
            }

            var insertVanilla = AccessTools.Method(typeof(AdaptiveAtlasBaker), "InsertVanillaStaticAtlasEntries");
            if (insertVanilla != null)
            {
                harmony.Patch(insertVanilla, prefix: new HarmonyMethod(prefixSkip));
            }

            var vanillaBake = AccessTools.Method(typeof(GlobalTextureAtlasManager), nameof(GlobalTextureAtlasManager.BakeStaticAtlases));
            if (vanillaBake != null)
            {
                harmony.Patch(vanillaBake, prefix: new HarmonyMethod(prefixSkip));
            }
        }


        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            harmony.UnpatchAll("FasterGameLoading.Tests.AtlasPatch");
        }

        private static bool PrefixSkip()
        {
            return false;
        }

        [SetUp]
        public void SetUp()
        {
            previousDelay = FasterGameLoadingSettings.DelayGraphicLoading;
            previousStaticBake = FasterGameLoadingSettings.StaticAtlasesBaking;
            previousAllLoaded = DelayedActions.AllDeferredVisualsLoaded;
            previousFailed = DelayedActions.AdaptiveStaticAtlasBakeFailed;
        }

        [TearDown]
        public void TearDown()
        {
            FasterGameLoadingSettings.DelayGraphicLoading = previousDelay;
            FasterGameLoadingSettings.StaticAtlasesBaking = previousStaticBake;
            DelayedActions.AllDeferredVisualsLoaded = previousAllLoaded;
            DelayedActions.AdaptiveStaticAtlasBakeFailed = previousFailed;
        }

        [Test]
        public void Prefix_WithoutDelayAndWithoutAdaptiveBakeLetsVanillaRun()
        {
            FasterGameLoadingSettings.DelayGraphicLoading = false;
            FasterGameLoadingSettings.StaticAtlasesBaking = false;

            Assert.That(GlobalTextureAtlasManager_BakeStaticAtlases_Patch.Prefix(), Is.True);
        }

        [Test]
        public void Prefix_WithoutDelayAndWithAdaptiveBake_PerformsSynchronousBakeAndReturnsFalse()
        {
            FasterGameLoadingSettings.DelayGraphicLoading = false;
            FasterGameLoadingSettings.StaticAtlasesBaking = true;

            Assert.That(GlobalTextureAtlasManager_BakeStaticAtlases_Patch.Prefix(), Is.False);
        }

        [Test]
        public void Prefix_WithDeferredVisualsNotLoadedSkipsVanillaBake()
        {
            FasterGameLoadingSettings.DelayGraphicLoading = true;
            DelayedActions.AllDeferredVisualsLoaded = false;

            Assert.That(GlobalTextureAtlasManager_BakeStaticAtlases_Patch.Prefix(), Is.False);
        }

        [Test]
        public void Prefix_WithDeferredVisualsLoadedAndAdaptiveBakeDisabledLetsVanillaRun()
        {
            FasterGameLoadingSettings.DelayGraphicLoading = true;
            FasterGameLoadingSettings.StaticAtlasesBaking = false;
            DelayedActions.AllDeferredVisualsLoaded = true;

            Assert.That(GlobalTextureAtlasManager_BakeStaticAtlases_Patch.Prefix(), Is.True);
        }

        [TestCase(false, false)]
        [TestCase(true, true)]
        public void Prefix_WithDeferredVisualsAndAdaptiveBakeReturnsFailureFallbackState(
            bool failed,
            bool expected)
        {
            FasterGameLoadingSettings.DelayGraphicLoading = true;
            FasterGameLoadingSettings.StaticAtlasesBaking = true;
            DelayedActions.AllDeferredVisualsLoaded = true;
            DelayedActions.AdaptiveStaticAtlasBakeFailed = failed;

            Assert.That(GlobalTextureAtlasManager_BakeStaticAtlases_Patch.Prefix(), Is.EqualTo(expected));
        }
    }
}

