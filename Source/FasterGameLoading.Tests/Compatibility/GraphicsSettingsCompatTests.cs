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
        public void IsActive_DelegatesToModStateWithoutCaching()
        {
            // IsActive 已改為 Utils.IsModActive 的無快取直通；多次讀取結果一致，
            // 且 CacheResetter 不再持有其狀態，重置不得影響結果。
            var isActive = GraphicsSettingsCompat.IsActive;

            Assert.That(GraphicsSettingsCompat.IsActive, Is.EqualTo(isActive));
            Assert.DoesNotThrow(() => CacheResetter.ResetAll());
            Assert.That(GraphicsSettingsCompat.IsActive, Is.EqualTo(isActive));
        }
    }
}
