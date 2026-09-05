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

            // Re-entrancy guard: see BoolEditor.Create for why - ComboBox.SelectionChanged fires
            // synchronously on any SelectedItem assignment, including the model->control sync
            // below, which would otherwise spuriously Commit() back into the model before any real
            // user interaction.
            bool syncingFromModel = false;
            comboBox.DataContextChanged += (s, e) => {
                var node = comboBox.DataContext as PropertyNode;
                if (node != null) {
                    syncingFromModel = true;
                    comboBox.SelectedValue = node.DesignerValue;
                    syncingFromModel = false;
                    node.PropertyChanged += (ps, pe) => {
                        if (pe.PropertyName == "DesignerValue") {
                            syncingFromModel = true;
                            comboBox.SelectedValue = node.DesignerValue;
                            syncingFromModel = false;
                        }
                    };
                }
            };
            comboBox.SelectionChanged += (s, e) => { if (!syncingFromModel) Commit(comboBox); };
            return comboBox;
        }

        static void Commit(ComboBox comboBox)
        {
            var node = comboBox.DataContext as PropertyNode;
            if (node != null) node.DesignerValue = comboBox.SelectedValue;
        }

        internal static void CommitForTest(ComboBox comboBox)
        {
            Commit(comboBox);
        }
    }
}
