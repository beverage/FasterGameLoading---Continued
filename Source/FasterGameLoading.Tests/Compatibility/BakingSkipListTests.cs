using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
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

        // ── 以下為覆蓋 IsTargetModTexture 的 get_name 路徑所用 ──
        // texture.name 走 UnityEngine.Object.get_name()（ECall），非 Unity 環境會拋例外。
        // 直接 patch ECall 會失敗（ECall methods must be packaged into a system module），
        // 故仿照 AdaptiveAtlasBakerTests 手法，transpile IsTargetModTexture 本身，
        // 把 IL 中的 get_name 呼叫替換為 mock。
        private static Harmony harmony;

        public static class MockTextureHelper
        {
            public static readonly IDictionary<Texture2D, string> TextureNames = new Dictionary<Texture2D, string>();

            public static Texture2D CreateTexture(string name)
            {
                var tex = (Texture2D)FormatterServices.GetUninitializedObject(typeof(Texture2D));
                TextureNames[tex] = name;
                return tex;
            }

            public static string GetName(UnityEngine.Object obj)
            {
                if (obj is Texture2D t && TextureNames.TryGetValue(t, out var n)) return n;
                return "MockTex";
            }

            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var mockGetName = AccessTools.Method(typeof(MockTextureHelper), nameof(GetName));
                foreach (var inst in instructions)
                {
                    if ((inst.opcode == OpCodes.Call || inst.opcode == OpCodes.Callvirt)
                        && inst.operand is MethodInfo m && string.Equals(m.Name, "get_name", StringComparison.Ordinal))
                    {
                        yield return new CodeInstruction(OpCodes.Call, mockGetName);
                        continue;
                    }
                    yield return inst;
                }
            }
        }

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            harmony = new Harmony("FasterGameLoading.Tests.BakingSkipList");
            var isTargetModTexture = AccessTools.Method(typeof(AdaptiveBakingSkipList), "IsTargetModTexture");
            if (isTargetModTexture != null)
            {
                var transpilerMethod = AccessTools.Method(typeof(MockTextureHelper), nameof(MockTextureHelper.Transpiler));
                harmony.Patch(isTargetModTexture, transpiler: new HarmonyMethod(transpilerMethod));
            }
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            harmony?.UnpatchAll("FasterGameLoading.Tests.BakingSkipList");
            MockTextureHelper.TextureNames.Clear();
        }

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
        public void Prefix_WhenTextureNameMatchesSkippedNames_ReturnsFalse()
        {
            // 實體不在 skippedBakingTextures，但 texture.name 落在 skippedBakingTextureNames。
            // 此路徑會走 IsTargetModTexture 的 get_name 分支（transpiler 已 mock）。
            var tex = MockTextureHelper.CreateTexture("TargetTexture.png");
            ModContentLoaderTexture2D_LoadTexture_Patch.skippedBakingTextureNames.TryAdd("TargetTexture.png", 0);

            Assert.That(AdaptiveBakingSkipList.Prefix(TextureAtlasGroup.Building, tex, mask: null), Is.False);
        }

        [Test]
        public void Prefix_WhenMaskNameMatchesSkippedNames_ReturnsFalse()
        {
            // 遮罩名命中 skippedBakingTextureNames：mask 非 null 時走相同 get_name 分支
            var mask = MockTextureHelper.CreateTexture("TargetMask.png");
            ModContentLoaderTexture2D_LoadTexture_Patch.skippedBakingTextureNames.TryAdd("TargetMask.png", 0);

            Assert.That(AdaptiveBakingSkipList.Prefix(TextureAtlasGroup.Building, texture: null, mask: mask), Is.False);
        }

        [Test]
        public void Prefix_WhenTextureNameDoesNotMatchSkippedNames_ReturnsTrue()
        {
            // 實體不在 skip list、名字也不命中 → IsTargetModTexture 回傳 false，Prefix 放行
            var tex = MockTextureHelper.CreateTexture("VanillaTex.png");

            Assert.That(AdaptiveBakingSkipList.Prefix(TextureAtlasGroup.Building, tex, mask: null), Is.True);
        }

        [Test]
        public void Prefix_WhenTextureNameGetterThrows_SafelyReturnsTrue()
        {
            // 暫時移除 transpiler mock，走真實 texture.name (ECall)，非 Unity 運行環境下會拋例外，
            // IsTargetModTexture 需安全降級為 false，Prefix 因此放行（不跳過原方法）。
            var isTargetModTexture = AccessTools.Method(typeof(AdaptiveBakingSkipList), "IsTargetModTexture");
            harmony.Unpatch(isTargetModTexture, HarmonyPatchType.Transpiler, harmony.Id);
            try
            {
                var tex = (Texture2D)FormatterServices.GetUninitializedObject(typeof(Texture2D));
                Assert.That(AdaptiveBakingSkipList.Prefix(TextureAtlasGroup.Building, tex, mask: null), Is.True);
            }
            finally
            {
                var transpilerMethod = AccessTools.Method(typeof(MockTextureHelper), nameof(MockTextureHelper.Transpiler));
                harmony.Patch(isTargetModTexture, transpiler: new HarmonyMethod(transpilerMethod));
            }
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
