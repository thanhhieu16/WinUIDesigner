# Moving the selection with the arrow keys — plan

**Spec:** `docs/superpowers/specs/2026-10-30-keyboard-move-design.md`

1. `src/WinUIDesigner.Designer/Controls/KeyboardMove.cs`: `Nudge`, `Commit` and `Abort`.
2. `DesignPanel`:
   - `KeyDown`/`KeyUp`/`LostFocus` handlers;
   - the test seams `HandleArrowKeyDown(key, shift)` and `HandleArrowKeyUp()`.
3. `PointerTool` focuses the panel on a press.
4. `KeyboardMoveTests` in the Designer suite.
5. Walkthrough:
   - `walkthrough.ps1` gains the arrow keys, sent as extended keys;
   - `steps.txt` moves and undoes the placed Button.
6. Results.
