# Remembering the window's size and position — design

**Date:** 2026-10-27
**Status:** started on "làm mục 1" (window size and position), 2026-10-27
**Closes:** the window half of the recent-files spec's "Persisting the window's size, position or
pane layout" (out of scope there). Pane layout stays out of scope.

## Decisions

**1. The window's restored bounds and maximized state are saved on close.**
- `ShellSettings.Window` is a `WindowBounds`: X, Y, Width and Height in physical pixels, as
  `AppWindow` reports them, plus `Maximized`.
- `MainWindow` tracks the bounds the window has while it is neither maximized nor minimized
  (`AppWindow.Changed`). So a window closed while maximized still restores to its normal size, and
  then maximizes.
- The save happens in `OnAppWindowClosing`, on the branch where the close goes through, while
  `AppWindow` can still be read. A close the user cancels at the save prompt saves nothing.

**2. Saved bounds are restored only if the window can still be grabbed.**
- `WindowPlacement.Fit` returns the rectangle to restore, or null to let the system place the
  window.
- It returns null when nothing was saved, or when less than 120 px of the title bar's width and
  half of its 32 px height would be on any display's work area. That covers a display that has
  been unplugged, and a title bar above the top of the screen.
- A window smaller than 640 × 400 is grown to that.
- Display work areas come from `DisplayArea.FindAll()`. They are read by index, because
  enumerating that projected list has been reported to fail under CsWinRT.

**3. Restoring happens in the constructor, before the window is shown,** through
`AppWindow.MoveAndResize`. If the window was maximized, `OverlappedPresenter.Maximize()` follows.

## Verification

- `WindowPlacementTests`, pure logic plus the settings file:
  - bounds on a display are restored as saved;
  - nothing saved means no placement;
  - bounds on a missing display are rejected;
  - a title bar off the top is rejected, and one half off the side is kept;
  - a tiny window is grown to the minimum;
  - the bounds survive another setting's update.
- Walkthrough, on the real app:
  - A fresh launch is moved to (120, 90, 900 × 640) and closed with `WM_CLOSE`, not killed.
  - It is then launched without the walkthrough's own placement.
  - The window rectangle is recorded before and after; the two should match.

## Out of scope

- Pane widths and other layout.
- Per-display DPI conversion. Bounds are stored and restored in the physical pixels `AppWindow`
  uses, so a window moved to a display with different scaling while closed comes back at the same
  pixel size.
