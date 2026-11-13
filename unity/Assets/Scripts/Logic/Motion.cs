#nullable enable
namespace LabEscape.Client
{
    /// <summary>
    /// The reduce-motion setting, which the web client has as the system's `prefers-reduced-motion`. On, the screen stops moving
    /// for its own sake: no camera shake, kick or pull-back, no hit stop or slow motion, no flashing edges or lights, no HP jolt,
    /// nothing popping in, no text printing a character at a time and no idle bob. Units still move, because how they move says
    /// what happened. It is remembered between sessions, and what comes back from the store is untrusted: anything but "1" is off.
    /// </summary>
    public static class Motion
    {
        public const string PrefKey = "lab-escape.reduceMotion";

        /// <summary>The key that turns it on and off, on every screen.</summary>
        public const string Key = "C";

        public static bool Reduced { get; set; }

        public static bool Load(IPrefs prefs) => prefs.Get(PrefKey) == "1";

        public static void Save(IPrefs prefs, bool reduced) => prefs.Set(PrefKey, reduced ? "1" : "0");
    }
}
