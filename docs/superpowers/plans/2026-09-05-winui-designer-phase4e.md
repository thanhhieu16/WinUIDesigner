# Phase 4e: Metadata Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Port the reference's `Metadata` static-registry class into `WinUIDesigner.DesignModel`, and wire it into the three already-flagged, already-commented gaps waiting for it: `DefaultPlacementBehavior`'s placement-disabled check, `ModelTools.GetDefaultSize`'s per-type size registry, and a new `IComponentService.SetDefaultPropertyValues` consulted by `CreateComponentTool` when creating a new component.

**Architecture:** One new file (`Metadata.cs`, `WinUIDesigner.DesignModel`) replaces the existing partial stand-in `PlacementMetadata.cs`, and gets consulted from two existing `WinUIDesigner.Designer` call sites (`ModelTools.GetDefaultSize`, `CreateComponentTool.CreateItem`) plus one new interface method (`IComponentService.SetDefaultPropertyValues`). No new WinUI API surface — `Type`, `DependencyProperty`, `Windows.Foundation.Size`, `Dictionary`/`HashSet` are all already used this way elsewhere in the codebase.

**Tech Stack:** C#, WinUI 3 (`Microsoft.WindowsAppSDK 1.7.250606001`), NUnit/NUnitLite via each test project's built `.exe`.

**Spec:** `docs/superpowers/specs/2026-09-05-winui-designer-phase4e-design.md`

## Global Constraints

- Target framework `net10.0-windows10.0.19041.0`, `UseWinUI=true`, `Microsoft.WindowsAppSDK` pinned to `1.7.250606001` — do not touch.
- `Nullable` is `disable` throughout, matching every prior phase.
- **Tests must be run via each test project's built `.exe`, never `dotnet test`** — `Microsoft.UI.Xaml` objects need to activate on a thread inside `Microsoft.UI.Xaml.Application.Start`, which only the `.exe`'s embedded manifest provides. Any test touching a live `DependencyProperty` field, a live `DesignItem`, or a loaded `XamlDesignContext` must run inside `WinUITestHost.Run(...)`/`WinUITestHost.RunWithWindow(...)`.
- `DesignItemProperty`/`DesignItemPropertyCollection` are **string-keyed only** — no `DependencyProperty`-indexed overload exists anywhere in this codebase (established in Phase 4a, `docs/superpowers/specs/2026-09-01-winui-designer-phase4a-design.md:163-169`). Any new code bridging a `DependencyProperty` back into a `Properties[...]` lookup must go through a plain property-name string.
- `DependencyProperty.GetFullName()`/`DesignItemProperty.DependencyFullName` are **confirmed broken** — WinUI's `DependencyProperty` does not override `ToString()`, so `GetFullName()` returns the same literal string (`"Microsoft.UI.Xaml.DependencyProperty"`) for every property. Do not call either from any code this plan adds. Use `DesignItemProperty.FullName` (`DeclaringType.FullName + "." + Name`, already correct) and `DesignItemProperty.DependencyProperty` (already correct) instead.
- Ported files (`Metadata.cs`) keep the original SharpDevelop MIT copyright header, per `CLAUDE.md`'s "Working with SharpDevelop-derived files".
- No `BasicMetadata`-style real seed data in this phase (spec Decision 6) — every test seeds `Metadata` directly via its own `Metadata.AddXxx` calls.

---

### Task 1: `Metadata` class + `NumberRange`

**Files:**
- Create: `src/WinUIDesigner.DesignModel/Metadata.cs`
- Test: `tests/WinUIDesigner.DesignModel.Tests/MetadataTests.cs`

**Interfaces:**
- Consumes: `WinUIDesigner.DesignModel.DesignItemProperty` (`.DependencyProperty`, `.FullName` — both already exist, `DesignItemProperty.cs:53,56-58`).
- Produces (used by Tasks 2-4): `Metadata.IsPlacementDisabled(Type)` / `Metadata.DisablePlacement(Type)`; `Metadata.GetDefaultSize(Type, bool checkBasetype = true)` / `Metadata.AddDefaultSize(Type, Windows.Foundation.Size)`; `Metadata.GetDefaultPropertyValues(Type)` returning `Dictionary<string, object>` / `Metadata.AddDefaultPropertyValue(Type, string, object)`.

- [ ] **Step 1: Write the failing test file**

Create `tests/WinUIDesigner.DesignModel.Tests/MetadataTests.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using System.Xml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NUnit.Framework;
using WinUIDesigner.DesignModel.Xaml;
using WinUIDesigner.TestHost;

namespace WinUIDesigner.DesignModel.Tests
{
    [TestFixture]
    public class MetadataTests
    {
        const string ButtonXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Button x:Name=\"Target\" />" +
            "</Grid>";

        static DesignItemProperty LoadProperty(string propertyName)
        {
            using var reader = XmlReader.Create(new StringReader(ButtonXaml));
            var context = new XamlDesignContext(reader, new XamlLoadSettings());
            var grid = (Grid)context.RootItem.Component;
            var button = (Button)grid.Children[0];
            var buttonItem = context.Services.Component.GetDesignItem(button);
            return buttonItem.Properties.GetProperty(propertyName);
        }

        // --- Marker types, one per test, so no two tests can disagree about the same
        // Metadata dictionary entry regardless of NUnit's run order. ---
        class StandardValuesTarget { }
        static class StandardValuesHolder
        {
            public static string Alpha => "alpha";
            public static string Beta => "beta";
        }
        class NamedStandardValuesTarget { }
        static class NamedStandardValuesHolder
        {
            public static double Small => 1.0;
            public static double Large => 100.0;
        }
        class GenericStandardValuesTarget { }
        enum ProbeEnum { First, Second }
        class DefaultSizeExactTarget { }
        class DefaultSizeBaseTarget { }
        class DefaultSizeDerivedTarget : DefaultSizeBaseTarget { }
        class PopularControlTarget { }
        class PlacementDisabledTarget { }
        class DefaultPropertyValueTarget { }

        [Test]
        public void AddStandardValuesFromContainerTypeRegistersEachStaticPropertyValue()
        {
            Metadata.AddStandardValues(typeof(StandardValuesTarget), typeof(StandardValuesHolder));
            var values = Metadata.GetStandardValues(typeof(StandardValuesTarget)).Cast<object>().ToList();
            CollectionAssert.Contains(values, "alpha");
            CollectionAssert.Contains(values, "beta");
        }

        [Test]
        public void AddDoubleNamedStandardValuesRegistersOneEntryPerStaticProperty()
        {
            Metadata.AddDoubleNamedStandardValues(typeof(NamedStandardValuesTarget), typeof(NamedStandardValuesHolder));
            var values = Metadata.GetNamedStandardValues(typeof(NamedStandardValuesTarget));
            Assert.AreEqual(2, values.Cast<object>().Count());
        }

        [Test]
        public void AddStandardValuesGenericOverloadRegistersSuppliedValues()
        {
            Metadata.AddStandardValues(typeof(GenericStandardValuesTarget), new[] { 1, 2, 3 });
            var values = Metadata.GetStandardValues(typeof(GenericStandardValuesTarget)).Cast<object>().ToList();
            CollectionAssert.Contains(values, 1);
            CollectionAssert.Contains(values, 2);
            CollectionAssert.Contains(values, 3);
        }

        [Test]
        public void GetStandardValuesForAnEnumTypeReturnsAllEnumValuesEvenWithoutRegistration()
        {
            var values = Metadata.GetStandardValues(typeof(ProbeEnum)).Cast<ProbeEnum>().ToList();
            CollectionAssert.AreEquivalent(new[] { ProbeEnum.First, ProbeEnum.Second }, values);
        }

        [Test]
        public void GetStandardValuesForANullableEnumTypeReturnsTheUnderlyingEnumValues()
        {
            var values = Metadata.GetStandardValues(typeof(ProbeEnum?)).Cast<ProbeEnum>().ToList();
            CollectionAssert.AreEquivalent(new[] { ProbeEnum.First, ProbeEnum.Second }, values);
        }

        [Test]
        public void GetStandardValuesForAnUnregisteredTypeReturnsNull()
        {
            Assert.IsNull(Metadata.GetStandardValues(typeof(DateTime)));
        }

        [Test]
        public void AddStandardValuesForADependencyPropertyIsRetrievedByTheSameDependencyProperty()
        {
            WinUITestHost.Run(() => {
                var widthProperty = LoadProperty("Width");
                Metadata.AddStandardValues(widthProperty.DependencyProperty, "small", "medium", "large");
                var values = Metadata.GetStandardValues(widthProperty.DependencyProperty).Cast<object>().ToList();
                CollectionAssert.AreEqual(new object[] { "small", "medium", "large" }, values);
            });
        }

        [Test]
        public void GetStandardValuesForAnUnregisteredDependencyPropertyReturnsNull()
        {
            WinUITestHost.Run(() => {
                var tagProperty = LoadProperty("Tag");
                Assert.IsNull(Metadata.GetStandardValues(tagProperty.DependencyProperty));
            });
        }

        [Test]
        public void HidePropertyByDependencyPropertyMakesItNotBrowsable()
        {
            WinUITestHost.Run(() => {
                var heightProperty = LoadProperty("Height");
                Metadata.HideProperty(heightProperty.DependencyProperty);
                Assert.IsFalse(Metadata.IsBrowsable(heightProperty));
            });
        }

        [Test]
        public void HidePropertyByTypeAndMemberNameMakesItNotBrowsable()
        {
            WinUITestHost.Run(() => {
                var minWidthProperty = LoadProperty("MinWidth");
                Metadata.HideProperty(minWidthProperty.DeclaringType, minWidthProperty.Name);
                Assert.IsFalse(Metadata.IsBrowsable(minWidthProperty));
            });
        }

        [Test]
        public void APropertyThatWasNeverHiddenIsBrowsable()
        {
            WinUITestHost.Run(() => {
                var opacityProperty = LoadProperty("Opacity");
                Assert.IsTrue(Metadata.IsBrowsable(opacityProperty));
            });
        }

        [Test]
        public void AddPopularPropertyByDependencyPropertyMakesItPopular()
        {
            WinUITestHost.Run(() => {
                var maxWidthProperty = LoadProperty("MaxWidth");
                Metadata.AddPopularProperty(maxWidthProperty.DependencyProperty);
                Assert.IsTrue(Metadata.IsPopularProperty(maxWidthProperty));
            });
        }

        [Test]
        public void AddPopularPropertyByTypeAndMemberNameMakesItPopular()
        {
            WinUITestHost.Run(() => {
                var minHeightProperty = LoadProperty("MinHeight");
                Metadata.AddPopularProperty(minHeightProperty.DeclaringType, minHeightProperty.Name);
                Assert.IsTrue(Metadata.IsPopularProperty(minHeightProperty));
            });
        }

        [Test]
        public void APropertyThatWasNeverRegisteredIsNotPopular()
        {
            WinUITestHost.Run(() => {
                var visibilityProperty = LoadProperty("Visibility");
                Assert.IsFalse(Metadata.IsPopularProperty(visibilityProperty));
            });
        }

        [Test]
        public void AddValueRangeIsRetrievedByTheSameDependencyProperty()
        {
            WinUITestHost.Run(() => {
                var maxHeightProperty = LoadProperty("MaxHeight");
                Metadata.AddValueRange(maxHeightProperty.DependencyProperty, 0, 1000);
                var range = Metadata.GetValueRange(maxHeightProperty);
                Assert.IsNotNull(range);
                Assert.AreEqual(0, range.Min);
                Assert.AreEqual(1000, range.Max);
            });
        }

        [Test]
        public void GetValueRangeForAPropertyWithNoRegisteredRangeReturnsNull()
        {
            WinUITestHost.Run(() => {
                var horizontalAlignmentProperty = LoadProperty("HorizontalAlignment");
                Assert.IsNull(Metadata.GetValueRange(horizontalAlignmentProperty));
            });
        }

        [Test]
        public void DisablePlacementMakesIsPlacementDisabledTrue()
        {
            Assert.IsFalse(Metadata.IsPlacementDisabled(typeof(PlacementDisabledTarget)));
            Metadata.DisablePlacement(typeof(PlacementDisabledTarget));
            Assert.IsTrue(Metadata.IsPlacementDisabled(typeof(PlacementDisabledTarget)));
        }

        [Test]
        public void AddPopularControlIsReflectedInGetPopularControlsAndIsPopularControl()
        {
            Assert.IsFalse(Metadata.IsPopularControl(typeof(PopularControlTarget)));
            Metadata.AddPopularControl(typeof(PopularControlTarget));
            Assert.IsTrue(Metadata.IsPopularControl(typeof(PopularControlTarget)));
            CollectionAssert.Contains(Metadata.GetPopularControls().ToList(), typeof(PopularControlTarget));
        }

        [Test]
        public void AddDefaultSizeForTheExactTypeIsReturnedRegardlessOfCheckBasetype()
        {
            Metadata.AddDefaultSize(typeof(DefaultSizeExactTarget), new Windows.Foundation.Size(42, 24));
            Assert.AreEqual(new Windows.Foundation.Size(42, 24), Metadata.GetDefaultSize(typeof(DefaultSizeExactTarget), false));
            Assert.AreEqual(new Windows.Foundation.Size(42, 24), Metadata.GetDefaultSize(typeof(DefaultSizeExactTarget), true));
        }

        [Test]
        public void GetDefaultSizeWalksToARegisteredBaseTypeOnlyWhenCheckBasetypeIsTrue()
        {
            Metadata.AddDefaultSize(typeof(DefaultSizeBaseTarget), new Windows.Foundation.Size(150, 120));
            Assert.AreEqual(new Windows.Foundation.Size(150, 120), Metadata.GetDefaultSize(typeof(DefaultSizeDerivedTarget), true));
            Assert.IsNull(Metadata.GetDefaultSize(typeof(DefaultSizeDerivedTarget), false));
        }

        [Test]
        public void GetDefaultSizeForATypeWithNoRegistrationAtAllReturnsNull()
        {
            Assert.IsNull(Metadata.GetDefaultSize(typeof(string), true));
        }

        [Test]
        public void AddDefaultPropertyValueIsRetrievedByTypeAndPropertyName()
        {
            Metadata.AddDefaultPropertyValue(typeof(DefaultPropertyValueTarget), "SomeProperty", "some value");
            Metadata.AddDefaultPropertyValue(typeof(DefaultPropertyValueTarget), "OtherProperty", 42);
            var values = Metadata.GetDefaultPropertyValues(typeof(DefaultPropertyValueTarget));
            Assert.AreEqual("some value", values["SomeProperty"]);
            Assert.AreEqual(42, values["OtherProperty"]);
        }

        [Test]
        public void GetDefaultPropertyValuesForAnUnregisteredTypeReturnsNull()
        {
            Assert.IsNull(Metadata.GetDefaultPropertyValues(typeof(Uri)));
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails to compile (Metadata doesn't exist yet)**

Run: `dotnet build WinUIDesigner.sln`
Expected: build FAILS — `CS0234`/`CS0103`, "The type or namespace name 'Metadata' does not exist".

- [ ] **Step 3: Create `src/WinUIDesigner.DesignModel/Metadata.cs`**

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
using System.Linq;
using System.Reflection;
using Microsoft.UI.Xaml;
using Windows.Foundation;

namespace WinUIDesigner.DesignModel
{
    /// <summary>
    /// Ported from the reference's Metadata.cs (WpfDesign/Project/Metadata.cs, 458 lines) - the
    /// "live" subset only (its Category/IsAdvanced cluster is commented out and dead in the
    /// reference itself, so it is not ported here either). GetFullName is not re-ported - it
    /// already exists on DependencyProperty via DesignItemProperty.cs's
    /// DependencyPropertyExtensions, and is confirmed broken there (WinUI's DependencyProperty
    /// doesn't override ToString(), so it returns the same literal string for every property -
    /// see the Phase 4e spec's "Verified against the current codebase"). HideProperty/
    /// AddPopularProperty's DependencyProperty-taking overloads therefore key by the
    /// DependencyProperty object's own identity in a separate store from their Type,string
    /// overloads (spec Decision 3a) instead of unifying both into one GetFullName()-keyed string
    /// space the way the reference does. AddDefaultPropertyValue/GetDefaultPropertyValues key by
    /// a plain property-name string instead of DependencyProperty, since DesignItemProperty/
    /// DesignItemPropertyCollection are string-keyed only (spec Decision 5a - the same deviation
    /// ModelTools.ApplyTransform already made for the same reason). DisablePlacement/
    /// IsPlacementDisabled are merged in from PlacementMetadata.cs (deleted by Task 2), using the
    /// reference's own names rather than that file's RegisterPlacementDisabled.
    /// </summary>
    public static class Metadata
    {
        class NamedValue
        {
            public string Name { get; set; }
            public object Value { get; set; }
        }

        static readonly Dictionary<Type, List<object>> standardValues = new Dictionary<Type, List<object>>();
        static readonly Dictionary<DependencyProperty, object[]> standardValuesForDependencyPropertys = new Dictionary<DependencyProperty, object[]>();
        static readonly Dictionary<Type, List<NamedValue>> standardNamedValues = new Dictionary<Type, List<NamedValue>>();

        public static void AddStandardValues(Type type, Type valuesContainer)
        {
            AddStandardValues(type,
                valuesContainer.GetProperties(BindingFlags.Public | BindingFlags.Static)
                    .Select(p => p.GetValue(null, null)));
        }

        public static void AddStandardValues(DependencyProperty dependencyProperty, params object[] values)
        {
            lock (standardValuesForDependencyPropertys) {
                standardValuesForDependencyPropertys[dependencyProperty] = values;
            }
        }

        public static void AddDoubleNamedStandardValues(Type type, Type valuesContainer)
        {
            var pList = valuesContainer.GetProperties(BindingFlags.Public | BindingFlags.Static)
                .Select(p => new NamedValue { Name = p.Name, Value = p.GetValue(null, null) });

            lock (standardNamedValues) {
                List<NamedValue> list;
                if (!standardNamedValues.TryGetValue(type, out list)) {
                    list = new List<NamedValue>();
                    standardNamedValues[type] = list;
                }
                foreach (var v in pList) {
                    list.Add(v);
                }
            }
        }

        public static void AddStandardValues<T>(Type type, IEnumerable<T> values)
        {
            lock (standardValues) {
                List<object> list;
                if (!standardValues.TryGetValue(type, out list)) {
                    list = new List<object>();
                    standardValues[type] = list;
                }
                foreach (var v in values) {
                    list.Add(v);
                }
            }
        }

        public static IEnumerable GetNamedStandardValues(Type type)
        {
            lock (standardNamedValues) {
                List<NamedValue> values;
                if (standardNamedValues.TryGetValue(type, out values)) {
                    return values;
                }
            }
            return null;
        }

        public static IEnumerable GetStandardValues(Type type)
        {
            var baseT = Nullable.GetUnderlyingType(type);

            if (type.IsEnum) {
                return Enum.GetValues(type);
            }

            if (baseT != null && baseT.IsEnum) {
                return Enum.GetValues(baseT);
            }

            lock (standardValues) {
                List<object> values;
                if (standardValues.TryGetValue(type, out values)) {
                    return values;
                }
            }
            return null;
        }

        public static IEnumerable GetStandardValues(DependencyProperty dependencyProperty)
        {
            lock (standardValuesForDependencyPropertys) {
                object[] values;
                if (standardValuesForDependencyPropertys.TryGetValue(dependencyProperty, out values)) {
                    return values;
                }
            }
            return null;
        }

        static readonly HashSet<DependencyProperty> hiddenDependencyProperties = new HashSet<DependencyProperty>();
        static readonly HashSet<string> hiddenProperties = new HashSet<string>();

        public static void HideProperty(DependencyProperty p)
        {
            lock (hiddenDependencyProperties) hiddenDependencyProperties.Add(p);
        }

        public static void HideProperty(Type type, string member)
        {
            lock (hiddenProperties) hiddenProperties.Add(type.FullName + "." + member);
        }

        public static bool IsBrowsable(DesignItemProperty p)
        {
            if (p.DependencyProperty != null) {
                lock (hiddenDependencyProperties) {
                    if (hiddenDependencyProperties.Contains(p.DependencyProperty)) return false;
                }
            }
            lock (hiddenProperties) {
                if (hiddenProperties.Contains(p.FullName)) return false;
            }
            return true;
        }

        static readonly HashSet<DependencyProperty> popularDependencyProperties = new HashSet<DependencyProperty>();
        static readonly HashSet<string> popularProperties = new HashSet<string>();

        public static void AddPopularProperty(DependencyProperty p)
        {
            lock (popularDependencyProperties) popularDependencyProperties.Add(p);
        }

        public static void AddPopularProperty(Type type, string member)
        {
            lock (popularProperties) popularProperties.Add(type.FullName + "." + member);
        }

        public static bool IsPopularProperty(DesignItemProperty p)
        {
            if (p.DependencyProperty != null) {
                lock (popularDependencyProperties) {
                    if (popularDependencyProperties.Contains(p.DependencyProperty)) return true;
                }
            }
            lock (popularProperties) {
                if (popularProperties.Contains(p.FullName)) return true;
            }
            return false;
        }

        static readonly HashSet<Type> popularControls = new HashSet<Type>();

        public static void AddPopularControl(Type t)
        {
            lock (popularControls) popularControls.Add(t);
        }

        public static IEnumerable<Type> GetPopularControls()
        {
            lock (popularControls) return popularControls.ToArray();
        }

        public static bool IsPopularControl(Type t)
        {
            lock (popularControls) return popularControls.Contains(t);
        }

        static readonly Dictionary<DependencyProperty, NumberRange> ranges = new Dictionary<DependencyProperty, NumberRange>();

        public static void AddValueRange(DependencyProperty p, double min, double max)
        {
            lock (ranges) ranges[p] = new NumberRange { Min = min, Max = max };
        }

        public static NumberRange GetValueRange(DesignItemProperty p)
        {
            if (p.DependencyProperty == null) return null;
            lock (ranges) {
                NumberRange r;
                if (ranges.TryGetValue(p.DependencyProperty, out r)) return r;
            }
            return null;
        }

        static readonly HashSet<Type> placementDisabled = new HashSet<Type>();

        public static void DisablePlacement(Type type)
        {
            lock (placementDisabled) placementDisabled.Add(type);
        }

        public static bool IsPlacementDisabled(Type type)
        {
            lock (placementDisabled) return placementDisabled.Contains(type);
        }

        static readonly Dictionary<Type, Size> defaultSizes = new Dictionary<Type, Size>();

        public static void AddDefaultSize(Type t, Size s)
        {
            lock (defaultSizes) defaultSizes[t] = s;
        }

        public static Size? GetDefaultSize(Type t, bool checkBasetype = true)
        {
            lock (defaultSizes) {
                while (t != null) {
                    Size s;
                    if (defaultSizes.TryGetValue(t, out s)) return s;
                    t = checkBasetype ? t.BaseType : null;
                }
            }
            return null;
        }

        static readonly Dictionary<Type, Dictionary<string, object>> standardPropertyValues = new Dictionary<Type, Dictionary<string, object>>();

        public static void AddDefaultPropertyValue(Type t, string propertyName, object value)
        {
            lock (standardPropertyValues) {
                Dictionary<string, object> forType;
                if (!standardPropertyValues.TryGetValue(t, out forType)) {
                    forType = new Dictionary<string, object>();
                    standardPropertyValues[t] = forType;
                }
                forType[propertyName] = value;
            }
        }

        public static Dictionary<string, object> GetDefaultPropertyValues(Type t)
        {
            lock (standardPropertyValues) {
                Dictionary<string, object> forType;
                if (standardPropertyValues.TryGetValue(t, out forType)) return forType;
                return null;
            }
        }
    }

    /// <summary>
    /// Represents the minimum and maximum valid value for a double property.
    /// </summary>
    public class NumberRange
    {
        public double Min { get; set; }
        public double Max { get; set; }
    }
}
```

- [ ] **Step 4: Build and run the new tests**

Run:
```
dotnet build WinUIDesigner.sln
tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe --noresult --test=WinUIDesigner.DesignModel.Tests.MetadataTests
```
Expected: build succeeds, all `MetadataTests` cases PASS.

- [ ] **Step 5: Commit**

```bash
git add src/WinUIDesigner.DesignModel/Metadata.cs tests/WinUIDesigner.DesignModel.Tests/MetadataTests.cs
git commit -m "feat: port Metadata static registry from reference"
```

---

### Task 2: Merge `PlacementMetadata` into `Metadata`

**Files:**
- Delete: `src/WinUIDesigner.DesignModel/PlacementMetadata.cs`
- Modify: `src/WinUIDesigner.Designer/Extensions/DefaultPlacementBehavior.cs:41`
- Test: `tests/WinUIDesigner.Designer.Tests/DefaultPlacementBehaviorTests.cs`

**Interfaces:**
- Consumes: `Metadata.IsPlacementDisabled(Type)` / `Metadata.DisablePlacement(Type)` (Task 1).
- Produces: nothing new — `DefaultPlacementBehavior.OnInitialized` keeps its existing behavior, only its backing store changes.

- [ ] **Step 1: Write the failing test**

Add to `tests/WinUIDesigner.Designer.Tests/DefaultPlacementBehaviorTests.cs`, inside the `DefaultPlacementBehaviorTests` class (after the existing two tests):

```csharp
        // Regression for the PlacementMetadata -> Metadata merge (Phase 4e Task 2): no existing
        // test exercised the placement-disabled branch at all before this - PlacementMetadata had
        // exactly one caller (this behavior) and zero direct test coverage.
        [Test]
        public void APlacementDisabledTypeDoesNotAttachDefaultPlacementBehavior()
        {
            WinUITestHost.RunWithWindow(window => {
                Metadata.DisablePlacement(typeof(Microsoft.UI.Xaml.Controls.Button));
                try {
                    var context = Load(ButtonInGridXaml);
                    window.Content = (Microsoft.UI.Xaml.UIElement)context.RootItem.View;
                    ((Microsoft.UI.Xaml.FrameworkElement)window.Content).UpdateLayout();

                    var button = (Microsoft.UI.Xaml.Controls.Button)((Microsoft.UI.Xaml.Controls.Grid)context.RootItem.Component).Children[0];
                    var buttonItem = context.Services.Component.GetDesignItem(button);

                    // PlacementOperation.GetPlacementBehavior (PlacementOperation.cs:231) returns
                    // parent.GetBehavior<IPlacementBehavior>() - null when no extension ever
                    // called AddBehavior(typeof(IPlacementBehavior), ...), which is exactly what
                    // DefaultPlacementBehavior.OnInitialized skips when Metadata.IsPlacementDisabled
                    // is true. It does not throw.
                    Assert.IsNull(PlacementOperation.GetPlacementBehavior(new[] { buttonItem }));
                } finally {
                    // Metadata is process-wide static state (matches the reference's own design) -
                    // undo the registration so later tests in this same process see a plain Button
                    // again. There is no Metadata.EnablePlacement/RemoveDisabledPlacement (the
                    // reference has none either) - a fresh HashSet swap is the only way back.
                    typeof(Metadata)
                        .GetField("placementDisabled", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                        .SetValue(null, new System.Collections.Generic.HashSet<System.Type>());
                }
            });
        }
```

`DefaultPlacementBehaviorTests.cs` already has `using WinUIDesigner.DesignModel;` at its top (confirmed by reading the file) — `Metadata`, `PlacementOperation`, and `Type` all resolve from that plus the implicit `System` reference every C# project has; no new `using` is needed.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet build WinUIDesigner.sln && tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult --test=WinUIDesigner.Designer.Tests.DefaultPlacementBehaviorTests.APlacementDisabledTypeDoesNotAttachDefaultPlacementBehavior`
Expected: build FAILS — `Metadata.DisablePlacement` compiles fine (Task 1 shipped it), but the assertion fails because `DefaultPlacementBehavior.cs:41` still checks `PlacementMetadata`, not `Metadata` — the behavior still attaches. (If it fails to COMPILE instead because `Metadata` isn't visible, add `using WinUIDesigner.DesignModel;` — it should already be there from the existing tests in this file.)

- [ ] **Step 3: Update the call site and delete the old file**

In `src/WinUIDesigner.Designer/Extensions/DefaultPlacementBehavior.cs`, change line 41:

```csharp
            if (ExtendedItem.ContentProperty == null || PlacementMetadata.IsPlacementDisabled(ExtendedItem.ComponentType))
```
to:
```csharp
            if (ExtendedItem.ContentProperty == null || Metadata.IsPlacementDisabled(ExtendedItem.ComponentType))
```

Delete `src/WinUIDesigner.DesignModel/PlacementMetadata.cs` entirely.

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet build WinUIDesigner.sln && tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult --test=WinUIDesigner.Designer.Tests.DefaultPlacementBehaviorTests`
Expected: build succeeds (deleting `PlacementMetadata.cs` produces no other compile errors — it had exactly one caller, now updated), all three tests in `DefaultPlacementBehaviorTests` PASS.

- [ ] **Step 5: Run the full Designer.Tests and DesignModel.Tests suites to confirm no regression**

Run:
```
tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe --noresult
tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult
```
Expected: both fully green, same pass counts as before this task plus the one new test.

- [ ] **Step 6: Commit**

```bash
git add -u src/WinUIDesigner.DesignModel/PlacementMetadata.cs src/WinUIDesigner.Designer/Extensions/DefaultPlacementBehavior.cs tests/WinUIDesigner.Designer.Tests/DefaultPlacementBehaviorTests.cs
git commit -m "refactor: merge PlacementMetadata into Metadata"
```

---

### Task 3: Wire `Metadata.GetDefaultSize` into `ModelTools.GetDefaultSize`

**Files:**
- Modify: `src/WinUIDesigner.Designer/ModelTools.cs:36-60`
- Test: `tests/WinUIDesigner.Designer.Tests/ModelToolsTests.cs`

**Interfaces:**
- Consumes: `Metadata.GetDefaultSize(Type, bool)` / `Metadata.AddDefaultSize(Type, Size)` (Task 1); `DesignItem.ComponentType` (existing).
- Produces: `ModelTools.GetDefaultSize(DesignItem)` keeps its existing public signature — no caller elsewhere in the codebase needs to change.

**Empirically-verified facts this task's tests rely on** (probed directly against the real `.exe` during plan-writing, then reverted — no residue left in the repo): using the existing `ModelToolsTests.Load(window)` fixture's pattern (a `<Grid>` root with real designer-assembly registration, real `window.Content` + `UpdateLayout()`), measuring `Measure(new Size(double.PositiveInfinity, double.PositiveInfinity))` on:
- an empty, sizeless nested `<Grid x:Name="Target" />` → `DesiredSize` = `(0, 0)`.
- a sizeless `<Button x:Name="Target" />` → `DesiredSize` = `(20, 13)`.
- a `<Button x:Name="Target" Width="40" Height="20" />` (the existing fixture's exact XAML) → `DesiredSize` = `(40, 20)`.

- [ ] **Step 1: Write the failing tests**

Add to `tests/WinUIDesigner.Designer.Tests/ModelToolsTests.cs`, inside the `ModelToolsTests` class. First add a second fixture helper (the existing `Load` always sets `Width="40" Height="20"` on its Button, which is wrong for testing the registry's override branch — these new tests need elements with no explicit size):

```csharp
        const string NestedGridXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Grid x:Name=\"Target\" />" +
            "</Grid>";

        const string SizelessButtonXaml =
            "<Grid xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
            "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
            "<Button x:Name=\"Target\" />" +
            "</Grid>";

        static WinUIDesigner.DesignModel.DesignItem LoadTarget(Microsoft.UI.Xaml.Window window, string xaml)
        {
            var settings = new XamlLoadSettings();
            settings.DesignerAssemblies.Add(typeof(DefaultPlacementBehavior).Assembly);
            settings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(WinUIDesigner.DesignModel.ISelectionService), new WinUIDesigner.Designer.Services.DefaultSelectionService()));
            settings.CustomServiceRegisterFunctions.Add(ctx => ctx.Services.AddService(typeof(WinUIDesigner.DesignModel.IDesignPanel), new WinUIDesigner.Designer.DesignPanel()));
            using var reader = XmlReader.Create(new StringReader(xaml));
            var context = new XamlDesignContext(reader, settings);
            window.Content = (Microsoft.UI.Xaml.UIElement)context.RootItem.View;
            ((Microsoft.UI.Xaml.FrameworkElement)window.Content).UpdateLayout();
            var target = ((Microsoft.UI.Xaml.Controls.Grid)context.RootItem.Component).Children[0];
            return context.Services.Component.GetDesignItem(target);
        }

        // Metadata's registries are process-wide static state (matches the reference's own
        // design - see spec Decision 6). AddDefaultSize is being registered here against real,
        // widely-shared WinUI types (Grid/Panel/Control) that other tests elsewhere in this same
        // process may also create - leaving a registration behind would make GetDefaultSize's
        // result for THOSE tests depend on whether this file happened to run first. Every test
        // below that calls AddDefaultSize resets the registry in a finally block; there is no
        // Metadata.RemoveDefaultSize (the reference has none either), so resetting means
        // replacing the whole private dictionary via reflection.
        static void ResetDefaultSizeRegistry()
        {
            typeof(WinUIDesigner.DesignModel.Metadata)
                .GetField("defaultSizes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .SetValue(null, new System.Collections.Generic.Dictionary<System.Type, Windows.Foundation.Size>());
        }

        [Test]
        public void AnExactTypeRegistryEntryShortCircuitsMeasureEntirely()
        {
            WinUITestHost.RunWithWindow(window => {
                var item = LoadTarget(window, NestedGridXaml);
                WinUIDesigner.DesignModel.Metadata.AddDefaultSize(typeof(Microsoft.UI.Xaml.Controls.Grid), new Windows.Foundation.Size(300, 200));
                try {
                    var size = WinUIDesigner.Designer.ModelTools.GetDefaultSize(item);
                    Assert.AreEqual(300, size.Width);
                    Assert.AreEqual(200, size.Height);
                } finally {
                    ResetDefaultSizeRegistry();
                }
            });
        }

        [Test]
        public void ABaseTypeRegistryEntryOverridesASmallMeasuredDimension()
        {
            WinUITestHost.RunWithWindow(window => {
                var item = LoadTarget(window, NestedGridXaml); // measures (0,0) - see Empirically-verified facts
                WinUIDesigner.DesignModel.Metadata.AddDefaultSize(typeof(Microsoft.UI.Xaml.Controls.Panel), new Windows.Foundation.Size(150, 120));
                try {
                    var size = WinUIDesigner.Designer.ModelTools.GetDefaultSize(item);
                    Assert.AreEqual(150, size.Width);
                    Assert.AreEqual(120, size.Height);
                } finally {
                    ResetDefaultSizeRegistry();
                }
            });
        }

        [Test]
        public void ARegistryEntryIsIgnoredWhenTheMeasuredDimensionIsAlreadyLargerThanFive()
        {
            WinUITestHost.RunWithWindow(window => {
                var item = LoadTarget(window, SizelessButtonXaml); // measures (20,13) - see Empirically-verified facts
                WinUIDesigner.DesignModel.Metadata.AddDefaultSize(typeof(Microsoft.UI.Xaml.Controls.Control), new Windows.Foundation.Size(999, 888));
                try {
                    var size = WinUIDesigner.Designer.ModelTools.GetDefaultSize(item);
                    Assert.AreEqual(20, size.Width);
                    Assert.AreEqual(13, size.Height);
                } finally {
                    ResetDefaultSizeRegistry();
                }
            });
        }

        [Test]
        public void WithNoRegistrationAtAllTheExistingMeasureBehaviorIsUnchanged()
        {
            WinUITestHost.RunWithWindow(window => {
                var (context, button, buttonItem) = Load(window); // existing fixture: Width=40, Height=20 explicit
                var size = WinUIDesigner.Designer.ModelTools.GetDefaultSize(buttonItem);
                Assert.AreEqual(40, size.Width);
                Assert.AreEqual(20, size.Height);
            });
        }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build WinUIDesigner.sln && tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult --test=WinUIDesigner.Designer.Tests.ModelToolsTests`
Expected: `AnExactTypeRegistryEntryShortCircuitsMeasureEntirely` and `ABaseTypeRegistryEntryOverridesASmallMeasuredDimension` FAIL (current `GetDefaultSize` ignores `Metadata` entirely, so it returns the raw measured `(0,0)` instead of the registered size); `ARegistryEntryIsIgnoredWhenTheMeasuredDimensionIsAlreadyLargerThanFive` and `WithNoRegistrationAtAllTheExistingMeasureBehaviorIsUnchanged` PASS already (nothing to override in either case with the current code).

- [ ] **Step 3: Update `ModelTools.GetDefaultSize`**

In `src/WinUIDesigner.Designer/ModelTools.cs`, replace the method (currently lines 36-60, including its doc comment) with:

```csharp
        /// <summary>Gets the default size to use for a newly-created item with no explicit size
        /// of its own, so a click-without-drag placement (CreateComponentTool.
        /// AddItemsWithDefaultSize, Phase 4d Task 4) doesn't add a 0x0 control. Ported in
        /// trimmed form from the reference's ModelTools.GetDefaultSize (WpfDesign.Designer/
        /// Project/ModelTools.cs:161-190). One reference layer is still dropped, not ported:
        /// CreateVisualTree's XPS-serializer trick (a WPF-only device to force layout on an
        /// element with no visual parent - WinUI's UIElement.Measure works directly on an
        /// unparented element without it). Metadata.GetDefaultSize's per-type registry (Phase 4e)
        /// is now wired in, matching the reference's exact composition: an exact-type hit
        /// short-circuits before Measure ever runs; otherwise a base-type-walking hit overrides
        /// a measured dimension only when that dimension is not already larger than 5.</summary>
        public static Size GetDefaultSize(DesignItem createdItem)
        {
            var type = createdItem.ComponentType;
            var exact = Metadata.GetDefaultSize(type, false);
            if (exact.HasValue) return exact.Value;

            var view = createdItem.View;
            view.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var s = view.DesiredSize;

            var walked = Metadata.GetDefaultSize(type, true);
            if (walked.HasValue) {
                if (!(s.Width > 5) && walked.Value.Width > 0) s.Width = walked.Value.Width;
                if (!(s.Height > 5) && walked.Value.Height > 0) s.Height = walked.Value.Height;
            }

            if (double.IsNaN(s.Width) && GetWidth(view) > 0) s.Width = GetWidth(view);
            if (double.IsNaN(s.Height) && GetHeight(view) > 0) s.Height = GetHeight(view);

            return s;
        }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet build WinUIDesigner.sln && tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult --test=WinUIDesigner.Designer.Tests.ModelToolsTests`
Expected: all four new tests PASS, plus the file's two pre-existing tests still pass.

- [ ] **Step 5: Run the full Designer.Tests suite to confirm no regression**

Run: `tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult`
Expected: fully green, including every `CreateComponentToolTests` case (which calls `AddItemsWithDefaultSize` → `ModelTools.GetDefaultSize` internally) — those items are all real controls (`Button`/`Polyline`) with no `Metadata` registrations in play, so their sizes must come out exactly as before this task's change.

- [ ] **Step 6: Commit**

```bash
git add src/WinUIDesigner.Designer/ModelTools.cs tests/WinUIDesigner.Designer.Tests/ModelToolsTests.cs
git commit -m "feat: wire Metadata.GetDefaultSize registry into ModelTools.GetDefaultSize"
```

---

### Task 4: `IComponentService.SetDefaultPropertyValues` + `CreateComponentTool` wiring

**Files:**
- Modify: `src/WinUIDesigner.DesignModel/Services.cs:26-37` (`IComponentService` interface)
- Modify: `src/WinUIDesigner.DesignModel/Xaml/XamlComponentService.cs`
- Modify: `src/WinUIDesigner.Designer/Services/CreateComponentTool.cs:201-211` (`CreateItem`)
- Test: `tests/WinUIDesigner.Designer.Tests/CreateComponentToolTests.cs`

**Interfaces:**
- Consumes: `Metadata.GetDefaultPropertyValues(Type)` returning `Dictionary<string, object>` (Task 1); `DesignItemPropertyCollection.this[string]`/`DesignItemProperty.SetValue(object)` (both already exist).
- Produces: `IComponentService.SetDefaultPropertyValues(DesignItem designItem)` — a new interface member every `IComponentService` implementation must now provide (there is exactly one implementation in this codebase, `XamlComponentService`).

- [ ] **Step 1: Write the failing test**

Add to `tests/WinUIDesigner.Designer.Tests/CreateComponentToolTests.cs`, inside the `CreateComponentToolTests` class:

```csharp
        // Metadata.GetDefaultPropertyValues is process-wide static state (spec Decision 6) being
        // registered here against the real, widely-shared Button type - other tests in this file
        // (e.g. ADeclinedClickReportsItWasNotHandledSoOtherPanelSubscribersStillSeeIt, which also
        // creates a Button) must not silently pick up "Default Text" depending on run order.
        // There is no Metadata.RemoveDefaultPropertyValue (the reference has none either), so
        // resetting means replacing the whole private dictionary via reflection.
        static void ResetDefaultPropertyValueRegistry()
        {
            typeof(WinUIDesigner.DesignModel.Metadata)
                .GetField("standardPropertyValues", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .SetValue(null, new System.Collections.Generic.Dictionary<System.Type, System.Collections.Generic.Dictionary<string, object>>());
        }

        [Test]
        public void CreatingAnItemWithRegisteredDefaultPropertyValuesAppliesThemToTheNewItem()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window);
                WinUIDesigner.DesignModel.Metadata.AddDefaultPropertyValue(
                    typeof(Microsoft.UI.Xaml.Controls.Button), "Content", "Default Text");
                try {
                    var createdItem = CreateComponentTool.CreateItem(surface.DesignContext, typeof(Microsoft.UI.Xaml.Controls.Button), null);
                    Assert.AreEqual("Default Text", ((Microsoft.UI.Xaml.Controls.Button)createdItem.View).Content);
                } finally {
                    ResetDefaultPropertyValueRegistry();
                }
            });
        }

        [Test]
        public void CreatingAnItemOfATypeWithNoRegisteredDefaultPropertyValuesIsUnaffected()
        {
            WinUITestHost.RunWithWindow(window => {
                var surface = Load(window);

                var createdItem = CreateComponentTool.CreateItem(surface.DesignContext, typeof(Microsoft.UI.Xaml.Controls.Canvas), null);

                Assert.IsNotNull(createdItem);
                Assert.IsInstanceOf<Microsoft.UI.Xaml.Controls.Canvas>(createdItem.View);
            });
        }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build WinUIDesigner.sln && tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult --test=WinUIDesigner.Designer.Tests.CreateComponentToolTests`
Expected: `CreatingAnItemWithRegisteredDefaultPropertyValuesAppliesThemToTheNewItem` FAILS — `Content` is still `null`, since nothing calls `SetDefaultPropertyValues` yet. `CreatingAnItemOfATypeWithNoRegisteredDefaultPropertyValuesIsUnaffected` already PASSES (nothing to apply either way).

- [ ] **Step 3: Add `SetDefaultPropertyValues` to `IComponentService`**

In `src/WinUIDesigner.DesignModel/Services.cs`, add one line inside the `IComponentService` interface (after the two `RegisterComponentForDesigner` overloads):

```csharp
    public interface IComponentService
    {
        DesignItem GetDesignItem(object component);
        DesignItem GetDesignItem(object component, bool findByView);
        DesignItem RegisterComponentForDesigner(object component);
        DesignItem RegisterComponentForDesigner(DesignItem parent, object component);
        void SetDefaultPropertyValues(DesignItem designItem);

        event EventHandler<DesignItemEventArgs> ComponentRegisteredAndAddedToContainer;
        event EventHandler<DesignItemEventArgs> ComponentRegistered;
        event EventHandler<DesignItemEventArgs> ComponentRemoved;
        event EventHandler<DesignItemPropertyChangedEventArgs> PropertyChanged;
    }
```

- [ ] **Step 4: Implement it on `XamlComponentService`**

In `src/WinUIDesigner.DesignModel/Xaml/XamlComponentService.cs`, add this method right before `public DesignItem RegisterComponentForDesigner(object component)`:

```csharp
        public void SetDefaultPropertyValues(DesignItem designItem)
        {
            var values = Metadata.GetDefaultPropertyValues(designItem.ComponentType);
            if (values != null) {
                foreach (var value in values) {
                    designItem.Properties[value.Key].SetValue(value.Value);
                }
            }
        }
```

- [ ] **Step 5: Build to confirm the interface addition compiles clean**

Run: `dotnet build WinUIDesigner.sln`
Expected: build succeeds (only one `IComponentService` implementation exists, and it now has the new method — no other implementer to update).

- [ ] **Step 6: Wire the call into `CreateComponentTool.CreateItem`**

In `src/WinUIDesigner.Designer/Services/CreateComponentTool.cs`, replace the `CreateItem` method (currently lines 201-211):

```csharp
		internal static DesignItem CreateItem(DesignContext context, Type type, object[] arguments)
		{
			var newInstance = context.Services.ExtensionManager.CreateInstanceWithCustomInstanceFactory(type, arguments);
			var item = context.Services.Component.RegisterComponentForDesigner(newInstance);
			// Reference also calls context.Services.Component.SetDefaultPropertyValues(item) here
			// - IComponentService in this port has no such method (confirmed via grep across
			// src/), so it's dropped. ApplyDefaultInitializers still runs (ExtensionManager.cs:283
			// - exists, previously had zero callers anywhere in this port).
			context.Services.ExtensionManager.ApplyDefaultInitializers(item);
			return item;
		}
```

with:

```csharp
		internal static DesignItem CreateItem(DesignContext context, Type type, object[] arguments)
		{
			var newInstance = context.Services.ExtensionManager.CreateInstanceWithCustomInstanceFactory(type, arguments);
			var item = context.Services.Component.RegisterComponentForDesigner(newInstance);
			context.Services.Component.SetDefaultPropertyValues(item);
			context.Services.ExtensionManager.ApplyDefaultInitializers(item);
			return item;
		}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet build WinUIDesigner.sln && tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult --test=WinUIDesigner.Designer.Tests.CreateComponentToolTests`
Expected: both new tests PASS, plus every pre-existing case in this file still passes (in particular `ADeclinedClickReportsItWasNotHandledSoOtherPanelSubscribersStillSeeIt`, which creates a `Polyline` and a `Button` through this same `CreateItem` path with no `Metadata` registrations in play for either type — unaffected).

- [ ] **Step 8: Run the full test suite (all three projects) to confirm no regression anywhere**

Run:
```
tests/WinUIDesigner.XamlDom.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.XamlDom.Tests.exe --noresult
tests/WinUIDesigner.DesignModel.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.DesignModel.Tests.exe --noresult
tests/WinUIDesigner.Designer.Tests/bin/Debug/net10.0-windows10.0.19041.0/win-x64/WinUIDesigner.Designer.Tests.exe --noresult
```
Expected: all three fully green.

- [ ] **Step 9: Commit**

```bash
git add src/WinUIDesigner.DesignModel/Services.cs src/WinUIDesigner.DesignModel/Xaml/XamlComponentService.cs src/WinUIDesigner.Designer/Services/CreateComponentTool.cs tests/WinUIDesigner.Designer.Tests/CreateComponentToolTests.cs
git commit -m "feat: add IComponentService.SetDefaultPropertyValues, wire into CreateComponentTool"
```
