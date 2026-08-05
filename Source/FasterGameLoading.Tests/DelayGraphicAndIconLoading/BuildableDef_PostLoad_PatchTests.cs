using NUnit.Framework;

namespace FasterGameLoading.Tests.DelayGraphicAndIconLoading
{
    [TestFixture]
    public class BuildableDef_PostLoad_PatchTests
    {
        [Test]
        public void Prepare_ReflectsDelayGraphicLoadingSetting()
        {
            var previous = FasterGameLoadingSettings.DelayGraphicLoading;
            try
            {
                FasterGameLoadingSettings.DelayGraphicLoading = true;
                Assert.That(BuildableDef_PostLoad_Patch.Prepare(), Is.True);

                FasterGameLoadingSettings.DelayGraphicLoading = false;
                Assert.That(BuildableDef_PostLoad_Patch.Prepare(), Is.False);
            }
            finally
            {
                FasterGameLoadingSettings.DelayGraphicLoading = previous;
            }
        }

        [Test]
        public void ExecuteDelayed_ExposesActionAndDefParameters()
        {
            var method = typeof(BuildableDef_PostLoad_Patch).GetMethod(
                nameof(BuildableDef_PostLoad_Patch.ExecuteDelayed));

            Assert.That(method, Is.Not.Null);
            Assert.That(method.GetParameters(), Has.Length.EqualTo(2));
        }
    }
}
