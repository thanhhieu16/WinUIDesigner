using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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

        // Metadata's registries are process-wide static state (matches the reference's own
        // design). The tests below register a REAL, shared WinUI property (Width/Height/MinWidth/
        // etc. on Button) into one of Metadata's dictionaries/hashsets - leaving that registration
        // behind would leak into every other test in the process that happens to touch the same
        // property (e.g. ModelToolsTests/CreateComponentToolTests/DefaultPlacementBehaviorTests,
        // which already guard against exactly this via their own reflection-based resets). There
        // is no Metadata.Remove*/Unhide* API (the reference has none either), and the fields are
        // `static readonly` so FieldInfo.SetValue throws FieldAccessException outside the type
        // initializer - fetching the same collection reference via GetValue and calling Clear()
        // on it achieves the same end state without attempting that disallowed write.
        static void ResetDictionary(string fieldName)
        {
            var field = typeof(Metadata).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
            ((System.Collections.IDictionary)field.GetValue(null)).Clear();
        }

        static void ResetHashSet<T>(string fieldName)
        {
            var field = typeof(Metadata).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
            ((HashSet<T>)field.GetValue(null)).Clear();
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
                try {
                    Metadata.AddStandardValues(widthProperty.DependencyProperty, "small", "medium", "large");
                    var values = Metadata.GetStandardValues(widthProperty.DependencyProperty).Cast<object>().ToList();
                    CollectionAssert.AreEqual(new object[] { "small", "medium", "large" }, values);
                } finally {
                    ResetDictionary("standardValuesForDependencyPropertys");
                }
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
                try {
                    Metadata.HideProperty(heightProperty.DependencyProperty);
                    Assert.IsFalse(Metadata.IsBrowsable(heightProperty));
                } finally {
                    ResetHashSet<DependencyProperty>("hiddenDependencyProperties");
                }
            });
        }

        [Test]
        public void HidePropertyByTypeAndMemberNameMakesItNotBrowsable()
        {
            WinUITestHost.Run(() => {
                var minWidthProperty = LoadProperty("MinWidth");
                try {
                    Metadata.HideProperty(minWidthProperty.DeclaringType, minWidthProperty.Name);
                    Assert.IsFalse(Metadata.IsBrowsable(minWidthProperty));
                } finally {
                    ResetHashSet<string>("hiddenProperties");
                }
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
                try {
                    Metadata.AddPopularProperty(maxWidthProperty.DependencyProperty);
                    Assert.IsTrue(Metadata.IsPopularProperty(maxWidthProperty));
                } finally {
                    ResetHashSet<DependencyProperty>("popularDependencyProperties");
                }
            });
        }

        [Test]
        public void AddPopularPropertyByTypeAndMemberNameMakesItPopular()
        {
            WinUITestHost.Run(() => {
                var minHeightProperty = LoadProperty("MinHeight");
                try {
                    Metadata.AddPopularProperty(minHeightProperty.DeclaringType, minHeightProperty.Name);
                    Assert.IsTrue(Metadata.IsPopularProperty(minHeightProperty));
                } finally {
                    ResetHashSet<string>("popularProperties");
                }
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
                try {
                    Metadata.AddValueRange(maxHeightProperty.DependencyProperty, 0, 1000);
                    var range = Metadata.GetValueRange(maxHeightProperty);
                    Assert.IsNotNull(range);
                    Assert.AreEqual(0, range.Min);
                    Assert.AreEqual(1000, range.Max);
                } finally {
                    ResetDictionary("ranges");
                }
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
