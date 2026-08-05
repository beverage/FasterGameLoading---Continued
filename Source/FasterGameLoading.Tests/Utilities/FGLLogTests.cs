using NUnit.Framework;

namespace FasterGameLoading.Tests.Utilities
{
    [TestFixture]
    public class FGLLogTests
    {
        [Test]
        public void Message_WhenVerboseLoggingIsDisabledIsSafeInHeadlessTestRunner()
        {
            var previous = FasterGameLoadingSettings.VerboseLogging;
            try
            {
                FasterGameLoadingSettings.VerboseLogging = false;

                Assert.DoesNotThrow(() => FGLLog.Message("headless test message"));
            }
            finally
            {
                FasterGameLoadingSettings.VerboseLogging = previous;
            }
        }

        [Test]
        public void FlushPending_WithEmptyQueueDoesNotThrow()
        {
            Assert.DoesNotThrow(() => FGLLog.FlushPending());
        }
    }
}
