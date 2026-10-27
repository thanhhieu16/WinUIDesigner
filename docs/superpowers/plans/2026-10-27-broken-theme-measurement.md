# Does a theme XamlReader cannot load take the app down? — measurement plan

**Follows:** `2026-10-27-library-resources.md`, Results, finding 1. A failed `XamlReader.Load`
left a stowed error behind that killed the Shell test host a few tests later. `LibraryResources`
now refuses a theme that is not well-formed XML. A theme that is well-formed but cannot load
still reaches `XamlReader`, and whether the real app survives that was not verified. The real
app differs from the test host: `App` handles `Application.UnhandledException` and sets
`Handled`.

## What is measured

`samples/WinUIDesigner.SampleBrokenTheme` is a second sample library, referenced by nothing. Its
`Themes/Generic.xaml` is well-formed. It merges
`ms-appx:///WinUIDesigner.SampleBrokenTheme/Themes/Colors.xaml`, which a library added at
runtime cannot resolve, and its control's template uses a brush from that file.

The walkthrough adds the library last (steps 29–33):
1. Screenshot the add's report.
2. Search for and place the library's control.
3. Undo the placement.
4. Wait, then take a final screenshot.

If the app dies, the next step fails with its exit code, which `error.txt` records.

## Outcomes

- **The app survives and reports the theme:** the risk is closed as measured. Record it in the
  library-resources plan and CLAUDE.md.
- **The app dies:** root-cause it on the runner before choosing a fix. Candidates are
  `Application.UnhandledException` not seeing the stowed error, and the error surfacing outside
  any handler. Then fix it with a regression step left in place.

## Results

**The real app survives.** Walkthrough steps 29–33, run on `057737e`:
- **Step 30:** the add reports "WinUIDesigner.SampleBrokenTheme's styles could not be loaded, so
  its templated controls will not show: ... Cannot locate resource from
  'ms-appx:///WinUIDesigner.SampleBrokenTheme/Themes/Colors.xaml'. [Line: 7 Position: 29]".
  `XamlReader.Load` threw, and `LibraryResources` caught it.
- **Steps 31–32:** the toolbox lists `SampleBrokenCard`, and it places. It renders nothing, as a
  templated control without its theme does, and appears in the outline.
- **Step 33:** an undo removes it. Five seconds later the app is still running, and the
  walkthrough ends without an `error.txt`.

So the stowed error a failed `XamlReader.Load` leaves behind ends the **test host**, measured in
the library-resources plan, but not this app. The difference is not root-caused. The likely
reason is the one this plan named: `App` handles `Application.UnhandledException`, and the test
host's `TestApplication` installs no handler. No unhandled-exception dialog appeared, so if the
error reached that handler, it was handled without a message.

The well-formed-XML check in `LibraryResources` stays. It is still what keeps the test suite
alive, and it costs nothing.

The first run of this measurement was void: the picker had opened in Downloads, so the `.dll` was
never chosen. The step now picks Documents in the picker's navigation pane first.
