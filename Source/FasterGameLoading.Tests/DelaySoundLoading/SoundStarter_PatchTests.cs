using System;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using NUnit.Framework;
using Verse.Sound;

namespace FasterGameLoading.Tests.DelaySoundLoading
{
    [TestFixture]
    public class SoundStarter_PatchTests
    {
        private static FieldInfo unpatchedField;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            unpatchedField = AccessTools.Field(typeof(SoundStarter_Patch), "unpatched");

            var fglModHarmonyProp = typeof(FasterGameLoadingMod).GetProperty(
                nameof(FasterGameLoadingMod.harmony), BindingFlags.Public | BindingFlags.Static);
            fglModHarmonyProp?.SetValue(null, new Harmony("FasterGameLoadingMod.SoundStarter_PatchTests"), null);
        }

        [SetUp]
        public void SetUp()
        {
            SoundStarter_Patch.ResetUnpatchedStatus();
        }

        [TearDown]
        public void TearDown()
        {
            SoundStarter_Patch.ResetUnpatchedStatus();
        }

        [Test]
        public void PlayOneShotOnCamera_Patch_ReturnsFalse()
        {
            var method = AccessTools.Method(typeof(SoundStarter_Patch), "PlayOneShotOnCamera_Patch");
            Assert.That(method, Is.Not.Null);

            var result = method.Invoke(null, null);
            Assert.That(result, Is.False);
        }

        [Test]
        public void PlayOneShot_Patch_ReturnsFalse()
        {
            var method = AccessTools.Method(typeof(SoundStarter_Patch), "PlayOneShot_Patch");
            Assert.That(method, Is.Not.Null);

            var result = method.Invoke(null, null);
            Assert.That(result, Is.False);
        }

        [Test]
        public void TrySpawnSustainer_Patch_SetsResultToNullAndReturnsFalse()
        {
            var method = AccessTools.Method(typeof(SoundStarter_Patch), "TrySpawnSustainer_Patch");
            Assert.That(method, Is.Not.Null);

            var sustainer = (Sustainer)FormatterServices.GetUninitializedObject(typeof(Sustainer));
            var args = new object[] { sustainer };

            var result = (bool)method.Invoke(null, args);

            Assert.That(result, Is.False);
            Assert.That(args[0], Is.Null);
        }

        [Test]
        public void TryPlay_Patch_ReturnsFalse()
        {
            var method = AccessTools.Method(typeof(SoundStarter_Patch), "TryPlay_Patch");
            Assert.That(method, Is.Not.Null);

            var result = method.Invoke(null, null);
            Assert.That(result, Is.False);
        }

        [Test]
        public void Unpatch_And_ResetUnpatchedStatus_OperatesCorrectlyAndIdempotently()
        {
            Assert.That((bool)unpatchedField.GetValue(null), Is.False);

            SoundStarter_Patch.Unpatch();
            Assert.That((bool)unpatchedField.GetValue(null), Is.True);

            // Second call is idempotent
            SoundStarter_Patch.Unpatch();
            Assert.That((bool)unpatchedField.GetValue(null), Is.True);

            SoundStarter_Patch.ResetUnpatchedStatus();
            Assert.That((bool)unpatchedField.GetValue(null), Is.False);
        }
    }
}
