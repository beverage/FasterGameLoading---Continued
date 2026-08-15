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
            ModContentLoaderTexture2D_LoadTexture_Patch.skippedBakingTextureNames.Clear();

            targetModRoots = (HashSet<string>)AccessTools.Field(typeof(AdaptiveBakingSkipList), "targetModRoots")?.GetValue(null);
            rootsInitializedField = AccessTools.Field(typeof(AdaptiveBakingSkipList), "rootsInitialized");
        }

        [TearDown]
        public void TearDown()
        {
            FasterGameLoadingSettings.StaticAtlasesBaking = previousStaticAtlasesBaking;
            CacheResetter.ResetAll();
            ModContentLoaderTexture2D_LoadTexture_Patch.skippedBakingTextures.Clear();
            ModContentLoaderTexture2D_LoadTexture_Patch.skippedBakingTextureNames.Clear();
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
            rootsInitializedField?.SetValue(null, true);

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
            rootsInitializedField?.SetValue(null, true);

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
            Assert.That(AdaptiveBakingSkipList.Prefix(TextureAtlasGroup.Building, null, null), Is.True);
        }

        [Test]
        public void Prefix_WhenTextureOrMaskIsInSkipList_ReturnsFalse()
        {
            var tex1 = (Texture2D)FormatterServices.GetUninitializedObject(typeof(Texture2D));
            var tex2 = (Texture2D)FormatterServices.GetUninitializedObject(typeof(Texture2D));

            ModContentLoaderTexture2D_LoadTexture_Patch.skippedBakingTextures[tex1] = true;

            // texture in skip list
            Assert.That(AdaptiveBakingSkipList.Prefix(TextureAtlasGroup.Building, tex1, null), Is.False);

            // mask in skip list
            Assert.That(AdaptiveBakingSkipList.Prefix(TextureAtlasGroup.Building, null, tex1), Is.False);

            // neither in skip list
            Assert.That(AdaptiveBakingSkipList.Prefix(TextureAtlasGroup.Building, tex2, null), Is.True);
        }
    }
}
