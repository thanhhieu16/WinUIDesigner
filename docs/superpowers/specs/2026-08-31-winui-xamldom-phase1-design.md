# Phase 1: WinUIDesigner.XamlDom — WPF→WinUI XAML DOM port

## Context

Long-term goal: turn SharpDevelop's `WpfDesign` (a runtime WYSIWYG XAML
designer engine, reference source at `F:\ReferencesCode\CSharp\WpfDesigner`)
into a designer for **WinUI 3** XAML instead of WPF XAML. That goal is too
large for one spec — it decomposes into five sequential phases:

1. **XamlDom retarget** (this spec) — parse/serialize WinUI XAML into an
   object model, no UI.
2. Core engine retarget (`DesignItem`/`Metadata`/extension framework) +
   build an overlay layer to replace WPF's `Adorner` (WinUI has no
   `Adorner`/`AdornerLayer`).
3. `DesignPanel`/`DesignSurface` for WinUI — live visual tree hosting,
   hit-testing, a basic selection rectangle.
4. Port the ~56 concrete design-time behaviors (resize thumbs, snaplines,
   per-container placement, drawing tools, context menus).
5. Port the `XamlDesigner` shell app to WinUI (standalone desktop app,
   successor to the current demo app).

This spec covers **Phase 1 only**. Each later phase gets its own spec once
the prior phase is done.

Reference source for porting: `F:\ReferencesCode\CSharp\WpfDesigner\WpfDesign.XamlDom\Project`
(27 files). New code lands in a new repo at
`D:\Project\ST4I\Research\WinUIDesigner` (git-initialized, no history yet).

Microsoft's WPF→WinUI AI-migration reference
(https://learn.microsoft.com/en-us/windows/apps/develop/ai-assisted/migrate/wpf-to-winui)
explicitly calls out that some WPF constructs have no direct WinUI 3
equivalent and should be *flagged, not guessed*. Several such gaps apply to
XamlDom and are called out below rather than papered over.

## Decisions already made

- **Fork, don't abstract.** A new project is copied and adapted from
  `WpfDesign.XamlDom`, rather than extracting a shared parser with
  pluggable WPF/WinUI adapters. The existing WPF designer does not need to
  keep running from the same codebase; a shared-abstraction layer would be
  built for a requirement that doesn't exist. Some duplication between the
  old WPF project and the new WinUI project is accepted.
- **Target framework:** `net10.0-windows10.0.19041.0`, `UseWinUI=true`,
  `Microsoft.WindowsAppSDK` package reference. .NET 10 SDK is already
  installed on this machine (`dotnet --list-sdks` shows `10.0.302`) and
  matches Microsoft's current published WPF→WinUI migration example.
- **Project location:** new solution `WinUIDesigner.sln` at the repo root,
  with library code under `src/`. `F:\ReferencesCode\CSharp\WpfDesigner` is
  read-only reference material, not touched.

## Architecture

```
D:\Project\ST4I\Research\WinUIDesigner\
  WinUIDesigner.sln
  src\
    WinUIDesigner.XamlDom\
      WinUIDesigner.XamlDom.csproj      (net10.0-windows10.0.19041.0, UseWinUI)
      XamlParser.cs
      XamlDocument.cs
      XamlObject.cs
      XamlProperty.cs
      XamlPropertyInfo.cs
      XamlPropertyValue.cs
      XamlTextValue.cs
      XamlStaticTools.cs
      XamlTypeFinder.cs
      XamlTypeResolverProvider.cs
      XamlNamespaceProperties.cs
      XamlConstants.cs
      MarkupExtensionParser.cs
      MarkupExtensionPrinter.cs
      MarkupExtensionWrapper.cs
      MarkupExtensionWrapperAttribute.cs
      CollectionElementsCollection.cs
      CollectionSupport.cs
      TemplateHelper.cs
      NameScopeHelper.cs
      DesignInstanceExtension.cs
      DesignTimeProperties.cs
      IXamlErrorSink.cs
      MarkupCompatibilityProperties.cs
      XamlObjectServiceProvider.cs
      XamlLoadException.cs
      XamlParserSettings.cs
      XamlXmlWriter.cs
      XamlFormatter.cs
      PositionXmlDocument.cs
      XamlElementLineInfo.cs
  tests\
    WinUIDesigner.XamlDom.Tests\
      WinUIDesigner.XamlDom.Tests.csproj
      SimpleLoadTests.cs
      WhitespaceTests.cs
      CollectionTests.cs
      MarkupExtensionTests.cs
      NamescopeTest.cs
      XamlTypeFinderTests.cs
      TestHelper.cs
      (WinUI equivalents of ExampleControl/ExampleClass fixtures)
  docs\superpowers\specs\
    2026-08-31-winui-xamldom-phase1-design.md   (this file)
```

All 27 source files are copied 1:1 in responsibility from the reference
project. The parsing algorithm itself is unchanged: `XamlParser` still
loads into a `System.Xml.XmlDocument` (via `PositionXmlDocument` for
line-tracking) and walks it recursively, creating instances by reflection
and resolving properties primarily through
`System.ComponentModel.TypeDescriptor`, with a `DependencyProperty`-typed
special case layered on top for attached properties. Only the WPF-specific
types referenced throughout get swapped for their WinUI equivalents:

| WPF type | WinUI type |
| --- | --- |
| `System.Windows.DependencyProperty` | `Microsoft.UI.Xaml.DependencyProperty` |
| `System.Windows.DependencyObject` | `Microsoft.UI.Xaml.DependencyObject` |
| `System.Windows.ResourceDictionary` | `Microsoft.UI.Xaml.ResourceDictionary` |
| `System.Windows.FrameworkTemplate` | `Microsoft.UI.Xaml.FrameworkTemplate` |
| `System.Windows.Markup.*` (markup-extension plumbing) | hand-rolled, unchanged shape (XamlDom never depended on `System.Windows.Markup.XamlReader` for parsing) |

## Known gaps — flagged, not guessed

These four items have no direct WinUI equivalent and need explicit
handling rather than a mechanical type swap:

1. **Brush serialization.** `XamlDocument.cs:246-267` (reference source)
   calls `System.Windows.Markup.XamlWriter.Save(brush)` to turn a `Brush`
   object back into a XAML attribute string. WinUI has no equivalent
   runtime XAML writer. Replacement: a hand-written serializer covering
   `SolidColorBrush` (the common case — `#AARRGGBB` or named color). Other
   brush types (gradients, image brushes) are out of scope for Phase 1;
   the writer throws a clearly-labeled `NotSupportedException` for them
   rather than silently producing wrong output.
2. **Named brush lookup.** `XamlDocument.cs:36-48` resolves brush names
   (e.g. `"Red"`) via `System.Windows.Media.Brushes.*`. WinUI has no
   `Brushes` class with pre-built brush instances — only
   `Microsoft.UI.Colors.*` (returns `Windows.UI.Color`). Replacement: a
   small lookup table built from `Microsoft.UI.Colors` properties via
   reflection, each wrapped in a `new SolidColorBrush(color)` on first use.
3. **XAML namespace URIs.** `XamlConstants.cs` hardcodes
   `http://schemas.microsoft.com/winfx/2006/xaml/presentation` and related
   URIs. Before relying on these unchanged, verify them against the root
   element of a real `dotnet new winui3` project's generated XAML — do not
   assume compatibility. If they differ, `XamlConstants.cs` gets the
   WinUI-correct values.
4. **`ISupportInitialize`.** `XamlParser.cs:392` (reference source) checks
   for `System.ComponentModel.ISupportInitialize` and calls
   `BeginInit`/`EndInit` around object construction. Verify whether any
   commonly-used WinUI control types implement this interface; if none do,
   the check becomes a no-op path (kept for forward compatibility, not
   removed, since it is a general ComponentModel interface and costs
   nothing to keep).

## Testing / Definition of Done

New test project `tests/WinUIDesigner.XamlDom.Tests/`, porting the subset
of `WpfDesign.Tests/XamlDom` tests that exercise parsing/serialization
logic (`SimpleLoadTests`, `WhitespaceTests`, `CollectionTests`,
`MarkupExtensionTests`, `NamescopeTest`, `XamlTypeFinderTests`), rewritten
against real WinUI control types instead of the WPF-only
`ExampleControl`/`ExampleClass` fixtures.

Phase 1 is done when:

- The above tests pass.
- A round-trip test parses the `MainWindow.xaml` of a real, freshly
  generated `dotnet new winui3` project into the `XamlDocument` object
  model and re-serializes it, producing XAML that parses back to an
  equivalent object graph (structural equivalence, not necessarily
  byte-identical text).
- Each of the four flagged gaps above has an explicit, tested resolution
  (not a silent fallback).

## Out of scope for Phase 1

No design surface, no `Adorner`/overlay rendering, no live visual-tree
hosting, no property-grid metadata (`Metadata.cs` equivalent), no
extension/behavior framework. These are Phases 2–5.
