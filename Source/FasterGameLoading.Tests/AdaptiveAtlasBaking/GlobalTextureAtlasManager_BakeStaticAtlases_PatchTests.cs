using NUnit.Framework;

namespace FasterGameLoading.Tests.AdaptiveAtlasBaking
{
    [TestFixture]
    public class GlobalTextureAtlasManager_BakeStaticAtlases_PatchTests
    {
        private bool previousDelay;
        private bool previousStaticBake;
        private bool previousAllLoaded;
        private bool previousFailed;

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
