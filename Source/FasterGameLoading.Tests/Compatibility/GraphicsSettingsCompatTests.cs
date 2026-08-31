using NUnit.Framework;

namespace FasterGameLoading.Tests.Compatibility
{
    [TestFixture]
    public class GraphicsSettingsCompatTests
    {
        [Test]
        public void HarmonyId_IsStableForGraphicsSettingsCompatibilityPatch()
        {
            Assert.That(
                GraphicsSettingsCompat.HarmonyId,
                Is.EqualTo("com.telefonmast.graphicssettings.rimworld.mod"));
        }

        [Test]
        public void IsActive_CachesResultAndResetsViaCacheResetter()
        {
            CacheResetter.ResetAll();
            var isActive = GraphicsSettingsCompat.IsActive;

            Assert.That(GraphicsSettingsCompat.IsActive, Is.EqualTo(isActive));
            Assert.DoesNotThrow(() => CacheResetter.ResetAll());
            Assert.That(GraphicsSettingsCompat.IsActive, Is.EqualTo(isActive));
        }
    }
}
