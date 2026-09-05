# Phase 4f: PropertyGrid Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the design-time property-editing panel: given a selection of `DesignItem`s, list their editable properties grouped into Popular/Other/Attached/Special categories, with 4 value-type editors (Bool/ComboBox/Number/TextBox) and undo-backed edits.

**Architecture:** Two new folders mirroring every prior phase's split — `WinUIDesigner.DesignModel/PropertyGrid/` (`Category`, `PropertyNode`, `EditorManager` — no WinUI-Controls-specific dependency beyond `FrameworkElement` itself) and `WinUIDesigner.Designer/PropertyGrid/` (`PropertyGridControl`, a plain `StackPanel` subclass built via C# object construction — this codebase has zero `.xaml` files anywhere, confirmed — plus `Editors/` with the 4 editor classes). One existing file gets a small addition: `DesignItemProperty.IsAttachedProperty`, needed for category bucketing and attached-property display naming (no `DependencyPropertyDescriptor` equivalent exists in WinUI).

**Tech Stack:** C#, WinUI 3 (`Microsoft.WindowsAppSDK 1.7.250606001`), NUnit/NUnitLite via each test project's built `.exe`.

**Spec:** `docs/superpowers/specs/2026-09-05-winui-designer-phase4f-design.md`

## Global Constraints

- Target framework `net10.0-windows10.0.19041.0`, `UseWinUI=true`, `Microsoft.WindowsAppSDK` pinned to `1.7.250606001` — do not touch.
- `Nullable` is `disable` throughout, matching every prior phase.
- **Tests must be run via each test project's built `.exe`, never `dotnet test`.** Any test touching a live `DependencyProperty`, a live `DesignItem`, a loaded `XamlDesignContext`, or constructing any real `Microsoft.UI.Xaml` object (even a bare `TextBox`/`TextBlock`) must run inside `WinUITestHost.Run(...)`/`WinUITestHost.RunWithWindow(...)`.
- `DesignItemProperty`/`DesignItemPropertyCollection` are **string-keyed only** — attached properties are retrieved via `Properties.GetAttachedProperty(Type ownerType, string name)`, never a combined `"Owner.Name"` string passed to `GetProperty`.
- `DependencyProperty.GetFullName()`/`DesignItemProperty.DependencyFullName` are **confirmed broken** (WinUI's `DependencyProperty` doesn't override `ToString()`) — never call either. Use `DesignItemProperty.FullName` (`DeclaringType.FullName + "." + Name`, already correct).
- Zero `.xaml` files exist anywhere in `src/` — every visual in this codebase (adorners, thumbs, panels) is built via plain C# object construction (e.g. `DesignPanel : Panel`, constructed and populated in its own constructor). `PropertyGridControl` follows the same convention: it derives from `Panel`-family (`StackPanel`), not `Control`+template.
- `WinUIDesigner.DesignModel` must never reference `WinUIDesigner.Designer` (existing `ProjectReference` direction) — `EditorManager` (DesignModel) is decoupled from the concrete editor classes (Designer) via `Func<DesignItemProperty, FrameworkElement>` delegate registration, never a project reference or reflection-based assembly scan.
- No `BasicMetadata`-style real seed data this phase (spec Decision 14, matching Phase 4e Decision 6) — every test seeds `Metadata`/`EditorManager` directly via their own registration calls, with reflection-based `[TearDown]` resets for any process-wide static state a test dirties (matching the established pattern from Phase 4e's `MetadataTests.cs`/`ModelToolsTests.cs`/`CreateComponentToolTests.cs`).
- Some WinUI controls used in the editors (`NumberBox`, `Expander`) are assumed available in this SDK version but **not yet empirically verified in this codebase** — the task first touching each must confirm via a real build, not assume; if unavailable, the step names a fallback.

## Task-ordering note (adjustment from the spec's rough estimate)

The spec's own Testing/DoD section listed the rough order "(2) Category+PropertyNode core; (3) EditorManager+TextBoxEditor" — but `PropertyNode.Load()` calls `EditorManager.CreateEditor(...)` directly, a compile-time dependency. This plan builds `EditorManager` (Task 2) **before** `Category`/`PropertyNode` (Task 3) so Task 3 has something real to call. `TextBoxEditor` (Designer-layer, needs `EditorManager` registered from the Designer side) ships together with `PropertyGridControl` in Task 4, since a `PropertyGridControl` with zero registered editors couldn't usefully be tested end-to-end.

---

### Task 1: `DesignItemProperty.IsAttachedProperty`

**Files:**
- Modify: `src/WinUIDesigner.DesignModel/DesignItemProperty.cs:54` (add one virtual member after `IsAdvanced`)
- Modify: `src/WinUIDesigner.DesignModel/Xaml/XamlModelProperty.cs:100-102` (add override after `DeclaringType`)
- Test: `tests/WinUIDesigner.DesignModel.Tests/DesignItemPropertyTests.cs` (new file)

**Interfaces:**
- Consumes: `WinUIDesigner.XamlDom.XamlProperty.IsAttached` (already exists, `src/WinUIDesigner.XamlDom/XamlProperty.cs:121-122`).
- Produces (used by Tasks 3-4): `DesignItemProperty.IsAttachedProperty` (`bool`, virtual, default `false`) — `true` for an attached property (e.g. `Grid.Row` on a child element), `false` otherwise.

- [ ] **Step 1: Write the failing test file**

Create `tests/WinUIDesigner.DesignModel.Tests/DesignItemPropertyTests.cs`:

```csharp
using System.IO;
using System.Xml;
using NUnit.Framework;
using Microsoft.UI.Xaml.Controls;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.DesignModel.Tests
{
    [TestFixture]
    public class DesignItemPropertyTests
    {
        const string GridXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Grid.RowDefinitions><RowDefinition /><RowDefinition /></Grid.RowDefinitions>" +
            "<Button x:Name=\"Target\" Grid.Row=\"1\" Width=\"40\" Height=\"20\" />" +
            "</Grid>";

        static DesignItem LoadButton()
        {
            using var reader = XmlReader.Create(new StringReader(GridXaml));
            var context = new XamlDesignContext(reader, new XamlLoadSettings());
            var grid = (Grid)context.RootItem.Component;
            var button = (Button)grid.Children[0];
            return context.Services.Component.GetDesignItem(button);
        }

        [Test]
        public void AnAttachedPropertyReportsIsAttachedPropertyTrue()
        {
            WinUITestHost.Run(() => {
                var buttonItem = LoadButton();
                var rowProperty = buttonItem.Properties.GetAttachedProperty(typeof(Grid), "Row");
                Assert.IsTrue(rowProperty.IsAttachedProperty);
            });
        }

        [Test]
        public void AnOrdinaryPropertyReportsIsAttachedPropertyFalse()
        {
            WinUITestHost.Run(() => {
                var buttonItem = LoadButton();
                var widthProperty = buttonItem.Properties.GetProperty("Width");
                Assert.IsFalse(widthProperty.IsAttachedProperty);
            });
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails to compile**

Run: `dotnet build WinUIDesigner.sln`
Expected: build FAILS — `CS1061`, "`DesignItemProperty` does not contain a definition for `IsAttachedProperty`".

- [ ] **Step 3: Add `IsAttachedProperty` to `DesignItemProperty`**

In `src/WinUIDesigner.DesignModel/DesignItemProperty.cs`, change:

```csharp
        public virtual bool IsAdvanced { get { return false; } }

        public string FullName {
```

to:

```csharp
        public virtual bool IsAdvanced { get { return false; } }

        // No WinUI equivalent of System.Windows.DependencyPropertyDescriptor exists (confirmed,
        // src/WinUIDesigner.XamlDom/XamlParser.cs:549-554) - this is how Phase 4f's PropertyGrid
        // detects an attached property (e.g. Grid.Row) without it, for category bucketing and
        // display-name qualification (spec Decisions 1-2).
        public virtual bool IsAttachedProperty { get { return false; } }

        public string FullName {
```

- [ ] **Step 4: Override it on `XamlModelProperty`**

In `src/WinUIDesigner.DesignModel/Xaml/XamlModelProperty.cs`, change:

```csharp
		public override Type DeclaringType {
			get { return _property.PropertyTargetType; }
		}

		public override string Category {
```

to:

```csharp
		public override Type DeclaringType {
			get { return _property.PropertyTargetType; }
		}

		public override bool IsAttachedProperty {
			get { return _property.IsAttached; }
		}

		public override string Category {
```

- [ ] **Step 5: Run the test to verify it passes**

Run:
```
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe --noresult --test=WinUIDesigner.DesignModel.Tests.DesignItemPropertyTests
```
Expected: build succeeds, both tests PASS.

- [ ] **Step 6: Run the full DesignModel.Tests suite to confirm no regression**

Run: `tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe --noresult`
Expected: fully green.

- [ ] **Step 7: Commit**

```bash
git add src/WinUIDesigner.DesignModel/DesignItemProperty.cs src/WinUIDesigner.DesignModel/Xaml/XamlModelProperty.cs tests/WinUIDesigner.DesignModel.Tests/DesignItemPropertyTests.cs
git commit -m "feat: add DesignItemProperty.IsAttachedProperty"
```

---

### Task 2: `EditorManager`

**Files:**
- Create: `src/WinUIDesigner.DesignModel/PropertyGrid/EditorManager.cs`
- Test: `tests/WinUIDesigner.DesignModel.Tests/EditorManagerTests.cs`

**Interfaces:**
- Consumes: `Metadata.GetStandardValues(Type)` / `Metadata.GetStandardValues(DependencyProperty)` / `Metadata.GetNamedStandardValues(Type)` (all exist, Phase 4e).
- Produces (used by Tasks 3-5): `EditorManager.RegisterTypeEditor(Type clrType, Func<DesignItemProperty, FrameworkElement> factory)`; `EditorManager.SetDefaultComboBoxEditorFactory(Func<DesignItemProperty, FrameworkElement> factory)`; `EditorManager.SetDefaultTextBoxEditorFactory(Func<DesignItemProperty, FrameworkElement> factory)`; `EditorManager.CreateEditor(DesignItemProperty property)` returning `FrameworkElement`.

- [ ] **Step 1: Write the failing test file**

Create `tests/WinUIDesigner.DesignModel.Tests/EditorManagerTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NUnit.Framework;
using WinUIDesigner.DesignModel.PropertyGrid;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.DesignModel.Tests
{
    // Minimal DesignItemProperty stub for exercising EditorManager's pure resolution logic,
    // which only reads ReturnType/DependencyProperty - no live XamlDesignContext/WinUI thread
    // needed to construct it (only the factories' own FrameworkElement construction needs
    // WinUITestHost.Run, since that touches real WinRT objects).
    class FakeDesignItemProperty : DesignItemProperty
    {
        public FakeDesignItemProperty(Type returnType) { ReturnType = returnType; }
        public override string Name => "Fake";
        public override Type ReturnType { get; }
        public override Type DeclaringType => typeof(object);
        public override string Category => "Misc";
        public override bool IsCollection => false;
        public override bool IsEvent => false;
        public override WinUIDesigner.DesignModel.Interfaces.IObservableList<DesignItem> CollectionElements => throw new NotImplementedException();
        public override DesignItem Value => null;
        public override string TextValue => null;
        public override event EventHandler ValueChanged { add { } remove { } }
        public override event EventHandler ValueOnInstanceChanged { add { } remove { } }
        public override object ValueOnInstance => null;
        public override object DesignerValue => null;
        public override void SetValue(object value) { }
        public override bool IsSet => false;
        public override event EventHandler IsSetChanged { add { } remove { } }
        public override void Reset() { }
        public override T GetConvertedValueOnInstance<T>() => default;
        public override DesignItem DesignItem => null;
        public override DependencyProperty DependencyProperty => null;
    }

    [TestFixture]
    public class EditorManagerTests
    {
        class RegisteredTypeTarget { }
        class NamedValuesTarget { }
        static class NamedValuesHolder
        {
            public static double Small => 1.0;
            public static double Large => 100.0;
        }
        enum ProbeEnum { First, Second }

        [TearDown]
        public void ResetEditorManagerState()
        {
            typeof(EditorManager).GetField("typeEditors", BindingFlags.NonPublic | BindingFlags.Static)
                .SetValue(null, new Dictionary<Type, Func<DesignItemProperty, FrameworkElement>>());
            typeof(EditorManager).GetField("defaultComboBoxEditor", BindingFlags.NonPublic | BindingFlags.Static)
                .SetValue(null, null);
            typeof(EditorManager).GetField("defaultTextBoxEditor", BindingFlags.NonPublic | BindingFlags.Static)
                .SetValue(null, null);
            // NamedValuesTarget registration below writes into Metadata's own process-wide
            // standardNamedValues dictionary (Phase 4e) - reset it too so this file can't affect
            // MetadataTests.cs, which runs in this same DesignModel.Tests.exe process. The
            // dictionary's value type (Metadata.NamedValue) is a private nested class, so this
            // can't spell out `new Dictionary<Type, List<NamedValue>>()` directly -
            // Activator.CreateInstance(field.FieldType) builds a fresh empty instance of
            // whatever the field's actual declared type is without naming it.
            var namedValuesField = typeof(Metadata).GetField("standardNamedValues", BindingFlags.NonPublic | BindingFlags.Static);
            namedValuesField.SetValue(null, Activator.CreateInstance(namedValuesField.FieldType));
        }

        [Test]
        public void ARegisteredExactTypeEditorIsUsedForAMatchingProperty()
        {
            WinUITestHost.Run(() => {
                var marker = new TextBlock();
                EditorManager.RegisterTypeEditor(typeof(RegisteredTypeTarget), p => marker);
                EditorManager.SetDefaultTextBoxEditorFactory(p => new TextBox());

                var result = EditorManager.CreateEditor(new FakeDesignItemProperty(typeof(RegisteredTypeTarget)));

                Assert.AreSame(marker, result);
            });
        }

        [Test]
        public void AnEnumTypeWithNoRegistrationResolvesToTheDefaultComboBoxFactory()
        {
            WinUITestHost.Run(() => {
                var combo = new ComboBox();
                EditorManager.SetDefaultComboBoxEditorFactory(p => combo);
                EditorManager.SetDefaultTextBoxEditorFactory(p => new TextBox());

                // No Metadata registration needed - Metadata.GetStandardValues(Type) already
                // special-cases every enum type internally (Phase 4e).
                var result = EditorManager.CreateEditor(new FakeDesignItemProperty(typeof(ProbeEnum)));

                Assert.AreSame(combo, result);
            });
        }

        [Test]
        public void ATypeWithOnlyNamedStandardValuesRegisteredResolvesToTheDefaultComboBoxFactory()
        {
            WinUITestHost.Run(() => {
                var combo = new ComboBox();
                Metadata.AddDoubleNamedStandardValues(typeof(NamedValuesTarget), typeof(NamedValuesHolder));
                EditorManager.SetDefaultComboBoxEditorFactory(p => combo);
                EditorManager.SetDefaultTextBoxEditorFactory(p => new TextBox());

                // NamedValuesTarget has no exact type-editor registration and no plain
                // Metadata.GetStandardValues entry (not an enum, nothing called AddStandardValues
                // for it) - only GetNamedStandardValues resolves, exercising the third
                // resolution step distinctly from the second (AnEnumTypeWithNoRegistration...).
                var result = EditorManager.CreateEditor(new FakeDesignItemProperty(typeof(NamedValuesTarget)));

                Assert.AreSame(combo, result);
            });
        }

        [Test]
        public void AnUnregisteredNonEnumTypeResolvesToTheDefaultTextBoxFactory()
        {
            WinUITestHost.Run(() => {
                var textBox = new TextBox();
                EditorManager.SetDefaultComboBoxEditorFactory(p => new ComboBox());
                EditorManager.SetDefaultTextBoxEditorFactory(p => textBox);

                var result = EditorManager.CreateEditor(new FakeDesignItemProperty(typeof(Uri)));

                Assert.AreSame(textBox, result);
            });
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails to compile**

Run: `dotnet build WinUIDesigner.sln`
Expected: build FAILS — `CS0234`, "The type or namespace name 'PropertyGrid' does not exist in the namespace 'WinUIDesigner.DesignModel'".

- [ ] **Step 3: Create `src/WinUIDesigner.DesignModel/PropertyGrid/EditorManager.cs`**

```csharp
// Copyright (c) 2019 AlphaSierraPapa for the SharpDevelop Team
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.UI.Xaml;

namespace WinUIDesigner.DesignModel.PropertyGrid
{
    /// <summary>
    /// Resolves which editor FrameworkElement to build for a given DesignItemProperty. Ported
    /// from the reference's EditorManager.cs (WpfDesign/Project/PropertyGrid/EditorManager.cs),
    /// with two deliberate simplifications (spec Decision 4): registration is delegate-based
    /// instead of Type+Activator.CreateInstance+attribute-scanning (RegisterAssembly/
    /// [TypeEditorAttribute]/[PropertyEditorAttribute] are not ported - nothing in this codebase
    /// needs runtime assembly scanning for a fixed editor set), and the reference's dead
    /// fall-through bug in CreateEditor (a base-type-walk match gets silently discarded by an
    /// unconditional second IsAssignableFrom scan, reference EditorManager.cs:46-101) is not
    /// reproduced - exact-type dictionary lookup is the only lookup step, no base-type walking
    /// (Phase 4f's fixed editor set registers nullable numeric/bool types as their own separate
    /// exact entries, so no derived-type polymorphism ever needs resolving).
    /// </summary>
    public static class EditorManager
    {
        static Dictionary<Type, Func<DesignItemProperty, FrameworkElement>> typeEditors =
            new Dictionary<Type, Func<DesignItemProperty, FrameworkElement>>();

        static Func<DesignItemProperty, FrameworkElement> defaultComboBoxEditor;
        static Func<DesignItemProperty, FrameworkElement> defaultTextBoxEditor;

        public static void RegisterTypeEditor(Type clrType, Func<DesignItemProperty, FrameworkElement> factory)
        {
            lock (typeEditors) typeEditors[clrType] = factory;
        }

        public static void SetDefaultComboBoxEditorFactory(Func<DesignItemProperty, FrameworkElement> factory)
        {
            defaultComboBoxEditor = factory;
        }

        public static void SetDefaultTextBoxEditorFactory(Func<DesignItemProperty, FrameworkElement> factory)
        {
            defaultTextBoxEditor = factory;
        }

        public static FrameworkElement CreateEditor(DesignItemProperty property)
        {
            Func<DesignItemProperty, FrameworkElement> factory;
            lock (typeEditors) {
                if (typeEditors.TryGetValue(property.ReturnType, out factory))
                    return factory(property);
            }

            IEnumerable standardValues = null;
            if (property.DependencyProperty != null) {
                standardValues = Metadata.GetStandardValues(property.DependencyProperty);
            }
            if (standardValues == null) {
                standardValues = Metadata.GetStandardValues(property.ReturnType);
            }
            if (standardValues != null) {
                return defaultComboBoxEditor(property);
            }

            var namedStandardValues = Metadata.GetNamedStandardValues(property.ReturnType);
            if (namedStandardValues != null) {
                return defaultComboBoxEditor(property);
            }

            return defaultTextBoxEditor(property);
        }
    }
}
```

- [ ] **Step 4: Build and run the new tests**

Run:
```
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe --noresult --test=WinUIDesigner.DesignModel.Tests.EditorManagerTests
```
Expected: build succeeds, all 4 `EditorManagerTests` cases PASS.

- [ ] **Step 5: Run the full DesignModel.Tests suite to confirm no regression**

Run: `tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe --noresult`
Expected: fully green.

- [ ] **Step 6: Commit**

```bash
git add src/WinUIDesigner.DesignModel/PropertyGrid/EditorManager.cs tests/WinUIDesigner.DesignModel.Tests/EditorManagerTests.cs
git commit -m "feat: add EditorManager editor-resolution registry"
```

---

### Task 3: `Category` + `PropertyNode`

**Files:**
- Create: `src/WinUIDesigner.DesignModel/PropertyGrid/Category.cs`
- Create: `src/WinUIDesigner.DesignModel/PropertyGrid/PropertyNode.cs`
- Test: `tests/WinUIDesigner.DesignModel.Tests/PropertyNodeTests.cs`

**Interfaces:**
- Consumes: `EditorManager.CreateEditor(DesignItemProperty)` (Task 2); `IComponentPropertyService.GetAvailableProperties(DesignItem)` (`src/WinUIDesigner.DesignModel/Services.cs:45`, exists since Phase 2); `Metadata.IsBrowsable`/`IsPopularProperty` (exist, Phase 4e); `DesignItemProperty.IsAttachedProperty` (Task 1).
- Produces (used by Task 4): `PropertyNode` — constructor `PropertyNode(DesignItemProperty[] properties, PropertyNode parent)`, `.Load(DesignItemProperty[] properties)`, `.Name`, `.FirstProperty`, `.Editor` (`FrameworkElement`), `.Category` (settable `Category`), `.Children`/`.MoreChildren` (`ObservableCollection<PropertyNode>`), `.IsExpanded`, `.HasChildren`, `.DesignerValue`/`.Value`/`.ValueString` (settable), `.IsEnabled`, `.IsSet`, `.IsAmbiguous`, `.IsVisible` (settable), `.CanReset`, `.Reset()`, `.PropertyChanged` event. `Category` — constructor `Category(string name)`, `.Name`, `.Properties` (`List<PropertyNode>`), `.IsExpanded`, `.IsVisible` (settable).

- [ ] **Step 1: Write the failing test file**

Create `tests/WinUIDesigner.DesignModel.Tests/PropertyNodeTests.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NUnit.Framework;
using WinUIDesigner.DesignModel.PropertyGrid;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.DesignModel.Tests
{
    [TestFixture]
    public class PropertyNodeTests
    {
        const string TwoButtonsXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Button x:Name=\"A\" Width=\"40\" Content=\"hi\" />" +
            "<Button x:Name=\"B\" Width=\"40\" />" +
            "</Grid>";

        const string ComplexPropertyXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Button x:Name=\"Target\">" +
            "<Button.RenderTransform><RotateTransform Angle=\"10\" /></Button.RenderTransform>" +
            "</Button>" +
            "</Grid>";

        static (DesignItem a, DesignItem b) LoadTwoButtons()
        {
            using var reader = XmlReader.Create(new StringReader(TwoButtonsXaml));
            var context = new XamlDesignContext(reader, new XamlLoadSettings());
            var grid = (Grid)context.RootItem.Component;
            var a = context.Services.Component.GetDesignItem(grid.Children[0]);
            var b = context.Services.Component.GetDesignItem(grid.Children[1]);
            return (a, b);
        }

        static DesignItem LoadComplexPropertyButton()
        {
            using var reader = XmlReader.Create(new StringReader(ComplexPropertyXaml));
            var context = new XamlDesignContext(reader, new XamlLoadSettings());
            var grid = (Grid)context.RootItem.Component;
            return context.Services.Component.GetDesignItem(grid.Children[0]);
        }

        [SetUp]
        public void RegisterFallbackEditor()
        {
            // PropertyNode.Load() calls EditorManager.CreateEditor - these tests don't exercise
            // editor SELECTION logic (that's EditorManagerTests' job), they just need it to not
            // throw. Re-registering every test is idempotent and avoids depending on NUnit's
            // (unspecified) run order relative to EditorManagerTests' own [TearDown] resets.
            EditorManager.SetDefaultTextBoxEditorFactory(p => new TextBox());
            EditorManager.SetDefaultComboBoxEditorFactory(p => new ComboBox());
        }

        [Test]
        public void LoadResolvesNameFromTheFirstPropertyForAnOrdinaryProperty()
        {
            WinUITestHost.Run(() => {
                var (a, _) = LoadTwoButtons();
                var node = new PropertyNode(new[] { a.Properties.GetProperty("Width") }, null);
                Assert.AreEqual("Width", node.Name);
            });
        }

        [Test]
        public void NameIsQualifiedForAnAttachedProperty()
        {
            WinUITestHost.Run(() => {
                var (a, _) = LoadTwoButtons();
                var rowProperty = a.Properties.GetAttachedProperty(typeof(Grid), "Row");
                var node = new PropertyNode(new[] { rowProperty }, null);
                Assert.AreEqual("Grid.Row", node.Name);
            });
        }

        [Test]
        public void SetValueCoreAppliesToEveryWrappedPropertyAndRoundTripsThroughUndo()
        {
            WinUITestHost.Run(() => {
                var (a, b) = LoadTwoButtons();
                var node = new PropertyNode(new[] { a.Properties.GetProperty("Width"), b.Properties.GetProperty("Width") }, null);

                node.DesignerValue = 99.0;

                Assert.AreEqual(99.0, (double)((Button)a.Component).Width);
                Assert.AreEqual(99.0, (double)((Button)b.Component).Width);
                Assert.IsTrue(a.Services.GetService<UndoService>().CanUndo);
            });
        }

        [Test]
        public void IsAmbiguousIsTrueWhenWrappedPropertiesDifferAndFalseWhenTheyAgree()
        {
            WinUITestHost.Run(() => {
                var (a, b) = LoadTwoButtons();
                var node = new PropertyNode(new[] { a.Properties.GetProperty("Content"), b.Properties.GetProperty("Content") }, null);
                // a.Content == "hi", b.Content == null (unset) - genuinely different.
                Assert.IsTrue(node.IsAmbiguous);

                node.DesignerValue = "same";
                Assert.IsFalse(node.IsAmbiguous);
            });
        }

        [Test]
        public void ResetClearsIsSetForEveryWrappedProperty()
        {
            WinUITestHost.Run(() => {
                var (a, _) = LoadTwoButtons();
                var widthProperty = a.Properties.GetProperty("Width");
                Assert.IsTrue(widthProperty.IsSet, "fixture sets Width=40 explicitly");

                var node = new PropertyNode(new[] { widthProperty }, null);
                node.Reset();

                Assert.IsFalse(widthProperty.IsSet);
            });
        }

        [Test]
        public void ChildrenDrillDownExposesTheComplexSubObjectsOwnBrowsableProperties()
        {
            WinUITestHost.Run(() => {
                var target = LoadComplexPropertyButton();
                var transformProperty = target.Properties.GetProperty("RenderTransform");
                var node = new PropertyNode(new[] { transformProperty }, null);

                Assert.IsFalse(node.HasChildren, "children are lazy - not built until IsExpanded is set");
                node.IsExpanded = true;
                Assert.IsTrue(node.HasChildren);

                var all = node.Children.Concat(node.MoreChildren);
                Assert.IsTrue(all.Any(c => c.Name == "Angle"), "RotateTransform.Angle should surface as a child node");
            });
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails to compile**

Run: `dotnet build WinUIDesigner.sln`
Expected: build FAILS — `CS0234`, "The type or namespace name 'PropertyNode' does not exist" (or similar — `Category`/`PropertyNode` don't exist yet).

- [ ] **Step 3: Create `src/WinUIDesigner.DesignModel/PropertyGrid/Category.cs`**

```csharp
// Copyright (c) 2019 AlphaSierraPapa for the SharpDevelop Team
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System.Collections.Generic;
using System.ComponentModel;

namespace WinUIDesigner.DesignModel.PropertyGrid
{
    /// <summary>
    /// View-model for one property-grid category bucket (Special/Popular/Other/Attached),
    /// ported from the reference's Category.cs (WpfDesign/Project/PropertyGrid/Category.cs). No
    /// SortedObservableCollection/PropertyNodeCollection port (spec Decision 5) - Properties is a
    /// plain list PropertyGridControl inserts into in sorted order.
    /// </summary>
    public class Category : INotifyPropertyChanged
    {
        public Category(string name)
        {
            Name = name;
            Properties = new List<PropertyNode>();
        }

        public string Name { get; private set; }

        public List<PropertyNode> Properties { get; private set; }

        bool isExpanded = true;

        public bool IsExpanded {
            get { return isExpanded; }
            set {
                isExpanded = value;
                RaisePropertyChanged("IsExpanded");
            }
        }

        bool isVisible;

        public bool IsVisible {
            get { return isVisible; }
            set {
                isVisible = value;
                RaisePropertyChanged("IsVisible");
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        void RaisePropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }
}
```

- [ ] **Step 4: Create `src/WinUIDesigner.DesignModel/PropertyGrid/PropertyNode.cs`**

```csharp
// Copyright (c) 2019 AlphaSierraPapa for the SharpDevelop Team
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Microsoft.UI.Xaml;

namespace WinUIDesigner.DesignModel.PropertyGrid
{
    /// <summary>
    /// View-model for one property row, ported from the reference's PropertyNode.cs
    /// (WpfDesign/Project/PropertyGrid/PropertyNode.cs, 456 lines). Trimmed for Phase 4f MVP
    /// scope (spec Decisions 12-13): no Description/IPropertyDescriptionService (no such service
    /// exists in this port), no CreateBinding()/NameForeground binding-value coloring (no
    /// binding/resource-value editing this phase - ValueString's MarkupExtension special case is
    /// simplified to a flat "has a complex sub-object => show its type name" check), no
    /// TextBlock.Text/Inlines special case (Known gap, not resolved this phase). Name uses
    /// DesignItemProperty.IsAttachedProperty (Task 1) instead of the reference's
    /// DependencyPropertyDescriptor (no WinUI equivalent).
    /// </summary>
    public class PropertyNode : INotifyPropertyChanged
    {
        static readonly object Unset = new object();

        public IReadOnlyList<DesignItemProperty> Properties { get; private set; }

        bool raiseEvents = true;
        bool hasStringConverter;

        public string Name {
            get {
                var first = FirstProperty;
                if (first.IsAttachedProperty)
                    return first.DeclaringType.Name + "." + first.Name;
                return first.Name;
            }
        }

        public FrameworkElement Editor { get; private set; }

        public DesignItemProperty FirstProperty { get { return Properties[0]; } }

        public PropertyNode Parent { get; private set; }

        public int Level { get; private set; }

        public Category Category { get; set; }

        public ObservableCollection<PropertyNode> Children { get; private set; }

        public ObservableCollection<PropertyNode> MoreChildren { get; private set; }

        bool isExpanded;

        public bool IsExpanded {
            get { return isExpanded; }
            set {
                isExpanded = value;
                UpdateChildren();
                RaisePropertyChanged("IsExpanded");
            }
        }

        public bool HasChildren {
            get { return Children.Count > 0 || MoreChildren.Count > 0; }
        }

        public object DesignerValue {
            get {
                if (IsAmbiguous) return null;
                return FirstProperty.DesignerValue;
            }
            set { SetValueCore(value); }
        }

        public object Value {
            get {
                if (IsAmbiguous) return null;
                return FirstProperty.ValueOnInstance;
            }
            set { SetValueCore(value); }
        }

        public string ValueString {
            get {
                var item = ValueItem;
                if (item != null) return "(" + item.ComponentType.Name + ")";
                var value = DesignerValue;
                if (value == null) return null;
                if (hasStringConverter)
                    return FirstProperty.TypeConverter.ConvertToInvariantString(value);
                return "(" + value.GetType().Name + ")";
            }
            set {
                DesignerValue = FirstProperty.TypeConverter.ConvertFromInvariantString(value);
            }
        }

        public bool IsEnabled {
            get { return ValueItem == null && hasStringConverter; }
        }

        public bool IsSet {
            get {
                foreach (var p in Properties) {
                    if (p.IsSet) return true;
                }
                return false;
            }
        }

        public DesignItem ValueItem {
            get {
                if (Properties.Count == 1) return FirstProperty.Value;
                return null;
            }
        }

        public bool IsAmbiguous {
            get {
                foreach (var p in Properties) {
                    if (p != FirstProperty && !object.Equals(p.ValueOnInstance, FirstProperty.ValueOnInstance))
                        return true;
                }
                return false;
            }
        }

        bool isVisible;

        public bool IsVisible {
            get { return isVisible; }
            set {
                isVisible = value;
                RaisePropertyChanged("IsVisible");
            }
        }

        public bool CanReset { get { return IsSet; } }

        public void Reset()
        {
            SetValueCore(Unset);
        }

        void SetValueCore(object value)
        {
            raiseEvents = false;
            if (value == Unset) {
                foreach (var p in Properties) p.Reset();
            } else {
                foreach (var p in Properties) p.SetValue(value);
            }
            raiseEvents = true;
            OnValueChanged();
        }

        void OnValueChanged()
        {
            RaisePropertyChanged("IsSet");
            RaisePropertyChanged("Value");
            RaisePropertyChanged("DesignerValue");
            RaisePropertyChanged("ValueString");
            RaisePropertyChanged("IsAmbiguous");
            RaisePropertyChanged("IsEnabled");
            UpdateChildren();
        }

        void OnValueOnInstanceChanged()
        {
            RaisePropertyChanged("Value");
            RaisePropertyChanged("DesignerValue");
            RaisePropertyChanged("ValueString");
        }

        public PropertyNode()
        {
            Children = new ObservableCollection<PropertyNode>();
            MoreChildren = new ObservableCollection<PropertyNode>();
        }

        public PropertyNode(DesignItemProperty[] properties, PropertyNode parent) : this()
        {
            this.Parent = parent;
            this.Level = parent == null ? 0 : parent.Level + 1;
            Load(properties);
        }

        public void Load(DesignItemProperty[] properties)
        {
            if (this.Properties != null) {
                foreach (var property in this.Properties) {
                    property.ValueChanged -= Property_ValueChanged;
                    property.ValueOnInstanceChanged -= Property_ValueOnInstanceChanged;
                }
            }

            this.Properties = new List<DesignItemProperty>(properties);

            if (Editor == null) {
                Editor = EditorManager.CreateEditor(FirstProperty);
                Editor.DataContext = this;
            }

            foreach (var property in properties) {
                property.ValueChanged += Property_ValueChanged;
                property.ValueOnInstanceChanged += Property_ValueOnInstanceChanged;
            }

            hasStringConverter =
                FirstProperty.TypeConverter.CanConvertFrom(typeof(string)) &&
                FirstProperty.TypeConverter.CanConvertTo(typeof(string));

            OnValueChanged();
        }

        void Property_ValueOnInstanceChanged(object sender, EventArgs e)
        {
            if (raiseEvents) OnValueOnInstanceChanged();
        }

        void Property_ValueChanged(object sender, EventArgs e)
        {
            if (raiseEvents) OnValueChanged();
        }

        void UpdateChildren()
        {
            Children.Clear();
            MoreChildren.Clear();

            if (Parent == null || Parent.IsExpanded) {
                var item = ValueItem;
                if (item != null) {
                    var service = item.Services.GetService<IComponentPropertyService>();
                    var list = service.GetAvailableProperties(item)
                        .OrderBy(d => d.Name)
                        .Select(d => new PropertyNode(new[] { item.Properties.GetProperty(d.Name) }, this));

                    foreach (var node in list) {
                        if (Metadata.IsBrowsable(node.FirstProperty)) {
                            node.IsVisible = true;
                            if (Metadata.IsPopularProperty(node.FirstProperty))
                                Children.Add(node);
                            else
                                MoreChildren.Add(node);
                        }
                    }
                }
            }

            RaisePropertyChanged("HasChildren");
        }

        public event PropertyChangedEventHandler PropertyChanged;

        void RaisePropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(name));
        }
    }
}
```

- [ ] **Step 5: Build and run the new tests**

Run:
```
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe --noresult --test=WinUIDesigner.DesignModel.Tests.PropertyNodeTests
```
Expected: build succeeds, all 6 `PropertyNodeTests` cases PASS.

- [ ] **Step 6: Run the full DesignModel.Tests suite to confirm no regression**

Run: `tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe --noresult`
Expected: fully green.

- [ ] **Step 7: Commit**

```bash
git add src/WinUIDesigner.DesignModel/PropertyGrid/Category.cs src/WinUIDesigner.DesignModel/PropertyGrid/PropertyNode.cs tests/WinUIDesigner.DesignModel.Tests/PropertyNodeTests.cs
git commit -m "feat: add Category and PropertyNode PropertyGrid view-models"
```

---

### Task 4: `PropertyGridControl` + `TextBoxEditor`

**Files:**
- Create: `src/WinUIDesigner.Designer/PropertyGrid/PropertyGridControl.cs`
- Create: `src/WinUIDesigner.Designer/PropertyGrid/Editors/TextBoxEditor.cs`
- Test: `tests/WinUIDesigner.Designer.Tests/PropertyGridControlTests.cs`

**Interfaces:**
- Consumes: `Category`/`PropertyNode` (Task 3); `EditorManager.SetDefaultTextBoxEditorFactory`/`RegisterTypeEditor` (Task 2); `IComponentPropertyService.GetAvailableProperties`/`GetCommonAvailableProperties` (exists, Phase 2); `Metadata.IsPopularProperty`/`IsBrowsable` (exists, Phase 4e); `DesignItemProperty.IsAttachedProperty` (Task 1).
- Produces (used by Task 5): `PropertyGridControl` — a `public` `StackPanel` subclass with `SelectedItems` (settable `ICollection<DesignItem>`). `TextBoxEditor.Register()` — static, idempotent, called once from `PropertyGridControl`'s static constructor.

- [ ] **Step 1: Write the failing test file**

Create `tests/WinUIDesigner.Designer.Tests/PropertyGridControlTests.cs`:

```csharp
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NUnit.Framework;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.PropertyGrid;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.Designer.Tests
{
    [TestFixture]
    public class PropertyGridControlTests
    {
        const string GridXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Grid.RowDefinitions><RowDefinition /><RowDefinition /></Grid.RowDefinitions>" +
            "<Button x:Name=\"A\" Grid.Row=\"1\" Width=\"40\" Content=\"a\" />" +
            "<Button x:Name=\"B\" Width=\"55\" Content=\"b\" />" +
            "</Grid>";

        static (DesignItem gridItem, DesignItem a, DesignItem b) Load(Window window)
        {
            var surface = new DesignSurface();
            window.Content = surface;
            using (var reader = XmlReader.Create(new StringReader(GridXaml)))
                surface.LoadDesigner(reader, new XamlLoadSettings());
            var grid = (Grid)surface.DesignContext.RootItem.Component;
            var a = surface.DesignContext.Services.Component.GetDesignItem(grid.Children[0]);
            var b = surface.DesignContext.Services.Component.GetDesignItem(grid.Children[1]);
            return (surface.DesignContext.RootItem, a, b);
        }

        static void ResetMetadataPopularProperties()
        {
            typeof(Metadata).GetField("popularProperties", BindingFlags.NonPublic | BindingFlags.Static)
                .SetValue(null, new System.Collections.Generic.HashSet<string>());
        }

        [Test]
        public void SelectingAnItemBucketsItsPropertiesIntoPopularOtherAndAttachedCategories()
        {
            WinUITestHost.RunWithWindow(window => {
                var (_, a, _) = Load(window);
                Metadata.AddPopularProperty(typeof(Microsoft.UI.Xaml.Controls.Button), "Content");
                try {
                    var grid = new PropertyGridControl();
                    grid.SelectedItems = new[] { a };

                    var popular = grid.Categories.First(c => c.Name == "Popular");
                    var other = grid.Categories.First(c => c.Name == "Other");
                    var attached = grid.Categories.First(c => c.Name == "Attached");

                    Assert.IsTrue(popular.Properties.Any(p => p.Name == "Content"));
                    Assert.IsTrue(other.Properties.Any(p => p.Name == "Width"));
                    Assert.IsTrue(attached.Properties.Any(p => p.Name == "Grid.Row"));
                } finally {
                    ResetMetadataPopularProperties();
                }
            });
        }

        [Test]
        public void EditingThroughTheTextBoxEditorRoundTripsThroughSetValueAndUndo()
        {
            WinUITestHost.RunWithWindow(window => {
                var (_, a, _) = Load(window);
                var grid = new PropertyGridControl();
                grid.SelectedItems = new[] { a };

                var node = grid.Categories.SelectMany(c => c.Properties).First(p => p.Name == "Width");
                var textBox = (TextBox)node.Editor;
                textBox.Text = "123";
                RaiseLostFocus(textBox);

                Assert.AreEqual(123.0, (double)((Button)a.Component).Width);
                Assert.IsTrue(a.Services.GetService<UndoService>().CanUndo);
            });
        }

        [Test]
        public void MultiSelectShowsAmbiguousForACommonPropertyWithDifferingValues()
        {
            WinUITestHost.RunWithWindow(window => {
                var (_, a, b) = Load(window);
                var grid = new PropertyGridControl();
                grid.SelectedItems = new[] { a, b };

                var node = grid.Categories.SelectMany(c => c.Properties).First(p => p.Name == "Width");
                Assert.IsTrue(node.IsAmbiguous, "A.Width=40, B.Width=55 - genuinely different");
            });
        }

        [Test]
        public void ReselectingTheSameItemReusesTheCachedPropertyNodeAndEditor()
        {
            WinUITestHost.RunWithWindow(window => {
                var (_, a, b) = Load(window);
                var grid = new PropertyGridControl();

                grid.SelectedItems = new[] { a };
                var firstNode = grid.Categories.SelectMany(c => c.Properties).First(p => p.Name == "Width");
                var firstEditor = firstNode.Editor;

                grid.SelectedItems = new[] { b };
                grid.SelectedItems = new[] { a };
                var secondNode = grid.Categories.SelectMany(c => c.Properties).First(p => p.Name == "Width");

                Assert.AreSame(firstNode, secondNode);
                Assert.AreSame(firstEditor, secondNode.Editor);
            });
        }

        static void RaiseLostFocus(TextBox textBox)
        {
            // A real LostFocus RoutedEventArgs can't be fabricated (no public constructor -
            // Global Constraints' established pattern for routed events in this codebase) -
            // TextBoxEditor exposes CommitForTest as the same "Handle*-extracted, internal test
            // seam" pattern used throughout this codebase (e.g. DesignPanel.HandlePointerPressed).
            WinUIDesigner.Designer.PropertyGrid.Editors.TextBoxEditor.CommitForTest(textBox);
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails to compile**

Run: `dotnet build WinUIDesigner.sln`
Expected: build FAILS — `CS0234`, "The type or namespace name 'PropertyGrid' does not exist in the namespace 'WinUIDesigner.Designer'".

- [ ] **Step 3: Create `src/WinUIDesigner.Designer/PropertyGrid/Editors/TextBoxEditor.cs`**

```csharp
// Copyright (c) 2019 AlphaSierraPapa for the SharpDevelop Team
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.PropertyGrid;

namespace WinUIDesigner.Designer.PropertyGrid.Editors
{
    /// <summary>
    /// Default fallback editor (spec Decision 9): a plain TextBox round-tripping
    /// PropertyNode.ValueString on LostFocus. No XAML/DataTemplate binding exists in this
    /// codebase (Global Constraints) - PropertyNode.Load() sets Editor.DataContext to itself once,
    /// and this editor reads it back at commit time rather than closing over the property at
    /// construction time, since the same Editor instance is reused across Reload()s of the same
    /// cached PropertyNode (Task 3's Load() only creates Editor once).
    /// </summary>
    public static class TextBoxEditor
    {
        public static void Register()
        {
            EditorManager.SetDefaultTextBoxEditorFactory(Create);
        }

        public static FrameworkElement Create(DesignItemProperty property)
        {
            var textBox = new TextBox();
            textBox.DataContextChanged += (s, e) => {
                var node = textBox.DataContext as PropertyNode;
                if (node != null) {
                    textBox.Text = node.ValueString;
                    node.PropertyChanged += (ps, pe) => {
                        if (pe.PropertyName == "ValueString") textBox.Text = node.ValueString;
                    };
                }
            };
            textBox.LostFocus += (s, e) => Commit(textBox);
            return textBox;
        }

        static void Commit(TextBox textBox)
        {
            var node = textBox.DataContext as PropertyNode;
            if (node != null) node.ValueString = textBox.Text;
        }

        // Test seam - a real LostFocus RoutedEventArgs can't be fabricated (no public
        // constructor). Mirrors this codebase's established "Handle*-extracted" test-seam
        // pattern (e.g. DesignPanel.HandlePointerPressed, CreateComponentTool.Gesture).
        internal static void CommitForTest(TextBox textBox)
        {
            Commit(textBox);
        }
    }
}
```

- [ ] **Step 4: Create `src/WinUIDesigner.Designer/PropertyGrid/PropertyGridControl.cs`**

```csharp
// Copyright (c) 2019 AlphaSierraPapa for the SharpDevelop Team
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.PropertyGrid;
using WinUIDesigner.Designer.PropertyGrid.Editors;

namespace WinUIDesigner.Designer.PropertyGrid
{
    /// <summary>
    /// Design-time property-editing panel (spec Decision 6 folds the reference's separate
    /// PropertyGrid.cs controller directly into this control - no XAML/DataTemplate view layer
    /// exists in this codebase to justify the split). No shell app exists yet (Phase 5) - a host
    /// sets SelectedItems directly, the same host-less-control-testing pattern DesignSurfaceTests
    /// and other Designer.Tests already use.
    /// </summary>
    public class PropertyGridControl : StackPanel
    {
        static PropertyGridControl()
        {
            TextBoxEditor.Register();
        }

        readonly Category specialCategory = new Category("Special");
        readonly Category popularCategory = new Category("Popular");
        readonly Category otherCategory = new Category("Other");
        readonly Category attachedCategory = new Category("Attached");

        readonly Dictionary<string, PropertyNode> nodeCache = new Dictionary<string, PropertyNode>();

        public IReadOnlyList<Category> Categories { get; }

        IList<DesignItem> selectedItems;

        public ICollection<DesignItem> SelectedItems {
            get { return (ICollection<DesignItem>)selectedItems; }
            set {
                selectedItems = value == null ? null : value.ToList();
                Reload();
            }
        }

        public PropertyGridControl()
        {
            Categories = new[] { popularCategory, otherCategory, attachedCategory, specialCategory };
        }

        void Reload()
        {
            foreach (var category in Categories) {
                category.IsVisible = false;
                foreach (var node in category.Properties) node.IsVisible = false;
            }

            Children.Clear();

            if (selectedItems == null || selectedItems.Count == 0) return;

            IEnumerable<System.ComponentModel.MemberDescriptor> descriptors;
            if (selectedItems.Count == 1) {
                var service = selectedItems[0].Services.GetService<IComponentPropertyService>();
                descriptors = service.GetAvailableProperties(selectedItems[0]);
            } else {
                var service = selectedItems[0].Services.GetService<IComponentPropertyService>();
                descriptors = service.GetCommonAvailableProperties(selectedItems);
            }

            foreach (var descriptor in descriptors) {
                AddNode(descriptor.Name);
            }

            foreach (var category in Categories) {
                if (category.IsVisible) Children.Add(BuildCategoryRow(category));
            }
        }

        void AddNode(string propertyName)
        {
            var designProperties = selectedItems.Select(item => item.Properties.GetProperty(propertyName)).ToArray();
            if (!Metadata.IsBrowsable(designProperties[0])) return;

            PropertyNode node;
            if (nodeCache.TryGetValue(designProperties[0].FullName, out node)) {
                node.Load(designProperties);
            } else {
                node = new PropertyNode(designProperties, null);
                var category = PickCategory(node);
                InsertSorted(category.Properties, node);
                node.Category = category;
                nodeCache[designProperties[0].FullName] = node;
            }
            node.IsVisible = true;
            node.Category.IsVisible = true;
        }

        Category PickCategory(PropertyNode node)
        {
            if (Metadata.IsPopularProperty(node.FirstProperty)) return popularCategory;
            if (node.FirstProperty.IsAttachedProperty) return attachedCategory;
            var typeName = node.FirstProperty.DeclaringType.FullName;
            if (typeName != null && typeName.StartsWith("Microsoft.UI.Xaml.", StringComparison.Ordinal)) return otherCategory;
            return specialCategory;
        }

        static void InsertSorted(List<PropertyNode> list, PropertyNode node)
        {
            int index = 0;
            while (index < list.Count && string.Compare(list[index].Name, node.Name, StringComparison.OrdinalIgnoreCase) < 0) {
                index++;
            }
            list.Insert(index, node);
        }

        FrameworkElement BuildCategoryRow(Category category)
        {
            var header = new TextBlock { Text = category.Name, FontWeight = Microsoft.UI.Text.FontWeights.Bold };
            var toggle = new ToggleButton { Content = header, IsChecked = category.IsExpanded };
            var body = new StackPanel { Visibility = category.IsExpanded ? Visibility.Visible : Visibility.Collapsed };
            foreach (var node in category.Properties) {
                if (node.IsVisible) body.Children.Add(BuildPropertyRow(node));
            }
            toggle.Checked += (s, e) => { category.IsExpanded = true; body.Visibility = Visibility.Visible; };
            toggle.Unchecked += (s, e) => { category.IsExpanded = false; body.Visibility = Visibility.Collapsed; };

            var panel = new StackPanel();
            panel.Children.Add(toggle);
            panel.Children.Add(body);
            return panel;
        }

        FrameworkElement BuildPropertyRow(PropertyNode node)
        {
            var row = new Grid { Margin = new Thickness(node.Level * 12, 0, 0, 0) };
            // 3 columns always, whether or not this node has children - a toggle-or-empty
            // spacer (0), the name (1), the editor (2). Sharing one column between the toggle
            // and the name (as an earlier draft of this method did) makes them overlap.
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16, GridUnitType.Pixel) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            ToggleButton toggle = null;
            if (node.HasChildren) {
                toggle = new ToggleButton { Content = "▶", IsChecked = node.IsExpanded };
                Grid.SetColumn(toggle, 0);
                row.Children.Add(toggle);
            }

            var nameText = new TextBlock { Text = node.Name, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(nameText, 1);
            row.Children.Add(nameText);

            // node.Editor is cached on the PropertyNode (Task 3's Load() only creates it once) -
            // on a second Reload() of an already-seen node it is still parented to the PREVIOUS
            // call's now-orphaned row Grid. WinUI (like WPF/UWP) throws if a FrameworkElement is
            // added as a child while still parented elsewhere, so it must be detached first.
            if (node.Editor.Parent is Panel oldEditorParent) {
                oldEditorParent.Children.Remove(node.Editor);
            }
            Grid.SetColumn(node.Editor, 2);
            row.Children.Add(node.Editor);

            var stack = new StackPanel();
            stack.Children.Add(row);

            if (node.HasChildren) {
                var childrenStack = new StackPanel { Visibility = node.IsExpanded ? Visibility.Visible : Visibility.Collapsed };
                RebuildChildRows(node, childrenStack);
                toggle.Checked += (s, e) => {
                    node.IsExpanded = true;
                    childrenStack.Visibility = Visibility.Visible;
                    RebuildChildRows(node, childrenStack);
                };
                toggle.Unchecked += (s, e) => {
                    node.IsExpanded = false;
                    childrenStack.Visibility = Visibility.Collapsed;
                };
                stack.Children.Add(childrenStack);
            }

            return stack;
        }

        void RebuildChildRows(PropertyNode node, StackPanel childrenStack)
        {
            childrenStack.Children.Clear();
            foreach (var child in node.Children) {
                childrenStack.Children.Add(BuildPropertyRow(child));
            }
            if (node.MoreChildren.Count > 0) {
                foreach (var child in node.MoreChildren) {
                    childrenStack.Children.Add(BuildPropertyRow(child));
                }
            }
        }
    }
}
```

- [ ] **Step 5: Build and run the new tests**

Run:
```
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult --test=WinUIDesigner.Designer.Tests.PropertyGridControlTests
```
Expected: build succeeds, all 4 `PropertyGridControlTests` cases PASS. If `dotnet build` fails on `ToggleButton`/`Microsoft.UI.Text.FontWeights` with a missing-type error, this is the plan's flagged-but-unverified WinUI surface (Global Constraints) - substitute `Microsoft.UI.Xaml.Controls.Primitives.ToggleButton` (check the exact namespace the compiler error names) and `Windows.UI.Text.FontWeights` respectively, both are real WinUI/UWP types just possibly namespaced differently than written here; re-run the build after the substitution.

- [ ] **Step 6: Run the full Designer.Tests suite to confirm no regression**

Run: `tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult`
Expected: fully green.

- [ ] **Step 7: Commit**

```bash
git add src/WinUIDesigner.Designer/PropertyGrid/PropertyGridControl.cs src/WinUIDesigner.Designer/PropertyGrid/Editors/TextBoxEditor.cs tests/WinUIDesigner.Designer.Tests/PropertyGridControlTests.cs
git commit -m "feat: add PropertyGridControl with TextBoxEditor fallback"
```

---

### Task 5: `BoolEditor` + `ComboBoxEditor` + `NumberEditor`

**Files:**
- Create: `src/WinUIDesigner.Designer/PropertyGrid/Editors/BoolEditor.cs`
- Create: `src/WinUIDesigner.Designer/PropertyGrid/Editors/ComboBoxEditor.cs`
- Create: `src/WinUIDesigner.Designer/PropertyGrid/Editors/NumberEditor.cs`
- Modify: `src/WinUIDesigner.Designer/PropertyGrid/PropertyGridControl.cs` (static constructor)
- Test: `tests/WinUIDesigner.Designer.Tests/PropertyGridControlTests.cs` (add cases)

**Interfaces:**
- Consumes: `EditorManager.RegisterTypeEditor`/`SetDefaultComboBoxEditorFactory` (Task 2); `Metadata.GetStandardValues`/`GetNamedStandardValues`/`GetValueRange` (exist, Phase 4e); `PropertyNode.DesignerValue`/`.ValueString`/`.PropertyChanged` (Task 3).
- Produces: nothing further - this is the last task.

- [ ] **Step 1: Write the failing tests**

Add to `tests/WinUIDesigner.Designer.Tests/PropertyGridControlTests.cs`, inside the `PropertyGridControlTests` class:

```csharp
        [Test]
        public void ABoolPropertyGetsACheckBoxEditorThatRoundTripsThroughSetValue()
        {
            WinUITestHost.RunWithWindow(window => {
                var (_, a, _) = Load(window);
                var grid = new PropertyGridControl();
                grid.SelectedItems = new[] { a };

                var node = grid.Categories.SelectMany(c => c.Properties).First(p => p.Name == "IsEnabled");
                var checkBox = (CheckBox)node.Editor;
                Assert.IsTrue(checkBox.IsChecked == true, "IsEnabled defaults to true");

                checkBox.IsChecked = false;
                RaiseToggle(checkBox);

                Assert.IsFalse(((Button)a.Component).IsEnabled);
            });
        }

        [Test]
        public void AnEnumPropertyGetsAComboBoxEditorPopulatedFromMetadataStandardValues()
        {
            WinUITestHost.RunWithWindow(window => {
                var (_, a, _) = Load(window);
                var grid = new PropertyGridControl();
                grid.SelectedItems = new[] { a };

                var node = grid.Categories.SelectMany(c => c.Properties).First(p => p.Name == "Visibility");
                var comboBox = (ComboBox)node.Editor;

                CollectionAssert.Contains(comboBox.ItemsSource.Cast<object>().ToList(), Microsoft.UI.Xaml.Visibility.Collapsed);

                comboBox.SelectedItem = Microsoft.UI.Xaml.Visibility.Collapsed;
                RaiseSelectionChanged(comboBox);

                Assert.AreEqual(Microsoft.UI.Xaml.Visibility.Collapsed, ((Button)a.Component).Visibility);
            });
        }

        [Test]
        public void ANumericPropertyWithARegisteredRangeGetsANumberBoxEditorWithThatRange()
        {
            WinUITestHost.RunWithWindow(window => {
                var (_, a, _) = Load(window);
                Metadata.AddValueRange(Microsoft.UI.Xaml.FrameworkElement.WidthProperty, 0, 500);
                try {
                    var grid = new PropertyGridControl();
                    grid.SelectedItems = new[] { a };

                    var node = grid.Categories.SelectMany(c => c.Properties).First(p => p.Name == "Width");
                    var numberBox = (NumberBox)node.Editor;

                    Assert.AreEqual(0, numberBox.Minimum);
                    Assert.AreEqual(500, numberBox.Maximum);

                    numberBox.Value = 200;
                    RaiseNumberBoxValueChanged(numberBox);

                    Assert.AreEqual(200.0, (double)((Button)a.Component).Width);
                } finally {
                    ResetMetadataValueRanges();
                }
            });
        }

        static void ResetMetadataValueRanges()
        {
            typeof(Metadata).GetField("ranges", BindingFlags.NonPublic | BindingFlags.Static)
                .SetValue(null, new System.Collections.Generic.Dictionary<Microsoft.UI.Xaml.DependencyProperty, NumberRange>());
        }

        static void RaiseToggle(CheckBox checkBox)
        {
            WinUIDesigner.Designer.PropertyGrid.Editors.BoolEditor.CommitForTest(checkBox);
        }

        static void RaiseSelectionChanged(ComboBox comboBox)
        {
            WinUIDesigner.Designer.PropertyGrid.Editors.ComboBoxEditor.CommitForTest(comboBox);
        }

        static void RaiseNumberBoxValueChanged(NumberBox numberBox)
        {
            WinUIDesigner.Designer.PropertyGrid.Editors.NumberEditor.CommitForTest(numberBox);
        }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build WinUIDesigner.sln && tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult --test=WinUIDesigner.Designer.Tests.PropertyGridControlTests`
Expected: build FAILS - `BoolEditor`/`ComboBoxEditor`/`NumberEditor` don't exist yet.

- [ ] **Step 3: Create `src/WinUIDesigner.Designer/PropertyGrid/Editors/BoolEditor.cs`**

```csharp
// Copyright (c) 2019 AlphaSierraPapa for the SharpDevelop Team
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.PropertyGrid;

namespace WinUIDesigner.Designer.PropertyGrid.Editors
{
    /// <summary>bool/bool? editor (spec Decision 9): a CheckBox round-tripping PropertyNode.DesignerValue.</summary>
    public static class BoolEditor
    {
        public static void Register()
        {
            EditorManager.RegisterTypeEditor(typeof(bool), Create);
            EditorManager.RegisterTypeEditor(typeof(bool?), Create);
        }

        public static FrameworkElement Create(DesignItemProperty property)
        {
            var checkBox = new CheckBox();
            checkBox.DataContextChanged += (s, e) => {
                var node = checkBox.DataContext as PropertyNode;
                if (node != null) {
                    checkBox.IsChecked = node.DesignerValue as bool?;
                    node.PropertyChanged += (ps, pe) => {
                        if (pe.PropertyName == "DesignerValue") checkBox.IsChecked = node.DesignerValue as bool?;
                    };
                }
            };
            checkBox.Checked += (s, e) => Commit(checkBox);
            checkBox.Unchecked += (s, e) => Commit(checkBox);
            return checkBox;
        }

        static void Commit(CheckBox checkBox)
        {
            var node = checkBox.DataContext as PropertyNode;
            if (node != null) node.DesignerValue = checkBox.IsChecked ?? false;
        }

        internal static void CommitForTest(CheckBox checkBox)
        {
            Commit(checkBox);
        }
    }
}
```

- [ ] **Step 4: Create `src/WinUIDesigner.Designer/PropertyGrid/Editors/ComboBoxEditor.cs`**

```csharp
// Copyright (c) 2019 AlphaSierraPapa for the SharpDevelop Team
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.PropertyGrid;

namespace WinUIDesigner.Designer.PropertyGrid.Editors
{
    /// <summary>
    /// Default combo-box editor (spec Decision 9): used both for Metadata.GetStandardValues
    /// (enum types resolve here automatically - Metadata's own internal enum handling, Phase 4e -
    /// and any explicitly-registered standard-values list) and Metadata.GetNamedStandardValues
    /// (DisplayMemberPath/SelectedValuePath variant - reflection-based since Metadata's
    /// NamedValue type is private, matching how the reference's own WPF bindings resolve
    /// Name/Value purely by string path at runtime with no compile-time type needed).
    /// </summary>
    public static class ComboBoxEditor
    {
        public static void Register()
        {
            EditorManager.SetDefaultComboBoxEditorFactory(Create);
        }

        public static FrameworkElement Create(DesignItemProperty property)
        {
            var comboBox = new ComboBox();

            System.Collections.IEnumerable standardValues = null;
            if (property.DependencyProperty != null) {
                standardValues = Metadata.GetStandardValues(property.DependencyProperty);
            }
            if (standardValues == null) {
                standardValues = Metadata.GetStandardValues(property.ReturnType);
            }
            if (standardValues != null) {
                comboBox.ItemsSource = standardValues;
            } else {
                var named = Metadata.GetNamedStandardValues(property.ReturnType);
                comboBox.ItemsSource = named;
                comboBox.DisplayMemberPath = "Name";
                comboBox.SelectedValuePath = "Value";
            }

            comboBox.DataContextChanged += (s, e) => {
                var node = comboBox.DataContext as PropertyNode;
                if (node != null) {
                    comboBox.SelectedItem = node.DesignerValue;
                    node.PropertyChanged += (ps, pe) => {
                        if (pe.PropertyName == "DesignerValue") comboBox.SelectedItem = node.DesignerValue;
                    };
                }
            };
            comboBox.SelectionChanged += (s, e) => Commit(comboBox);
            return comboBox;
        }

        static void Commit(ComboBox comboBox)
        {
            var node = comboBox.DataContext as PropertyNode;
            if (node != null) node.DesignerValue = comboBox.SelectedItem;
        }

        internal static void CommitForTest(ComboBox comboBox)
        {
            Commit(comboBox);
        }
    }
}
```

- [ ] **Step 5: Create `src/WinUIDesigner.Designer/PropertyGrid/Editors/NumberEditor.cs`**

```csharp
// Copyright (c) 2019 AlphaSierraPapa for the SharpDevelop Team
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.PropertyGrid;

namespace WinUIDesigner.Designer.PropertyGrid.Editors
{
    /// <summary>
    /// Numeric editor (spec Decision 9): a NumberBox consuming Metadata.GetValueRange for
    /// Minimum/Maximum when registered (else NumberBox's own double.MinValue/MaxValue defaults).
    /// Registered against every numeric CLR type and its nullable variant as separate exact
    /// entries (no base-type walking - spec Decision 4) since NumberBox.Value is always double.
    /// </summary>
    public static class NumberEditor
    {
        static readonly Type[] NumericTypes = {
            typeof(byte), typeof(byte?), typeof(sbyte), typeof(sbyte?),
            typeof(short), typeof(short?), typeof(ushort), typeof(ushort?),
            typeof(int), typeof(int?), typeof(uint), typeof(uint?),
            typeof(long), typeof(long?), typeof(ulong), typeof(ulong?),
            typeof(float), typeof(float?), typeof(double), typeof(double?),
            typeof(decimal), typeof(decimal?),
        };

        public static void Register()
        {
            foreach (var type in NumericTypes) {
                EditorManager.RegisterTypeEditor(type, Create);
            }
        }

        public static FrameworkElement Create(DesignItemProperty property)
        {
            var numberBox = new NumberBox();

            numberBox.DataContextChanged += (s, e) => {
                var node = numberBox.DataContext as PropertyNode;
                if (node != null) {
                    var nodeRange = Metadata.GetValueRange(node.FirstProperty);
                    if (nodeRange != null) {
                        numberBox.Minimum = nodeRange.Min;
                        numberBox.Maximum = nodeRange.Max;
                    }
                    numberBox.Value = ToDouble(node.DesignerValue);
                    node.PropertyChanged += (ps, pe) => {
                        if (pe.PropertyName == "DesignerValue") numberBox.Value = ToDouble(node.DesignerValue);
                    };
                }
            };
            numberBox.ValueChanged += (s, e) => Commit(numberBox);
            return numberBox;
        }

        static double ToDouble(object value)
        {
            if (value == null) return double.NaN;
            return Convert.ToDouble(value);
        }

        static void Commit(NumberBox numberBox)
        {
            var node = numberBox.DataContext as PropertyNode;
            if (node == null) return;
            if (double.IsNaN(numberBox.Value)) {
                node.Reset();
                return;
            }
            var targetType = node.FirstProperty.ReturnType;
            var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
            node.DesignerValue = Convert.ChangeType(numberBox.Value, underlying);
        }

        internal static void CommitForTest(NumberBox numberBox)
        {
            Commit(numberBox);
        }
    }
}
```

- [ ] **Step 6: Wire all three into `PropertyGridControl`'s static constructor**

In `src/WinUIDesigner.Designer/PropertyGrid/PropertyGridControl.cs`, change:

```csharp
        static PropertyGridControl()
        {
            TextBoxEditor.Register();
        }
```

to:

```csharp
        static PropertyGridControl()
        {
            TextBoxEditor.Register();
            BoolEditor.Register();
            ComboBoxEditor.Register();
            NumberEditor.Register();
        }
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet build WinUIDesigner.sln && tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult --test=WinUIDesigner.Designer.Tests.PropertyGridControlTests`
Expected: all 7 `PropertyGridControlTests` cases PASS.

- [ ] **Step 8: Run the full three-project suite to confirm no regression anywhere**

Run:
```
tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult
tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe --noresult
tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult
```
Expected: all three fully green.

- [ ] **Step 9: Commit**

```bash
git add src/WinUIDesigner.Designer/PropertyGrid/Editors/BoolEditor.cs src/WinUIDesigner.Designer/PropertyGrid/Editors/ComboBoxEditor.cs src/WinUIDesigner.Designer/PropertyGrid/Editors/NumberEditor.cs src/WinUIDesigner.Designer/PropertyGrid/PropertyGridControl.cs tests/WinUIDesigner.Designer.Tests/PropertyGridControlTests.cs
git commit -m "feat: add Bool/ComboBox/Number editors to PropertyGrid"
```
