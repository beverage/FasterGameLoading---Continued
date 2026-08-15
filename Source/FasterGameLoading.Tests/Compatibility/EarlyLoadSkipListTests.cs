using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization;
using HarmonyLib;
using NUnit.Framework;
using Verse;

namespace FasterGameLoading.Tests.Compatibility
{
    [TestFixture]
    public class EarlyLoadSkipListTests
    {
        [TestCase("Ayameduki.Harpy", ExpectedResult = true)]
        [TestCase("ayameduki.core", ExpectedResult = true)]
        [TestCase("WRK.RaceMod", ExpectedResult = true)]
        [TestCase("wrk.submod", ExpectedResult = true)]
        [TestCase("erdelf.HumanoidAlienRaces", ExpectedResult = true)]
        [TestCase("ERDELF.HUMANOIDALIENRACES", ExpectedResult = true)]
        [TestCase("Ludeon.RimWorld", ExpectedResult = false)]
        [TestCase("OskarPotocki.VanillaFactionsExpanded", ExpectedResult = false)]
        [TestCase("", ExpectedResult = false)]
        [TestCase(null, ExpectedResult = false)]
        public bool ShouldSkip_WithPackageId_MatchesExpected(string packageId)
        {
            return EarlyLoadSkipList.ShouldSkip(packageId);
        }

        [Test]
        public void ShouldSkip_WithHARMetaDataDependency_ReturnsTrue()
        {
            var metaDataWithHar = new MockMetaData(new MockDependency("erdelf.HumanoidAlienRaces"));
            var metaDataWithoutHar = new MockMetaData(new MockDependency("other.dependency"));

            Assert.That(EarlyLoadSkipList.ShouldSkip("Custom.RaceMod", metaDataWithHar), Is.True);
            Assert.That(EarlyLoadSkipList.ShouldSkip("Custom.RaceMod", metaDataWithoutHar), Is.False);
            Assert.That(EarlyLoadSkipList.ShouldSkip("Custom.RaceMod", null), Is.False);
        }

        [Test]
        public void ShouldSkip_WithNullModContentPack_ReturnsFalse()
        {
            Assert.That(EarlyLoadSkipList.ShouldSkip((ModContentPack)null), Is.False);
        }

        [Test]
        public void ShouldSkip_WithModContentPack_CachesResult()
        {
            var mod = (ModContentPack)FormatterServices.GetUninitializedObject(typeof(ModContentPack));
            var packageIdField = AccessTools.Field(typeof(ModContentPack), "packageIdInt")
                ?? AccessTools.Field(typeof(ModContentPack), "packageId");

            if (packageIdField != null)
            {
                packageIdField.SetValue(mod, "wrk.custommod");
                Assert.That(EarlyLoadSkipList.ShouldSkip(mod), Is.True);
                // 第二次呼叫驗證快取
                Assert.That(EarlyLoadSkipList.ShouldSkip(mod), Is.True);

                var normalMod = (ModContentPack)FormatterServices.GetUninitializedObject(typeof(ModContentPack));
                packageIdField.SetValue(normalMod, "normal.mod");
                Assert.That(EarlyLoadSkipList.ShouldSkip(normalMod), Is.False);
                Assert.That(EarlyLoadSkipList.ShouldSkip(normalMod), Is.False);
            }
            else
            {
                // 若欄位為 null，驗證 null packageId 的 ModContentPack 回傳 false 且不崩潰
                Assert.That(EarlyLoadSkipList.ShouldSkip(mod), Is.False);
                Assert.That(EarlyLoadSkipList.ShouldSkip(mod), Is.False);
            }
        }

        private sealed class MockMetaData
        {
            public IEnumerable Dependencies { get; }

            public MockMetaData(params object[] dependencies)
            {
                Dependencies = new List<object>(dependencies);
            }
        }

        private sealed class MockDependency
        {
            public string PackageId { get; }

            public MockDependency(string packageId)
            {
                PackageId = packageId;
            }
        }
    }
}
