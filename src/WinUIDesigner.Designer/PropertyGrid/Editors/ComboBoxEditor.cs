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
using System.Runtime.CompilerServices;
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
    /// (each entry's Name is shown, its Value is what gets committed).
    /// </summary>
    /// <remarks>
    /// The ComboBox is given plain display strings and the values are kept alongside, matched by
    /// index - no DisplayMemberPath, SelectedValuePath or item ToString involved. Both of the
    /// earlier routes rendered the wrong text: a boxed enum handed to ItemsSource crosses into
    /// WinRT as an IReference, and the default item presenter then shows its runtime class name
    /// ("Windows.Foundation.IReference`1&lt;...&gt;") rather than "Stretch" - the reading that
    /// explains the tabs walkthrough's HorizontalAlignment row showing "Windows.Foundatio",
    /// inferred from that text rather than measured; and
    /// DisplayMemberPath="Name" against Metadata's private NamedValue resolves nothing, because
    /// WinUI's binding engine cannot see a non-public type (the same cause as CLAUDE.md's blank
    /// System.Type ListView rows).
    /// </remarks>
    public static class ComboBoxEditor
    {
        public static void Register()
        {
            EditorManager.SetDefaultComboBoxEditorFactory(Create);
        }

        public static FrameworkElement Create(DesignItemProperty property)
        {
            var comboBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };

            System.Collections.IEnumerable standardValues = null;
            if (property.DependencyProperty != null) {
                standardValues = Metadata.GetStandardValues(property.DependencyProperty);
            }
            if (standardValues == null) {
                standardValues = Metadata.GetStandardValues(property.ReturnType);
            }

            var values = new List<object>();
            var names = new List<string>();
            if (standardValues != null) {
                foreach (var value in standardValues) {
                    values.Add(value);
                    names.Add(value == null ? string.Empty : value.ToString());
                }
            } else {
                var named = Metadata.GetNamedStandardValues(property.ReturnType);
                if (named != null) {
                    // NamedValue is private to Metadata; its two properties are public, so plain
                    // reflection reaches them where WinUI's binding engine could not.
                    foreach (var item in named) {
                        var type = item.GetType();
                        names.Add((string)type.GetProperty("Name").GetValue(item));
                        values.Add(type.GetProperty("Value").GetValue(item));
                    }
                }
            }
            comboBox.ItemsSource = names;
            valuesByEditor.Add(comboBox, values);

            // Re-entrancy guard: see BoolEditor.Create for why - ComboBox.SelectionChanged fires
            // synchronously on any SelectedIndex assignment, including the model->control sync
            // below, which would otherwise spuriously Commit() back into the model before any real
            // user interaction.
            bool syncingFromModel = false;
            comboBox.DataContextChanged += (s, e) => {
                var node = comboBox.DataContext as PropertyNode;
                if (node != null) {
                    syncingFromModel = true;
                    try {
                        comboBox.SelectedIndex = values.IndexOf(node.DesignerValue);
                    } finally {
                        syncingFromModel = false;
                    }
                    node.PropertyChanged += (ps, pe) => {
                        if (pe.PropertyName == "DesignerValue") {
                            syncingFromModel = true;
                            try {
                                comboBox.SelectedIndex = values.IndexOf(node.DesignerValue);
                            } finally {
                                syncingFromModel = false;
                            }
                        }
                    };
                }
            };
            comboBox.SelectionChanged += (s, e) => { if (!syncingFromModel) Commit(comboBox); };
            return comboBox;
        }

        // Keyed weakly so a discarded editor does not keep its value list (or a named value's
        // live Brush) alive.
        static readonly ConditionalWeakTable<ComboBox, List<object>> valuesByEditor = new ConditionalWeakTable<ComboBox, List<object>>();

        /// <summary>The value behind the item at <paramref name="index"/>, or null when there is none.</summary>
        internal static object ValueAt(ComboBox comboBox, int index)
        {
            List<object> values;
            if (!valuesByEditor.TryGetValue(comboBox, out values) || index < 0 || index >= values.Count) return null;
            return values[index];
        }

        static void Commit(ComboBox comboBox)
        {
            var node = comboBox.DataContext as PropertyNode;
            // Nothing selected is not a choice the user made - it is what a value outside the
            // list syncs to - so it commits nothing rather than null.
            if (node != null && comboBox.SelectedIndex >= 0) node.DesignerValue = ValueAt(comboBox, comboBox.SelectedIndex);
        }

        internal static void CommitForTest(ComboBox comboBox)
        {
            Commit(comboBox);
        }
    }
}
