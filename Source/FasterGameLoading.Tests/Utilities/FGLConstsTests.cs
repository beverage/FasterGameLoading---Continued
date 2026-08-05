using NUnit.Framework;

namespace FasterGameLoading.Tests.Utilities
{
    [TestFixture]
    public class FGLConstsTests
    {
        [Test]
        public void Constants_ExposeStableCacheAndDirectoryContract()
        {
            Assert.That(FGLConsts.ModName, Is.EqualTo("FasterGameLoading"));
            Assert.That(FGLConsts.TextureCacheDir, Is.EqualTo("TextureCache"));
            Assert.That(FGLConsts.TextureCacheStagingDir, Is.EqualTo("TextureCache_New"));
            Assert.That(FGLConsts.UIDirSlash, Is.EqualTo("/UI/"));
            Assert.That(FGLConsts.TexturesDirName, Is.EqualTo("Textures"));
            Assert.That(FGLConsts.TexturesDirSlash, Is.EqualTo("Textures/"));
            Assert.That(FGLConsts.DefsDirName, Is.EqualTo("Defs"));
            Assert.That(FGLConsts.PatchesDirName, Is.EqualTo("Patches"));
        }

        [Test]
        public void Constants_ExposeExpectedTextureAndReflectionValues()
        {
            Assert.That(FGLConsts.PlaceholderTextureSize, Is.EqualTo(2));
            Assert.That(FGLConsts.AccessToolsPreloadDelayMs, Is.EqualTo(50));
            Assert.That(FGLConsts.TexturePreloadDelayMs, Is.EqualTo(150));
            CollectionAssert.AreEqual(
                new[] { "Furniture", "Production", "Security" },
                FGLConsts.FurnitureKeywords);
            Assert.That(FGLConsts.AlienRaceAssemblyName, Is.EqualTo("AlienRace"));
            Assert.That(FGLConsts.LoadGraphicsHookMethodName, Is.EqualTo("LoadGraphicsHook"));
        }
    }
}
