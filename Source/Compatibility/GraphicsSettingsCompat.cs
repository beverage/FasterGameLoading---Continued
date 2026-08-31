using Verse;

namespace FasterGameLoading
{
    /// <summary>
    /// Graphics Settings+ / GraphicsSetter 相容性檢查器。
    /// </summary>
    public static class GraphicsSettingsCompat
    {
        public const string HarmonyId = "com.telefonmast.graphicssettings.rimworld.mod";

        private static bool? isActive;

        static GraphicsSettingsCompat()
        {
            CacheResetter.Register(() => isActive = null);
        }

        public static bool IsActive
        {
            get
            {
                if (isActive is null)
                {
                    isActive = Utils.IsModActive("Telefonmast.GraphicsSettings");
                }
                return isActive.Value;
            }
        }
    }
}
