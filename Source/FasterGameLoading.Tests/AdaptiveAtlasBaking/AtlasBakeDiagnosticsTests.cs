using NUnit.Framework;

namespace FasterGameLoading.Tests.AdaptiveAtlasBaking
{
    [TestFixture]
    public class AtlasBakeDiagnosticsTests
    {
        [Test]
        public void LogPotentialMaskIssues_ExposesPublicDiagnosticEntryPoint()
        {
            var method = typeof(AtlasBakeDiagnostics).GetMethod(
                nameof(AtlasBakeDiagnostics.LogPotentialMaskIssues));

            Assert.That(method, Is.Not.Null);
            Assert.That(method.IsStatic, Is.True);
        }
    }
}
