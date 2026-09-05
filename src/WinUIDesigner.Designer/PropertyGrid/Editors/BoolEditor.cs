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
            // Re-entrancy guard: WinUI DependencyProperty changed events (Checked/Unchecked) fire
            // synchronously on SetValue regardless of whether the change came from code or user
            // interaction, so syncing the model's current value into checkBox.IsChecked below would
            // otherwise immediately re-fire Checked/Unchecked and spuriously Commit() back into the
            // model (undo-stack pollution the instant an editor is shown, before any real user
            // interaction). TextBoxEditor doesn't need this because LostFocus only fires from real
            // focus loss, never from a value assignment.
            bool syncingFromModel = false;
            checkBox.DataContextChanged += (s, e) => {
                var node = checkBox.DataContext as PropertyNode;
                if (node != null) {
                    syncingFromModel = true;
                    checkBox.IsChecked = node.DesignerValue as bool?;
                    syncingFromModel = false;
                    node.PropertyChanged += (ps, pe) => {
                        if (pe.PropertyName == "DesignerValue") {
                            syncingFromModel = true;
                            checkBox.IsChecked = node.DesignerValue as bool?;
                            syncingFromModel = false;
                        }
                    };
                }
            };
            checkBox.Checked += (s, e) => { if (!syncingFromModel) Commit(checkBox); };
            checkBox.Unchecked += (s, e) => { if (!syncingFromModel) Commit(checkBox); };
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
