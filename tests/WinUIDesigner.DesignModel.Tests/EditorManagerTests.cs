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
            // field is readonly, so we can't replace it; instead retrieve the existing dictionary
            // and clear its contents in place.
            var namedValuesField = typeof(Metadata).GetField("standardNamedValues", BindingFlags.NonPublic | BindingFlags.Static);
            ((System.Collections.IDictionary)namedValuesField.GetValue(null)).Clear();
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
