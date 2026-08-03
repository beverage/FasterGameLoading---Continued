using System;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace FasterGameLoading
{
    /// <summary>
    /// 讓 FGL 提早內容載入使用 ImageOpt 同步路徑，避免提早啟動原生佇列。
    /// </summary>
    internal static class ImageOptEarlyLoadCoordinator
    {
        private const string TextureLoadPatchTypeName = "ImageOpt.TextureLoadPatch";
        private const string StartedFieldName = "Started";

        private static readonly IDisposable noOpScope = new NoOpScope();
        private static Func<bool> getStarted;
        private static Action<bool> setStarted;
        private static Action<string, Exception> logWarning = (message, ex) => FGLLog.Warning(message, ex);

        private static bool installed;
        private static bool installAttempted;
        private static bool warningLogged;
        private static int warningLogCount;
        private static int syncScopeDepth;
        private static bool syncScopeChangedStarted;

        static ImageOptEarlyLoadCoordinator()
        {
            CacheResetter.Register(ResetScopeState);
        }

        internal static bool IsInstalled => installed;
        internal static bool WarningLogged => warningLogged;
        internal static int WarningLogCount => warningLogCount;

        internal static void TryInstall()
        {
            if (installAttempted) return;
            installAttempted = true;

            if (!IsWindows() || !ImageOptCompat.IsActive) return;

            try
            {
                var textureLoadPatch = AccessTools.TypeByName(TextureLoadPatchTypeName);
                var startedField = AccessTools.Field(textureLoadPatch, StartedFieldName);
                if (startedField == null || startedField.FieldType != typeof(bool))
                {
                    throw new MissingMemberException("ImageOpt TextureLoadPatch.Started was not found.");
                }

                getStarted = CreateStartedGetter(startedField);
                setStarted = CreateStartedSetter(startedField);
                installed = true;
            }
            catch (Exception ex)
            {
                installed = false;
                WarnFailOpen(ex);
            }
        }

        /// <summary>
        /// 讓 FGL 直接觸發的 ReloadContentInt 暫時走 ImageOpt 同步路徑。
        /// </summary>
        internal static IDisposable EnterEarlyLoadSyncScope()
        {
            if (!installed || getStarted == null || setStarted == null) return noOpScope;

            if (syncScopeDepth++ == 0)
            {
                syncScopeChangedStarted = !getStarted();
                if (syncScopeChangedStarted)
                {
                    setStarted(true);
                }
            }

            return new SyncScope();
        }

        private static void ExitEarlyLoadSyncScope()
        {
            if (syncScopeDepth <= 0) return;
            if (--syncScopeDepth != 0) return;

            if (syncScopeChangedStarted)
            {
                setStarted(false);
            }
            syncScopeChangedStarted = false;
        }

        private static Func<bool> CreateStartedGetter(FieldInfo field)
        {
            var method = new DynamicMethod(
                "FGL_ImageOpt_GetStarted",
                typeof(bool),
                Type.EmptyTypes,
                typeof(ImageOptEarlyLoadCoordinator).Module,
                true);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldsfld, field);
            il.Emit(OpCodes.Ret);
            return (Func<bool>)method.CreateDelegate(typeof(Func<bool>));
        }

        private static Action<bool> CreateStartedSetter(FieldInfo field)
        {
            var method = new DynamicMethod(
                "FGL_ImageOpt_SetStarted",
                null,
                new[] { typeof(bool) },
                typeof(ImageOptEarlyLoadCoordinator).Module,
                true);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Stsfld, field);
            il.Emit(OpCodes.Ret);
            return (Action<bool>)method.CreateDelegate(typeof(Action<bool>));
        }

        private static bool IsWindows()
        {
            return Environment.OSVersion.Platform == PlatformID.Win32NT;
        }

        private static void WarnFailOpen(Exception ex)
        {
            if (warningLogged) return;
            warningLogged = true;
            warningLogCount++;
            logWarning(
                "ImageOpt Early Loading synchronization could not be enabled. Early Loading remains active, but ImageOpt missing-ID errors may recur:",
                ex);
        }

        private static void ResetScopeState()
        {
            if (syncScopeDepth > 0 && syncScopeChangedStarted && setStarted != null)
            {
                try
                {
                    setStarted(false);
                }
                catch
                {
                }
            }
            syncScopeDepth = 0;
            syncScopeChangedStarted = false;
        }

        private sealed class SyncScope : IDisposable
        {
            private bool disposed;

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                ExitEarlyLoadSyncScope();
            }
        }

        private sealed class NoOpScope : IDisposable
        {
            public void Dispose()
            {
            }
        }

        #region 測試支援
        internal static void ConfigureForTests(
            Func<bool> startedGetter,
            Action<bool> startedSetter,
            bool enabled = true)
        {
            getStarted = startedGetter;
            setStarted = startedSetter;
            installed = enabled;
            ResetScopeState();
        }

        internal static void ReportInstallFailureForTests(Exception ex)
        {
            installed = false;
            WarnFailOpen(ex);
        }

        internal static void ResetScopeForTests()
        {
            ResetScopeState();
        }

        internal static void SetWarningSinkForTests(Action<string, Exception> sink)
        {
            logWarning = sink ?? ((message, ex) => { });
        }

        internal static void ResetTestConfiguration()
        {
            installed = false;
            warningLogged = false;
            warningLogCount = 0;
            getStarted = null;
            setStarted = null;
            logWarning = (message, ex) => FGLLog.Warning(message, ex);
            ResetScopeState();
        }
        #endregion
    }
}
