using System.Collections.Generic;
using System.Runtime.Serialization;
using NUnit.Framework;
using UnityEngine;

namespace FasterGameLoading.Tests.TextureDownscaler
{
    [TestFixture]
    public class TextureScannerTests
    {
        [Test]
        public void TryGetTexturePath_WithNullTextureReturnsFalse()
        {
            var scanner = new TextureScanner();
            string path;

            var result = scanner.TryGetTexturePath(null, out path);

            Assert.That(result, Is.False);
            Assert.That(path, Is.Null);
        }

        [Test]
        public void ClearTextureScanData_ClearsAllIndexesAndTypeLists()
        {
            var scanner = new TextureScanner();
            scanner.textures[TextureResize.TextureType.UI] =
                new List<KeyValuePair<Verse.BuildableDef, string>>();
            scanner.textures[TextureResize.TextureType.UI].Add(
                new KeyValuePair<Verse.BuildableDef, string>(null, "ui"));
            var texture = (Texture2D)FormatterServices.GetUninitializedObject(typeof(Texture2D));
            scanner.texturesByPaths[texture] = "texture";
            scanner.texturesByDefs[texture] =
                new KeyValuePair<Verse.BuildableDef, string>(null, "texture");

            scanner.ClearTextureScanData();

            Assert.That(scanner.texturesByPaths, Is.Empty);
            Assert.That(scanner.texturesByDefs, Is.Empty);
            Assert.That(scanner.textures[TextureResize.TextureType.UI], Is.Empty);
        }

        [Test]
        public void ClearTextureScanData_IsSafeBeforeContainersAreInitialized()
        {
            var scanner = new TextureScanner();

            Assert.DoesNotThrow(() => scanner.ClearTextureScanData());
        }
    }
}
