using NUnit.Framework;
using RimWorld;
using System.Runtime.Serialization;
using UnityEngine;
using Verse;

namespace FasterGameLoading.Tests.TextureDownscaler
{
    [TestFixture]
    public class TextureResizerTests
    {
        [Test]
        public void TryGetResizeTarget_TerrainUsesTerrainTarget()
        {
            int targetSize;
            var result = TextureResizer.TryGetResizeTarget(
                null, Uninitialized<TerrainDef>(), out targetSize);

            Assert.That(result, Is.True);
            Assert.That(targetSize, Is.EqualTo(1024));
        }

        [Test]
        public void TryGetResizeTarget_SmallBuildingUsesBuildingTarget()
        {
            var def = Uninitialized<ThingDef>();
            def.building = Uninitialized<BuildingProperties>();
            def.graphicData = Uninitialized<GraphicData>();
            def.graphicData.drawSize = new Vector2(4f, 4f);
            int targetSize;

            var result = TextureResizer.TryGetResizeTarget(null, def, out targetSize);

            Assert.That(result, Is.True);
            Assert.That(targetSize, Is.EqualTo(256));
        }

        [Test]
        public void TryGetResizeTarget_LargeThingIsNotSelected()
        {
            var def = Uninitialized<ThingDef>();
            def.building = Uninitialized<BuildingProperties>();
            def.graphicData = Uninitialized<GraphicData>();
            def.graphicData.drawSize = new Vector2(5f, 4f);
            int targetSize;

            var result = TextureResizer.TryGetResizeTarget(null, def, out targetSize);

            Assert.That(result, Is.False);
            Assert.That(targetSize, Is.EqualTo(0));
        }

        [Test]
        public void TryGetResizeTarget_WithoutGraphicDataIsNotSelected()
        {
            int targetSize;

            var result = TextureResizer.TryGetResizeTarget(
                null, Uninitialized<ThingDef>(), out targetSize);

            Assert.That(result, Is.False);
            Assert.That(targetSize, Is.EqualTo(0));
        }

        [Test]
        public void DestroyTemporaryUnityObject_WithNullIsNoOp()
        {
            Assert.DoesNotThrow(() => TextureResizer.DestroyTemporaryUnityObject(null));
        }

        private static T Uninitialized<T>()
        {
            return (T)FormatterServices.GetUninitializedObject(typeof(T));
        }
    }
}
