using NUnit.Framework;
using UnityEngine;
using Verse;

namespace FasterGameLoading.Tests.DelayGraphicAndIconLoading
{
    [TestFixture]
    public class GraphicData_Init_PatchTests
    {
        [SetUp]
        public void SetUp()
        {
            GraphicData_Init_Patch.savedGraphics.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            GraphicData_Init_Patch.savedGraphics.Clear();
        }

        [Test]
        public void IsSameGraphicData_WithEquivalentDefaultsReturnsTrue()
        {
            var current = new GraphicData();
            var other = new GraphicData();

            Assert.That(GraphicData_Init_Patch.IsSameGraphicData(current, other), Is.True);
        }

        [Test]
        public void IsSameGraphicData_WhenDrawSizeDiffersReturnsFalse()
        {
            var current = new GraphicData { drawSize = new Vector2(1f, 1f) };
            var other = new GraphicData { drawSize = new Vector2(2f, 1f) };

            Assert.That(GraphicData_Init_Patch.IsSameGraphicData(current, other), Is.False);
        }

        [Test]
        public void Prefix_ForFirstTexturePathAllowsOriginalInitAndRecordsState()
        {
            var data = new GraphicData { texPath = "Textures/Test" };
            bool state;

            var result = GraphicData_Init_Patch.Prefix(data, out state);

            Assert.That(result, Is.True);
            Assert.That(state, Is.True);
            Assert.That(GraphicData_Init_Patch.savedGraphics.ContainsKey("Textures/Test"), Is.True);
        }

        [Test]
        public void Prefix_ForEmptyTexturePathDoesNotCreateCacheEntry()
        {
            var data = new GraphicData { texPath = string.Empty };
            bool state;

            var result = GraphicData_Init_Patch.Prefix(data, out state);

            Assert.That(result, Is.True);
            Assert.That(state, Is.False);
            Assert.That(GraphicData_Init_Patch.savedGraphics, Is.Empty);
        }

    }
}
