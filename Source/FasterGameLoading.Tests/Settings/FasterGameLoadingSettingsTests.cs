using NUnit.Framework;

namespace FasterGameLoading.Tests.Settings
{
    [TestFixture]
    public class FasterGameLoadingSettingsTests
    {
        private bool origVerboseLogging;
        private bool origDelayGraphicLoading;
        private bool origEarlyModContentLoading;
        private bool origStaticAtlasesBaking;
        private bool origEnableMultiThreading;
        private bool origXPathCaching;

        [SetUp]
        public void SetUp()
        {
            origVerboseLogging = FasterGameLoadingSettings.VerboseLogging;
            origDelayGraphicLoading = FasterGameLoadingSettings.DelayGraphicLoading;
            origEarlyModContentLoading = FasterGameLoadingSettings.earlyModContentLoading;
            origStaticAtlasesBaking = FasterGameLoadingSettings.StaticAtlasesBaking;
            origEnableMultiThreading = FasterGameLoadingSettings.EnableMultiThreading;
            origXPathCaching = FasterGameLoadingSettings.XPathCaching;
        }

        [TearDown]
        public void TearDown()
        {
            FasterGameLoadingSettings.VerboseLogging = origVerboseLogging;
            FasterGameLoadingSettings.DelayGraphicLoading = origDelayGraphicLoading;
            FasterGameLoadingSettings.earlyModContentLoading = origEarlyModContentLoading;
            FasterGameLoadingSettings.StaticAtlasesBaking = origStaticAtlasesBaking;
            FasterGameLoadingSettings.EnableMultiThreading = origEnableMultiThreading;
            FasterGameLoadingSettings.XPathCaching = origXPathCaching;
        }

        [Test]
        public void VerboseLogging_PropertyGetSet()
        {
            FasterGameLoadingSettings.VerboseLogging = true;
            Assert.That(FasterGameLoadingSettings.VerboseLogging, Is.True);

            FasterGameLoadingSettings.VerboseLogging = false;
            Assert.That(FasterGameLoadingSettings.VerboseLogging, Is.False);
        }

        [Test]
        public void DelayGraphicLoading_PropertyGetSet()
        {
            FasterGameLoadingSettings.DelayGraphicLoading = true;
            Assert.That(FasterGameLoadingSettings.DelayGraphicLoading, Is.True);

            FasterGameLoadingSettings.DelayGraphicLoading = false;
            Assert.That(FasterGameLoadingSettings.DelayGraphicLoading, Is.False);
        }

        [Test]
        public void EarlyModContentLoading_FieldGetSet()
        {
            FasterGameLoadingSettings.earlyModContentLoading = true;
            Assert.That(FasterGameLoadingSettings.earlyModContentLoading, Is.True);

            FasterGameLoadingSettings.earlyModContentLoading = false;
            Assert.That(FasterGameLoadingSettings.earlyModContentLoading, Is.False);
        }

        [Test]
        public void StaticAtlasesBaking_PropertyGetSet()
        {
            FasterGameLoadingSettings.StaticAtlasesBaking = true;
            Assert.That(FasterGameLoadingSettings.StaticAtlasesBaking, Is.True);

            FasterGameLoadingSettings.StaticAtlasesBaking = false;
            Assert.That(FasterGameLoadingSettings.StaticAtlasesBaking, Is.False);
        }

        [Test]
        public void EnableMultiThreading_PropertyGetSet()
        {
            FasterGameLoadingSettings.EnableMultiThreading = true;
            Assert.That(FasterGameLoadingSettings.EnableMultiThreading, Is.True);

            FasterGameLoadingSettings.EnableMultiThreading = false;
            Assert.That(FasterGameLoadingSettings.EnableMultiThreading, Is.False);
        }

        [Test]
        public void XPathCaching_PropertyGetSet()
        {
            FasterGameLoadingSettings.XPathCaching = true;
            Assert.That(FasterGameLoadingSettings.XPathCaching, Is.True);

            FasterGameLoadingSettings.XPathCaching = false;
            Assert.That(FasterGameLoadingSettings.XPathCaching, Is.False);
        }
    }
}
