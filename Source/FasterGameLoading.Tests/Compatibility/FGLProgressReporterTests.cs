using NUnit.Framework;

namespace FasterGameLoading.Tests.Compatibility
{
    [TestFixture]
    public class FGLProgressReporterTests
    {
        [Test]
        public void Prepare_MatchesAvailabilityOfOptionalLoadingProgressGetter()
        {
            var targetMethod = FGLProgressReporter.TargetMethod();

            Assert.That(FGLProgressReporter.Prepare(), Is.EqualTo(targetMethod != null));
        }

        [Test]
        public void Postfix_WhenOriginalResultIsFalse_LeavesItFalse()
        {
            var result = false;

            FGLProgressReporter.Postfix(ref result);

            Assert.That(result, Is.False);
        }

        [Test]
        public void Postfix_WhenEarlyLoadingIsDisabled_LeavesOriginalResultUnchanged()
        {
            var previous = FasterGameLoadingSettings.earlyModContentLoading;
            try
            {
                FasterGameLoadingSettings.earlyModContentLoading = false;
                var result = true;

                FGLProgressReporter.Postfix(ref result);

                Assert.That(result, Is.True);
            }
            finally
            {
                FasterGameLoadingSettings.earlyModContentLoading = previous;
            }
        }

        [Test]
        public void ReloadContentPostfix_WhenMoveNextHasMoreWork_DoesNothing()
        {
            Assert.DoesNotThrow(() => LoadingProgress_ReloadContent_Patch.Postfix(null, true));
        }
    }
}
