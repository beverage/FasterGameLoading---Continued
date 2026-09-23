using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using NUnit.Framework;
using UnityEngine;
using Verse;

namespace FasterGameLoading.Tests.Compatibility
{
    [TestFixture]
    public class BakingSkipListTests
    {
        private bool previousStaticAtlasesBaking;
        private HashSet<string> targetModRoots;
        private FieldInfo rootsInitializedField;

        [SetUp]
        public void SetUp()
        {
            previousStaticAtlasesBaking = FasterGameLoadingSettings.StaticAtlasesBaking;
            CacheResetter.ResetAll();
            ModContentLoaderTexture2D_LoadTexture_Patch.skippedBakingTextures.Clear();

            targetModRoots = (HashSet<string>)AccessTools.Field(typeof(AdaptiveBakingSkipList), "targetModRoots")?.GetValue(null);
            rootsInitializedField = AccessTools.Field(typeof(AdaptiveBakingSkipList), "rootsInitialized");
        }

        [TearDown]
        public void TearDown()
        {
            FasterGameLoadingSettings.StaticAtlasesBaking = previousStaticAtlasesBaking;
            CacheResetter.ResetAll();
            ModContentLoaderTexture2D_LoadTexture_Patch.skippedBakingTextures.Clear();
        }

        [TestCase(null)]
        [TestCase("")]
        public void IsProtectedModTexturePath_WhenPathIsNullOrEmpty_ReturnsFalse(string path)
        {
            Assert.That(AdaptiveBakingSkipList.IsProtectedModTexturePath(path), Is.False);
        }

        [Test]
        public void IsProtectedModTexturePath_MatchesNormalizedPathAndIgnoresCase()
        {
            targetModRoots.Clear();
            targetModRoots.Add("C:/Steam/RimWorld/Mods/AlienRaces");
            rootsInitializedField?.SetValue(null, value: true);

            // 正斜線與反斜線比對
            Assert.That(AdaptiveBakingSkipList.IsProtectedModTexturePath(@"C:\Steam\RimWorld\Mods\AlienRaces\Textures\Pawn.png"), Is.True);
            Assert.That(AdaptiveBakingSkipList.IsProtectedModTexturePath("C:/Steam/RimWorld/Mods/AlienRaces/Textures/Pawn.png"), Is.True);

            // 大小寫不敏感
            Assert.That(AdaptiveBakingSkipList.IsProtectedModTexturePath(@"c:\steam\rimworld\mods\alienraces\textures\pawn.png"), Is.True);

            // Root 本身
            Assert.That(AdaptiveBakingSkipList.IsProtectedModTexturePath("C:/Steam/RimWorld/Mods/AlienRaces"), Is.True);

            // 非子路徑（前綴相似但非同目錄）
            Assert.That(AdaptiveBakingSkipList.IsProtectedModTexturePath("C:/Steam/RimWorld/Mods/AlienRacesExtended/Textures/Pawn.png"), Is.False);

            // 其他 Mod 路徑
            Assert.That(AdaptiveBakingSkipList.IsProtectedModTexturePath("C:/Steam/RimWorld/Mods/OtherMod/Textures/Pawn.png"), Is.False);
        }

        [Test]
        public void ShouldSkipBaking_WhenStaticAtlasesBakingIsDisabled_AlwaysReturnsFalse()
        {
            targetModRoots.Clear();
            targetModRoots.Add("C:/Mods/TargetMod");
            rootsInitializedField?.SetValue(null, value: true);

            FasterGameLoadingSettings.StaticAtlasesBaking = false;
            Assert.That(AdaptiveBakingSkipList.ShouldSkipBaking("C:/Mods/TargetMod/Textures/Body.png"), Is.False);

            FasterGameLoadingSettings.StaticAtlasesBaking = true;
            Assert.That(AdaptiveBakingSkipList.ShouldSkipBaking("C:/Mods/TargetMod/Textures/Body.png"), Is.True);
        }

        [Test]
        public void Prepare_ReflectsStaticAtlasesBakingSetting()
        {
            FasterGameLoadingSettings.StaticAtlasesBaking = false;
            Assert.That(AdaptiveBakingSkipList.Prepare(), Is.False);

            FasterGameLoadingSettings.StaticAtlasesBaking = true;
            Assert.That(AdaptiveBakingSkipList.Prepare(), Is.True);
        }

        [Test]
        public void Prefix_WhenTexturesAreNotInSkipList_ReturnsTrue()
        {
            Assert.That(AdaptiveBakingSkipList.Prefix(TextureAtlasGroup.Building, texture: null, mask: null), Is.True);
        }

        [Test]
        public void Prefix_WhenTextureOrMaskIsInSkipList_ReturnsFalse()
        {
            var tex1 = (Texture2D)FormatterServices.GetUninitializedObject(typeof(Texture2D));
            var tex2 = (Texture2D)FormatterServices.GetUninitializedObject(typeof(Texture2D));

            ModContentLoaderTexture2D_LoadTexture_Patch.skippedBakingTextures[tex1] = true;

            // texture in skip list
            Assert.That(AdaptiveBakingSkipList.Prefix(TextureAtlasGroup.Building, tex1, mask: null), Is.False);

            // mask in skip list
            Assert.That(AdaptiveBakingSkipList.Prefix(TextureAtlasGroup.Building, texture: null, mask: tex1), Is.False);

            // neither in skip list
            Assert.That(AdaptiveBakingSkipList.Prefix(TextureAtlasGroup.Building, tex2, mask: null), Is.True);
        }

        [Test]
        public void IsProtectedModTexturePath_ConcurrentAccess_DoesNotThrow()
        {
            targetModRoots?.Add("c:/mods/targetmod");
            rootsInitializedField?.SetValue(null, true);

            Assert.DoesNotThrow(() =>
            {
                System.Threading.Tasks.Parallel.For(0, 100, i =>
                {
                    AdaptiveBakingSkipList.IsProtectedModTexturePath($"c:/mods/targetmod/textures/tex_{i}.png");
                    AdaptiveBakingSkipList.IsProtectedModTexturePath($"c:/mods/othermod/textures/tex_{i}.png");
                });
            });
        }

        [Test]
        public void InitializeModRoots_PopulatesTargetModRootsFromRunningMods()
        {
            targetModRoots?.Clear();
            rootsInitializedField?.SetValue(null, false);

            var mod = (ModContentPack)FormatterServices.GetUninitializedObject(typeof(ModContentPack));
            AccessTools.Field(typeof(ModContentPack), "rootDirInt").SetValue(mod, new System.IO.DirectoryInfo(@"C:\TestModRoot\"));
            AccessTools.Field(typeof(ModContentPack), "packageIdInt").SetValue(mod, "erdelf.humanoidalienraces");

            var runningModsField = AccessTools.Field(typeof(LoadedModManager), "runningMods");
            var originalRunning = runningModsField?.GetValue(null);
            try
            {
                runningModsField?.SetValue(null, new List<ModContentPack> { mod });
                AdaptiveBakingSkipList.InitializeModRoots();

                Assert.That(targetModRoots, Does.Contain("C:/TestModRoot"));
            }
            finally
            {
                runningModsField?.SetValue(null, originalRunning);
            }
        }

        [Test]
        public void InitializeModRoots_WhenModRootDirThrows_LogsError()
        {
            targetModRoots?.Clear();
            rootsInitializedField?.SetValue(null, false);

            var mod = (ModContentPack)FormatterServices.GetUninitializedObject(typeof(ModContentPack));
            // 不設定 rootDirInt，使 RootDir getter 存取時拋出 NullReferenceException，測試內部 catch 區塊
            AccessTools.Field(typeof(ModContentPack), "packageIdInt").SetValue(mod, "erdelf.humanoidalienraces");

            var runningModsField = AccessTools.Field(typeof(LoadedModManager), "runningMods");
            var originalRunning = runningModsField?.GetValue(null);
            try
            {
                runningModsField?.SetValue(null, new List<ModContentPack> { mod });
                Assert.DoesNotThrow(() => AdaptiveBakingSkipList.InitializeModRoots());
            }
            finally
            {
                runningModsField?.SetValue(null, originalRunning);
            }
        }

        [Test]
        public void InitializeModRoots_WhenModIsNotDirectTarget_ChecksDependencies()
        {
            targetModRoots?.Clear();
            rootsInitializedField?.SetValue(null, false);

            var mod = (ModContentPack)FormatterServices.GetUninitializedObject(typeof(ModContentPack));
            AccessTools.Field(typeof(ModContentPack), "rootDirInt").SetValue(mod, new System.IO.DirectoryInfo(@"C:\OtherModRoot\"));
            AccessTools.Field(typeof(ModContentPack), "packageIdInt").SetValue(mod, "some.other.mod");

            var runningModsField = AccessTools.Field(typeof(LoadedModManager), "runningMods");
            var originalRunning = runningModsField?.GetValue(null);
            try
            {
                runningModsField?.SetValue(null, new List<ModContentPack> { mod });
                AdaptiveBakingSkipList.InitializeModRoots();

                Assert.That(targetModRoots, Does.Not.Contain("C:/OtherModRoot"));
            }
            finally
            {
                runningModsField?.SetValue(null, originalRunning);
            }
        }
    }
}
