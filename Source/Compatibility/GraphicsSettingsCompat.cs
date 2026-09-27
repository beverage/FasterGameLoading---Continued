using Verse;

namespace FasterGameLoading
{
    /// <summary>
    /// Graphics Settings+ / GraphicsSetter 相容性檢查器。
    /// </summary>
    public static class GraphicsSettingsCompat
    {
        public const string HarmonyId = "com.telefonmast.graphicssettings.rimworld.mod";

        public static bool IsActive => Utils.IsModActive("Telefonmast.GraphicsSettings");
    }
}
