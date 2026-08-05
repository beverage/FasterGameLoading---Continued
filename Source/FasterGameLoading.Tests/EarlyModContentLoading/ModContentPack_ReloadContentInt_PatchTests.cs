using NUnit.Framework;

namespace FasterGameLoading.Tests.EarlyModContentLoading
{
    [TestFixture]
    public class ModContentPack_ReloadContentInt_PatchTests
    {
        [SetUp]
        public void SetUp()
        {
            ModContentPack_ReloadContentInt_Patch.loadedMods.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            ModContentPack_ReloadContentInt_Patch.loadedMods.Clear();
        }

        [Test]
        public void Postfix_RecordsTheContentPackForFutureCalls()
        {
            ModContentPack_ReloadContentInt_Patch.Postfix(null);

            Assert.That(ModContentPack_ReloadContentInt_Patch.loadedMods.Contains(null), Is.True);
        }
    }
}
