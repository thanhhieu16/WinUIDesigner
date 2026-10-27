# Readable prefixes for control namespaces — design

**Date:** 2026-10-27
**Status:** started on "1" (readable namespace prefix), 2026-10-27

## The problem

When a control from a namespace the document does not declare yet is placed, XamlDom invents a
prefix. It has used the reference's `"Controls" + counter`, so a saved file read
`xmlns:Controls0="using:WinUIDesigner.SampleControls"` (toolbox-assemblies plan, Results). That
is valid, but nobody writes it that way, and the number depends on how many prefixes the session
has invented.

## Decisions

**1. A CLR namespace mapping gets its last segment, in lower case, as its prefix.**
- `using:WinUIDesigner.SampleControls` gets `samplecontrols`.
- `using:CommunityToolkit.WinUI.Controls` gets `controls`.
- The same applies to `clr-namespace:Ns;assembly=...`.
- Only ASCII letters and digits are kept.
- A segment that would start with a digit, or with the reserved `xml`, gets no readable prefix.
- A prefix already bound in scope to a different namespace gets 2, 3, ... appended.
- Any other XML namespace, and the cases above that get no readable prefix, keep `"Controls"` +
  counter.
- A prefix the type finder already knows for the namespace, from an earlier parse, is still used
  first.

The change is in `XamlDocument.GetPrefixForNamespace` (ported file; its style is kept).

## Verification

XamlDom suite (`ElementSerializationTests`):
- A fixture control saves as `xmlns:tests="using:WinUIDesigner.XamlDom.Tests"` and `<tests:ExampleControl`.
- With `tests` already bound to another namespace, it saves as `tests2` and reloads to the same
  type.

The walkthrough's saved `walk.xaml` will show `samplecontrols`.

## Results

- CI is green on `windows-latest`, with all four suites run from their `.exe`s, including the two
  new `ElementSerializationTests`.
- The walkthrough's saved `walk.xaml`, on the real app, now reads
  `xmlns:samplecontrols="using:WinUIDesigner.SampleControls"`, with
  `<samplecontrols:SampleBadge .../>` and `<samplecontrols:SampleCard .../>`.
