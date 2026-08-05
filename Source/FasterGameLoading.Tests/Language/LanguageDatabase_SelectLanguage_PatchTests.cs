using NUnit.Framework;

namespace FasterGameLoading.Tests.Language
{
    [TestFixture]
    public class LanguageDatabase_SelectLanguage_PatchTests
    {
        [Test]
        public void Prefix_InvokesRegisteredCacheResetActions()
        {
            var wasReset = false;
            CacheResetter.Register(() => wasReset = true);

            LanguageDatabase_SelectLanguage_Patch.Prefix();

            Assert.That(wasReset, Is.True);
        }
    }
}
