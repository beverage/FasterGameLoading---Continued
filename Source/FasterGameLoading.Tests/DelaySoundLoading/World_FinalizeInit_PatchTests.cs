using NUnit.Framework;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace FasterGameLoading.Tests.DelaySoundLoading
{
    [TestFixture]
    public class World_FinalizeInit_PatchTests
    {
        [Test]
        public void Patch_IsRegisteredForWorldFinalizeInit()
        {
            var patch = (HarmonyPatch)System.Attribute.GetCustomAttribute(
                typeof(World_FinalizeInit_Patch), typeof(HarmonyPatch));

            Assert.That(patch, Is.Not.Null);
            Assert.That(typeof(World_FinalizeInit_Patch).GetMethod("Postfix"), Is.Not.Null);
        }
    }
}
