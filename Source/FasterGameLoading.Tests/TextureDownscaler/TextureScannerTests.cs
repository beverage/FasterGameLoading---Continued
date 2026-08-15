using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using NUnit.Framework;
using UnityEngine;
using Verse;

namespace FasterGameLoading.Tests.TextureDownscaler
{
    [TestFixture]
    public class TextureScannerTests
    {
        private static T Uninitialized<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));

        [TearDown]
        public void TearDown()
        {
            ModContentLoaderTexture2D_LoadTexture_Patch.savedTextures.Clear();
        }

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
        public void TryGetTexturePath_WhenInTexturesByPaths_ReturnsTrueAndPath()
        {
            var scanner = new TextureScanner();
            var texture = Uninitialized<Texture2D>();
            scanner.texturesByPaths[texture] = "Mods/MyMod/Textures/Test.png";

            var result = scanner.TryGetTexturePath(texture, out var path);

            Assert.That(result, Is.True);
            Assert.That(path, Is.EqualTo("Mods/MyMod/Textures/Test.png"));
        }

        [Test]
        public void TryGetTexturePath_WhenInSavedTexturesPatch_FindsAndCachesPath()
        {
            var scanner = new TextureScanner();
            var texture = Uninitialized<Texture2D>();
            var expectedPath = "Mods/MyMod/Textures/FromSaved.png";

            ModContentLoaderTexture2D_LoadTexture_Patch.savedTextures[expectedPath] = new System.WeakReference<Texture2D>(texture);

            var result = scanner.TryGetTexturePath(texture, out var path);

            Assert.That(result, Is.True);
            Assert.That(path, Is.EqualTo(expectedPath));
            Assert.That(scanner.texturesByPaths.ContainsKey(texture), Is.True);
        }

        [Test]
        public void BuildTextureScanData_InitializesAllTextureTypeContainers()
        {
            var scanner = new TextureScanner();

            Assert.DoesNotThrow(() => scanner.BuildTextureScanData());

            foreach (TextureResize.TextureType type in Enum.GetValues(typeof(TextureResize.TextureType)))
            {
                Assert.That(scanner.textures.ContainsKey(type), Is.True);
                Assert.That(scanner.textures[type], Is.Not.Null);
            }
        }

        [Test]
        public void ClearTextureScanData_ClearsAllIndexesAndTypeLists()
        {
            var scanner = new TextureScanner();
            scanner.textures[TextureResize.TextureType.UI] =
                new List<KeyValuePair<BuildableDef, string>>();
            scanner.textures[TextureResize.TextureType.UI].Add(
                new KeyValuePair<BuildableDef, string>(null, "ui"));
            var texture = Uninitialized<Texture2D>();
            scanner.texturesByPaths[texture] = "texture";
            scanner.texturesByDefs[texture] =
                new KeyValuePair<BuildableDef, string>(null, "texture");

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
