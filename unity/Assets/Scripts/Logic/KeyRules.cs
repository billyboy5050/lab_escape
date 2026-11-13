#nullable enable
namespace LabEscape.Client
{
    /// <summary>
    /// Which key presses the game takes and which it leaves alone, as the web client's `actionKeys.ts` decides them. It has no
    /// Unity code: the director reads the keyboard and asks.
    /// </summary>
    public static class KeyRules
    {
        /// <summary>
        /// Control, Command or Alt held: the key belongs to the system, not the game. Cmd+Q quits, Cmd+M minimises, Cmd+` changes
        /// window, Cmd+1 switches desktop. This holds on every screen, not only in a fight.
        /// </summary>
        public static bool Modified(bool control, bool command, bool alt) => control || command || alt;

        /// <summary>Z undoes a move; Shift+Z is another key's business, and does nothing.</summary>
        public static bool Undoes(bool shift) => !shift;
    }
}
