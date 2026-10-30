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

## Results

- CI green on 7dac9b2.
- Real app (walkthrough, 7dac9b2): the Button placed at client (330,421) was at (350,422) after
  Shift+Right twice and Down once: exactly +20 and +1 (`02a-arrow-moved.png`). Three Ctrl+Z put
  it back at (330,421) (`02b-arrow-undone.png`), so each press was one undo entry. The next
  Ctrl+Z still removed the placement (`03-undone.png`).
- The press that placed the Button gave the surface focus, and the arrow keys reached it from
  `keybd_event` sent as extended keys.
