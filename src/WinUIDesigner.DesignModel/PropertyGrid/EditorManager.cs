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
