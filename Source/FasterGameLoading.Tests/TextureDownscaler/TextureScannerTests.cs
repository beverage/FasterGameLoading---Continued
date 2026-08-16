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

            var result = scanner.TryGetTexturePath(texture: null, fullPath: out path);

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

            // SaveTexturePath 同時維護 savedTextures（弱引用字典）與 savedTexturePathsByTexture
            // （弱鍵反查表）；TryGetSavedTexturePath 實際查詢的是後者。
            var saveTexturePath = typeof(ModContentLoaderTexture2D_LoadTexture_Patch)
                .GetMethod("SaveTexturePath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.That(saveTexturePath, Is.Not.Null);
            saveTexturePath.Invoke(null, new object[] { expectedPath, texture });

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

            foreach (TextureResize.TextureType type in Enum.GetValues<TextureResize.TextureType>())
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
                new KeyValuePair<BuildableDef, string>(key: null, value: "ui"));
            var texture = Uninitialized<Texture2D>();
            scanner.texturesByPaths[texture] = "texture";
            scanner.texturesByDefs[texture] =
                new KeyValuePair<BuildableDef, string>(key: null, value: "texture");

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

        [Test]
        public void CacheLoadCounters_GetterSetter_RoundTripPreservesValue()
        {
            // cacheLoadHits / cacheLoadFailures 是公開靜態計數器，供 Prefix 在命中/失敗時遞增，
            // 並在摘要訊息中讀取。此測試驗證 getter 與 setter 能正確往返數值。
            ModContentLoaderTexture2D_LoadTexture_Patch.cacheLoadHits = 7;
            ModContentLoaderTexture2D_LoadTexture_Patch.cacheLoadFailures = 3;

            Assert.That(ModContentLoaderTexture2D_LoadTexture_Patch.cacheLoadHits, Is.EqualTo(7));
            Assert.That(ModContentLoaderTexture2D_LoadTexture_Patch.cacheLoadFailures, Is.EqualTo(3));

            // 清理，避免污染其他測試
            ModContentLoaderTexture2D_LoadTexture_Patch.cacheLoadHits = 0;
            ModContentLoaderTexture2D_LoadTexture_Patch.cacheLoadFailures = 0;
        }

        [Test]
        public void CacheLoadCounters_ResetToZero_ClearsPreviousValues()
        {
            ModContentLoaderTexture2D_LoadTexture_Patch.cacheLoadHits = 11;
            ModContentLoaderTexture2D_LoadTexture_Patch.cacheLoadFailures = 5;

            ModContentLoaderTexture2D_LoadTexture_Patch.cacheLoadHits = 0;
            ModContentLoaderTexture2D_LoadTexture_Patch.cacheLoadFailures = 0;

            Assert.That(ModContentLoaderTexture2D_LoadTexture_Patch.cacheLoadHits, Is.EqualTo(0));
            Assert.That(ModContentLoaderTexture2D_LoadTexture_Patch.cacheLoadFailures, Is.EqualTo(0));
        }
    }
}
