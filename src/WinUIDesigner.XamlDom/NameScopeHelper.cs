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

namespace WinUIDesigner.XamlDom
{
	/// <summary>
	/// Tracks x:Name -&gt; XamlObject for the owning document. WinUI has no public
	/// NameScope/INameScope object model (verified absent from Microsoft.UI.Xaml.winmd),
	/// so this keeps a flat per-document table instead of WPF's nested-scope model.
	/// </summary>
	public static class NameScopeHelper
	{
		/// <summary>
		/// Records a name change for <paramref name="namedObject"/> in its document's name table.
		/// </summary>
		public static void NameChanged(XamlObject namedObject, string oldName, string newName)
		{
			var names = namedObject.OwnerDocument.NamedObjects;
			if (oldName != null)
				names.Remove(oldName);
			if (newName != null)
				names[newName] = namedObject;
		}

		/// <summary>
		/// Looks up the <see cref="XamlObject"/> registered under <paramref name="name"/> in
		/// <paramref name="document"/>, or null if there is none.
		/// </summary>
		public static XamlObject FindNamedObject(XamlDocument document, string name)
		{
			return document.NamedObjects.TryGetValue(name, out var obj) ? obj : null;
		}
	}
}
