using System;
using System.Collections.Generic;
using Windows.Graphics;

namespace WinUIDesigner.Shell
{
    /// <summary>The window's restored (not maximized) bounds and whether it was maximized.</summary>
    public sealed class WindowBounds
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool Maximized { get; set; }
    }

    /// <summary>
    /// Decides where a remembered window may go back to. Window-placement spec Decision 2.
    /// </summary>
    internal static class WindowPlacement
    {
        /// <summary>The smallest window restored as is; anything smaller is grown to it.</summary>
        internal const int MinWidth = 640, MinHeight = 400;

        /// <summary>How much of the title bar must be on a display for the bounds to be used.</summary>
        internal const int VisibleTitleWidth = 120, TitleHeight = 32;

        /// <summary>
        /// The rectangle to restore <paramref name="saved"/> to, or null to leave the window
        /// where the system puts it: when nothing was saved, or when not enough of the saved
        /// title bar would be on any of <paramref name="workAreas"/> to drag the window by - a
        /// display that has since been unplugged, or a resolution that shrank.
        /// </summary>
        internal static RectInt32? Fit(WindowBounds saved, IReadOnlyList<RectInt32> workAreas)
        {
            if (saved == null || saved.Width <= 0 || saved.Height <= 0 || workAreas == null)
                return null;

            var rect = new RectInt32(saved.X, saved.Y,
                Math.Max(saved.Width, MinWidth), Math.Max(saved.Height, MinHeight));

            foreach (var area in workAreas)
            {
                int left = Math.Max(rect.X, area.X);
                int right = Math.Min(rect.X + rect.Width, area.X + area.Width);
                int top = Math.Max(rect.Y, area.Y);
                int bottom = Math.Min(rect.Y + TitleHeight, area.Y + area.Height);
                if (right - left >= VisibleTitleWidth && bottom - top >= TitleHeight / 2)
                    return rect;
            }
            return null;
        }
    }
}
