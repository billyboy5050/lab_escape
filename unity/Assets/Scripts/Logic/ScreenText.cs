#nullable enable
using System.Collections.Generic;
using LabEscape.Core;

namespace LabEscape.Client
{
    /// <summary>The screens a player moves through before and after a fight. Fight is the board with its panels.</summary>
    public enum GameScreen { Title, Loadout, Briefing, Fight }

    /// <summary>The words on the title screen, which the web client writes in `TitleScreen.tsx`, and how fast its terminal prints them.</summary>
    public static class ScreenText
    {
        /// <summary>Characters printed a second, and the wait before the first, as the web client's title terminal.</summary>
        public const float TitleCharsPerSecond = 38f;
        public const float TitleDelay = 0.5f;

        /// <summary>The first three lines are the status; the rest are the alert.</summary>
        public const int TitleStatusLines = 3;

        public static IReadOnlyList<string> TitleLines(Content c) => new[]
        {
            "> VITAL SIGNS ........ STABLE",
            "> MEMORY ............. NOT FOUND",
            $"> LOCATION ........... {c.DefaultRoom.Name.ToUpperInvariant()}",
            "> 1 UNREAD MESSAGE",
            "  \"Get out. Fast.\"",
        };

        public const string TitleBlurb = "You wake in a lab with no memories. Stitch tech and alien powers into combos you didn't plan, and get out.";

        public static string TitleFooter(Content c) => $"Lab Escape · greybox · one room · engine {GameStates.EngineVersion} · content {c.Hash}";
    }
}
