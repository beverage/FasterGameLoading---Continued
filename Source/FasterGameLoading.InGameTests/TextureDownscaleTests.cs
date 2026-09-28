using System;
using System.IO;
using RimTestRedux;
using RimWorld.IO;
using UnityEngine;
using Verse;

namespace FasterGameLoading.InGameTests
{
    /// <summary>
    /// 降質快取在真實 Unity 上的載入結果：LoadImage、mipmap、壓縮都只能在遊戲內驗證。
    /// 不依賴事先跑過降質工具：測試自己產生「原始貼圖」與「降質快取」兩張 PNG，登記到 FGL 的快取對照表，
    /// 再透過原版 ModContentLoader&lt;Texture2D&gt;.LoadTexture（FGL 的 patch 所在）載入。
    /// </summary>
    [TestSuite]
    internal static class TextureDownscaleTests
    {
        private const int OriginalSize = 64;
        private const int CachedSize = 32;

        private static bool ExternalTextureToolActive => ImageOptCompat.IsActive || GraphicsSettingsCompat.IsActive;

        /// <summary>
        /// 降質快取載入的貼圖要與原版載入同一張 PNG 的結果一致（格式、濾波、mipmap；壓縮後的濾波設定也以原版為準）。
        /// 唯一刻意的差異：/UI/ 底下的貼圖不產生 mipmap，縮小顯示時才不會糊。
        /// </summary>
        [Test]
        public static void CachedTextureIsServedLikeVanilla()
        {
            foreach (var isUi in new[] { true, false })
            {
                using var probe = new Probe(isUi);
                int hitsBefore = ModContentLoaderTexture2D_LoadTexture_Patch.cacheLoadHits;

                var texture = probe.Load();

                if (ExternalTextureToolActive)
                {
                    // Graphics Settings+／Image Opt 接手貼圖載入時，FGL 必須完全讓開。
                    Assert.That(texture.width).Is.EqualTo(OriginalSize);
                    Assert.That(ModContentLoaderTexture2D_LoadTexture_Patch.cacheLoadHits).Is.EqualTo(hitsBefore);
                    continue;
                }
                Assert.That(ModContentLoaderTexture2D_LoadTexture_Patch.cacheLoadHits).Is.EqualTo(hitsBefore + 1);
                Assert.That(texture.name).Is.EqualTo(Probe.FileNameWithoutExtension);

                var vanilla = probe.LoadCacheFileDirectly();
                Assert.That(texture.width).Is.EqualTo(vanilla.width);
                Assert.That(texture.height).Is.EqualTo(vanilla.height);
                Assert.That(texture.format.ToString()).Is.EqualTo(vanilla.format.ToString());
                Assert.That(texture.filterMode.ToString()).Is.EqualTo(vanilla.filterMode.ToString());
                Assert.That(texture.anisoLevel).Is.EqualTo(vanilla.anisoLevel);
                Assert.That(texture.mipmapCount).Is.EqualTo(isUi ? 1 : vanilla.mipmapCount);

                // 同一路徑再載入一次要沿用同一個實體，不能重新讀檔。
                Assert.That(ReferenceEquals(probe.Load(), texture)).Is.True();
            }
        }

        /// <summary>原始檔被 mod 更新（大小或時間變了）後，舊的降質快取必須作廢、改載原始貼圖。</summary>
        [Test]
        public static void StaleCacheFallsBackToOriginalTexture()
        {
            if (ExternalTextureToolActive) return;

            using var probe = new Probe(isUi: false);
            File.SetLastWriteTimeUtc(probe.OriginalPath, DateTime.UtcNow.AddHours(1));
            int failuresBefore = ModContentLoaderTexture2D_LoadTexture_Patch.cacheLoadFailures;

            var texture = probe.Load();

            Assert.That(texture.width).Is.EqualTo(OriginalSize);
            Assert.That(FasterGameLoadingMod.Instance.CacheManager.ResizedTextureCache.ContainsKey(probe.OriginalPath)).Is.False();
            // 過期屬正常失效，不是載入失敗。
            Assert.That(ModContentLoaderTexture2D_LoadTexture_Patch.cacheLoadFailures).Is.EqualTo(failuresBefore);
        }

        /// <summary>在隔離 session 的存檔資料夾產生一組原始貼圖與降質快取，結束時全部清掉。</summary>
        private sealed class Probe : IDisposable
        {
            public const string FileNameWithoutExtension = "FglDownscaleProbe";

            private readonly TextureCacheManager cacheManager = FasterGameLoadingMod.Instance.CacheManager;
            private readonly string cachePath;
            private Texture2D loaded;
            private Texture2D reference;

            public string OriginalPath { get; }

            public Probe(bool isUi)
            {
                var directory = Path.Combine(GenFilePaths.SaveDataFolderPath, "FglInGameTests", "Textures", isUi ? "UI" : "Things");
                Directory.CreateDirectory(directory);
                OriginalPath = Path.Combine(directory, FileNameWithoutExtension + ".png");
                WritePng(OriginalPath, OriginalSize);

                cachePath = cacheManager.GetCachePath(OriginalPath);
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
                WritePng(cachePath, CachedSize);
                cacheManager.SetCacheEntry(OriginalPath, cachePath);
                ModContentLoaderTexture2D_LoadTexture_Patch.savedTextures.TryRemove(OriginalPath, out _);
            }

            public Texture2D Load() => loaded = LoadTexture(OriginalPath);

            /// <summary>對照組：直接載入快取 PNG 本身。它沒有快取項目，FGL 放行，由原版流程載入。</summary>
            public Texture2D LoadCacheFileDirectly() => reference = LoadTexture(cachePath);

            public void Dispose()
            {
                cacheManager.RemoveCachedTexturePath(OriginalPath);
                ModContentLoaderTexture2D_LoadTexture_Patch.savedTextures.TryRemove(OriginalPath, out _);
                ModContentLoaderTexture2D_LoadTexture_Patch.savedTextures.TryRemove(cachePath, out _);
                if (loaded != null) UnityEngine.Object.Destroy(loaded);
                if (reference != null) UnityEngine.Object.Destroy(reference);
                File.Delete(OriginalPath);
                File.Delete(cachePath);
            }

            private static Texture2D LoadTexture(string path)
            {
                var file = AbstractFilesystem.GetDirectory(Path.GetDirectoryName(path)).GetFile(Path.GetFileName(path));
                var texture = ModContentLoader<Texture2D>.LoadTexture(file);
                if (texture == null) throw new AssertionException($"LoadTexture returned null for {path}");
                return texture;
            }

            private static void WritePng(string path, int size)
            {
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false);
                try
                {
                    var pixels = new Color32[size * size];
                    for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32((byte)i, 128, 64, 255);
                    texture.SetPixels32(pixels);
                    texture.Apply();
                    File.WriteAllBytes(path, texture.EncodeToPNG());
                }
                finally
                {
                    UnityEngine.Object.Destroy(texture);
                }
            }
        }
    }
}
