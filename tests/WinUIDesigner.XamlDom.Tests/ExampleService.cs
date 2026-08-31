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

namespace WinUIDesigner.XamlDom.Tests
{
	/// <summary>
	/// Provides an example attached property.
	/// </summary>
	/// <remarks>
	/// WinUI's <c>DependencyProperty.RegisterAttached</c> has no 3-argument overload - the
	/// <c>PropertyMetadata</c> argument is required - hence the added <c>new PropertyMetadata(null)</c>.
	/// </remarks>
	public static class ExampleService
	{
		public static readonly DependencyProperty ExampleProperty = DependencyProperty.RegisterAttached(
			"Example", typeof(string), typeof(ExampleService), new PropertyMetadata(null)
		);

		public static string GetExample(DependencyObject element)
		{
			TestHelperLog.Log("ExampleService.GetExample");
			return (string)element.GetValue(ExampleProperty);
		}

		public static void SetExample(DependencyObject element, string value)
		{
			TestHelperLog.Log("ExampleService.SetExample");
			element.SetValue(ExampleProperty, value);
		}

		public static readonly DependencyProperty ExampleCollectionProperty = DependencyProperty.RegisterAttached(
			"ExampleCollection", typeof(ExampleClassList), typeof(ExampleService), new PropertyMetadata(null)
		);

		public static ExampleClassList GetExampleCollection(DependencyObject element)
		{
			TestHelperLog.Log("ExampleService.GetExampleCollection");
			return (ExampleClassList)element.GetValue(ExampleCollectionProperty);
		}

		public static void SetExampleCollection(DependencyObject element, ExampleClassList value)
		{
			TestHelperLog.Log("ExampleService.SetExampleCollection");
			element.SetValue(ExampleCollectionProperty, value);
		}
	}

	/// <remarks>
	/// The reference overrides <c>DependencyObject.OnPropertyChanged(DependencyPropertyChangedEventArgs)</c>
	/// here (commented-out logging only). WinUI's <c>DependencyObject</c> has no such virtual
	/// method - change notification is subscription-based via
	/// <c>RegisterPropertyChangedCallback</c> - so the empty override is dropped.
	/// </remarks>
	public class ExampleDependencyObject : DependencyObject
	{
		public static readonly DependencyProperty ExampleProperty = DependencyProperty.RegisterAttached(
			"Example", typeof(string), typeof(ExampleDependencyObject), new PropertyMetadata(null)
		);

		public static string GetExample(DependencyObject element)
		{
			TestHelperLog.Log("ExampleDependencyObject.GetExample");
			return (string)element.GetValue(ExampleProperty);
		}

		public static void SetExample(DependencyObject element, string value)
		{
			TestHelperLog.Log("ExampleDependencyObject.SetExample");
			element.SetValue(ExampleProperty, value);
		}
	}

	public class DerivedExampleDependencyObject : ExampleDependencyObject
	{
	}
}
