using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace FasterGameLoading
{
    /// <summary>
    /// 負責底層的紋理轉換、大小判定與 Unity 暫存資源釋放。
    /// </summary>
    public static class TextureResizer
    {
        /// <summary>各紋理類型的降質目標尺寸（較長邊會縮放至此尺寸）。</summary>
        internal static readonly Dictionary<TextureResize.TextureType, int> targetSizes = new Dictionary<TextureResize.TextureType, int>
        {
            { TextureResize.TextureType.Building, 256 },
            { TextureResize.TextureType.Pawn, 256 },
            { TextureResize.TextureType.Apparel, 128 },
            { TextureResize.TextureType.Weapon, 128 },
            { TextureResize.TextureType.Item, 128 },
            { TextureResize.TextureType.Plant, 128 },
            { TextureResize.TextureType.Tree, 256 },
            { TextureResize.TextureType.Terrain, 1024 },
        };

        /// <summary>
        /// 判斷是否應該對此紋理進行降質。
        /// 只對 drawSize 總和 ≤ 8 的 Def 進行降質（避免影響大型物件外觀）。
        /// </summary>
        public static bool TryGetResizeTarget(Texture texture, BuildableDef def, out int targetSize)
        {
            if (def is TerrainDef)
            {
                targetSize = targetSizes[TextureResize.TextureType.Terrain];
                return true;
            }

            if (def is ThingDef thingDef && thingDef.graphicData != null
                && thingDef.graphicData.drawSize.x + thingDef.graphicData.drawSize.y <= 8)
            {
                return targetSizes.TryGetValue(TextureResize.GetTextureType(thingDef), out targetSize);
            }

            targetSize = 0;
            return false;
        }

        /// <summary>
        /// 安全銷毀暫存 Unity 物件。先嘗試 DestroyImmediate，失敗時改用 Destroy。
        /// </summary>
        public static void DestroyTemporaryUnityObject(UnityEngine.Object obj)
        {
            if (obj == null) return;
            try
            {
                UnityEngine.Object.DestroyImmediate(obj);
            }
            catch (Exception ex)
            {
                if (FasterGameLoadingSettings.VerboseLogging)
                {
                    FGLLog.Warning($"DestroyImmediate failed, falling back to Destroy: {ex.Message}");
                }
                try
                {
                    UnityEngine.Object.Destroy(obj);
                }
                catch
                {
                    // Ignore double fault
                }
            }
        }

        /// <summary>
        /// 使用 RenderTexture 將來源紋理縮放到目標尺寸，輸出為 PNG 位元組陣列。
        /// 雙線性 Blit 每個輸出像素只取樣 2×2 個來源像素，一次縮小超過 2 倍會跳過大部分像素而產生鋸齒；
        /// 因此先逐次減半到目標的 2 倍以內，再做最後一次縮放。
        /// </summary>
        public static byte[] ResizeTextureToPng(Texture source, int width, int height)
        {
            var previous = RenderTexture.active;
            var renderTexture = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            RenderTexture intermediate = null;
            Texture2D readable = null;

            try
            {
                Texture current = source;
                int currentWidth = source.width;
                int currentHeight = source.height;
                while (currentWidth / 2 >= width && currentHeight / 2 >= height)
                {
                    currentWidth /= 2;
                    currentHeight /= 2;
                    var halved = RenderTexture.GetTemporary(currentWidth, currentHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                    halved.filterMode = FilterMode.Bilinear;
                    Graphics.Blit(current, halved);
                    if (intermediate != null)
                    {
                        RenderTexture.ReleaseTemporary(intermediate);
                    }
                    intermediate = halved;
                    current = halved;
                }

                Graphics.Blit(current, renderTexture);
                RenderTexture.active = renderTexture;
                readable = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false);
                readable.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                readable.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                return readable.EncodeToPNG();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(renderTexture);
                if (intermediate != null) RenderTexture.ReleaseTemporary(intermediate);
                if (readable != null) DestroyTemporaryUnityObject(readable);
            }
        }
    }
}
