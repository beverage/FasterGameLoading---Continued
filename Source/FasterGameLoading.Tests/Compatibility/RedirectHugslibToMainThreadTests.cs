using NUnit.Framework;

namespace FasterGameLoading.Tests.Compatibility
{
    [TestFixture]
    public class RedirectHugslibToMainThreadTests
    {
        [Test]
        public void Prepare_IsDisabledWhenDelayedGraphicLoadingIsDisabled()
        {
            var previous = FasterGameLoadingSettings.DelayGraphicLoading;
            try
            {
                FasterGameLoadingSettings.DelayGraphicLoading = false;

                Assert.That(RedirectHugslibToMainThread.Prepare(), Is.False);
            }
            finally
            {
                FasterGameLoadingSettings.DelayGraphicLoading = previous;
            }
        }

        [Test]
        public void TargetMethod_ReturnsTheCachedOptionalHugsLibMethod()
        {
            Assert.That(
                RedirectHugslibToMainThread.TargetMethod(),
                Is.SameAs(RedirectHugslibToMainThread.targetMethod));
        }
    }
}
