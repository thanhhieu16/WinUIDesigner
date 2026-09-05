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

            // Re-entrancy guard: see BoolEditor.Create for why - NumberBox.ValueChanged fires
            // synchronously on any Value assignment, including the model->control sync below,
            // which would otherwise spuriously Commit() back into the model before any real user
            // interaction.
            bool syncingFromModel = false;
            numberBox.DataContextChanged += (s, e) => {
                var node = numberBox.DataContext as PropertyNode;
                if (node != null) {
                    var nodeRange = Metadata.GetValueRange(node.FirstProperty);
                    if (nodeRange != null) {
                        numberBox.Minimum = nodeRange.Min;
                        numberBox.Maximum = nodeRange.Max;
                    }
                    syncingFromModel = true;
                    numberBox.Value = ToDouble(node.DesignerValue);
                    syncingFromModel = false;
                    node.PropertyChanged += (ps, pe) => {
                        if (pe.PropertyName == "DesignerValue") {
                            syncingFromModel = true;
                            numberBox.Value = ToDouble(node.DesignerValue);
                            syncingFromModel = false;
                        }
                    };
                }
            };
            numberBox.ValueChanged += (s, e) => { if (!syncingFromModel) Commit(numberBox); };
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
