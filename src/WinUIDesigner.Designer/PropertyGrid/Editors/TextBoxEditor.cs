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
            if (node == null) return;
            try {
                node.ValueString = textBox.Text;
            } catch (Exception) {
                // Invalid input for this property's TypeConverter (e.g. non-numeric text typed into a
                // property whose converter expects a number) - revert the display to the last known-good
                // value rather than letting the exception escape a UI event handler.
                textBox.Text = node.ValueString;
            }
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
