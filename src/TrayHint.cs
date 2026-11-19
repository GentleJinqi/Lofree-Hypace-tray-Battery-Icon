using System;

namespace HypaceTray
{
    // The first-run reminder. Windows puts the tray icon of a program in a new location under ^ (it remembers
    // icons by the exe's path), so the block shows left of ^ until the user drags the icon onto the taskbar.
    // Tick once a second: true once the icon has been under ^ for WaitTicks seconds in a row (not while Windows
    // is still placing it at start-up), and only once per exe location (shownFor: where it was last shown).
    sealed class TrayHint
    {
        public const int WaitTicks = 10;
        int ticksUnder;
        bool done;

        public TrayHint(string shownFor, string exePath)
        {
            done = string.Equals(shownFor, exePath, StringComparison.OrdinalIgnoreCase);
        }

        public bool Tick(bool underChevron)
        {
            if (done) return false;
            ticksUnder = underChevron ? ticksUnder + 1 : 0;
            if (ticksUnder < WaitTicks) return false;
            done = true;
            return true;
        }
    }
}
