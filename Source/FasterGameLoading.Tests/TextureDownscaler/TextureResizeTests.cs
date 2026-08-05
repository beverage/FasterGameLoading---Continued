using NUnit.Framework;
using RimWorld;
using System.Runtime.Serialization;
using Verse;

namespace FasterGameLoading.Tests.TextureDownscaler
{
    [TestFixture]
    public class TextureResizeTests
    {
        [Test]
        public void GetTextureType_RecognizesRepresentativeDefCategories()
        {
            var building = Uninitialized<ThingDef>();
            building.building = Uninitialized<BuildingProperties>();
            var apparel = Uninitialized<ThingDef>();
            apparel.apparel = Uninitialized<ApparelProperties>();
            var plant = Uninitialized<ThingDef>();
            plant.plant = Uninitialized<PlantProperties>();
            plant.category = ThingCategory.Plant;
            plant.thingClass = typeof(Plant);
            var projectile = Uninitialized<ThingDef>();
            projectile.projectile = Uninitialized<ProjectileProperties>();
            var mote = Uninitialized<ThingDef>();
            mote.category = ThingCategory.Mote;
            var filth = Uninitialized<ThingDef>();
            filth.category = ThingCategory.Filth;
            var item = Uninitialized<ThingDef>();
            item.category = ThingCategory.Item;
            var pawn = Uninitialized<ThingDef>();
            pawn.race = Uninitialized<RaceProperties>();

            Assert.That(TextureResize.GetTextureType(building), Is.EqualTo(TextureResize.TextureType.Building));
            Assert.That(TextureResize.GetTextureType(apparel), Is.EqualTo(TextureResize.TextureType.Apparel));
            Assert.That(TextureResize.GetTextureType(plant), Is.EqualTo(TextureResize.TextureType.Plant));
            Assert.That(TextureResize.GetTextureType(projectile), Is.EqualTo(TextureResize.TextureType.Projectile));
            Assert.That(TextureResize.GetTextureType(mote), Is.EqualTo(TextureResize.TextureType.Mote));
            Assert.That(TextureResize.GetTextureType(filth), Is.EqualTo(TextureResize.TextureType.Filth));
            Assert.That(TextureResize.GetTextureType(item), Is.EqualTo(TextureResize.TextureType.Item));
            Assert.That(TextureResize.GetTextureType(pawn), Is.EqualTo(TextureResize.TextureType.Pawn));
        }

        [Test]
        public void GetTextureType_WithNoRecognizedPropertiesReturnsNone()
        {
            Assert.That(
                TextureResize.GetTextureType(Uninitialized<ThingDef>()),
                Is.EqualTo(TextureResize.TextureType.None));
        }

        private static T Uninitialized<T>()
        {
            return (T)FormatterServices.GetUninitializedObject(typeof(T));
        }
    }
}
