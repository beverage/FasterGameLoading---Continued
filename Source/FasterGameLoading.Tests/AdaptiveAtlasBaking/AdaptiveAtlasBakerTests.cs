using NUnit.Framework;

namespace FasterGameLoading.Tests.AdaptiveAtlasBaking
{
    [TestFixture]
    public class AdaptiveAtlasBakerTests
    {
        [Test]
        public void PerformAdaptiveStaticAtlasBake_ReturnsCoroutineEntryPoint()
        {
            var iterator = AdaptiveAtlasBaker.PerformAdaptiveStaticAtlasBake(null);

            Assert.That(iterator, Is.Not.Null);
        }
    }
}
