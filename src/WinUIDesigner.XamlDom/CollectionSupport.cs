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
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Documents;

namespace WinUIDesigner.XamlDom
{
	/// <summary>
	/// Static class containing helper methods to work with collections (like the XamlParser does)
	/// </summary>
	public static class CollectionSupport
	{
		/// <summary>
		/// Gets if the type is considered a collection in XAML.
		/// </summary>
		/// <remarks>
		/// The reference tests only the non-generic <see cref="IList"/>/<see cref="IDictionary"/>.
		/// That is not sufficient on WinUI: its C#/WinRT-projected collections implement the
		/// <b>generic</b> interfaces only. Verified by reflection over Microsoft.WinUI.dll -
		/// <c>UIElementCollection</c> and <c>InlineCollection</c> implement
		/// <c>IList`1</c>/<c>ICollection`1</c>/<c>IEnumerable`1</c>/<c>IEnumerable</c> but not
		/// <c>IList</c>, and <c>ResourceDictionary</c> implements <c>IDictionary`2</c> but not
		/// <c>IDictionary</c> (<c>typeof(IDictionary).IsAssignableFrom(typeof(ResourceDictionary))</c>
		/// is <c>false</c>). Without the generic checks below, <c>Grid.Children</c>,
		/// <c>Panel.Children</c>, <c>TextBlock.Inlines</c> and every <c>Resources</c> dictionary
		/// would not be recognised as collections at all and no WinUI XAML with child elements
		/// could be parsed.
		/// </remarks>
		public static bool IsCollectionType(Type type)
		{
			return type != typeof(LineBreak) && (
				typeof(IList).IsAssignableFrom(type)
				|| type.IsArray
				|| typeof(IDictionary).IsAssignableFrom(type)
				|| ImplementsGenericInterface(type, typeof(IList<>))
				|| ImplementsGenericInterface(type, typeof(IDictionary<,>)));
		}

		static bool ImplementsGenericInterface(Type type, Type openGenericInterface)
		{
			if (type.IsGenericType && type.GetGenericTypeDefinition() == openGenericInterface)
				return true;
			foreach (var i in type.GetInterfaces()) {
				if (i.IsGenericType && i.GetGenericTypeDefinition() == openGenericInterface)
					return true;
			}
			return false;
		}

		/// <summary>
		/// Returns the closed <c>IDictionary&lt;,&gt;</c> interface <paramref name="instance"/>
		/// implements, or null. Used because WinUI dictionaries (notably
		/// <c>Microsoft.UI.Xaml.ResourceDictionary</c>) do not implement the non-generic
		/// <see cref="IDictionary"/> the reference relies on.
		/// </summary>
		static Type GetGenericDictionaryInterface(object instance)
		{
			if (instance == null)
				return null;
			foreach (var i in instance.GetType().GetInterfaces()) {
				if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>))
					return i;
			}
			return null;
		}

		static void GenericDictionaryAdd(Type dictionaryInterface, object instance, object key, object value)
		{
			dictionaryInterface.GetMethod("Add", dictionaryInterface.GetGenericArguments())
				.Invoke(instance, new[] { key, value });
		}

		static void GenericDictionaryRemove(Type dictionaryInterface, object instance, object key)
		{
			dictionaryInterface.GetMethod("Remove", new[] { dictionaryInterface.GetGenericArguments()[0] })
				.Invoke(instance, new[] { key });
		}

		/// <summary>
		/// Gets if the collection type <paramref name="col"/> can accepts items of type
		/// <paramref name="item"/>.
		/// </summary>
		public static bool CanCollectionAdd(Type col, Type item)
		{
			var e = col.GetInterface("IEnumerable`1");
			if (e != null && e.IsGenericType) {
				var a = e.GetGenericArguments()[0];
				return a.IsAssignableFrom(item);
			}
			return true;
		}

		/// <summary>
		/// Gets if the collection type <paramref name="col"/> can accept the specified items.
		/// </summary>
		public static bool CanCollectionAdd(Type col, IEnumerable items)
		{
			foreach (var item in items) {
				if (!CanCollectionAdd(col, item.GetType())) return false;
			}
			return true;
		}

		/// <summary>
		/// Adds a value to the end of a collection.
		/// </summary>
		public static void AddToCollection(Type collectionType, object collectionInstance, XamlPropertyValue newElement)
		{
			var genericDictionary = collectionInstance is IDictionary ? null : GetGenericDictionaryInterface(collectionInstance);
			if (collectionInstance is IDictionary || genericDictionary != null) {
				object val = newElement.GetValueFor(null);
				object key = newElement is XamlObject ? ((XamlObject)newElement).GetXamlAttribute("Key") : null;
				if (key == null || (key as string) == "")
				{
					if (val is Style)
						key = ((Style)val).TargetType;
				}
				if (key == null || (key as string) == "")
					key = val;
				if (genericDictionary != null)
					GenericDictionaryAdd(genericDictionary, collectionInstance, key, val);
				else
					((IDictionary)collectionInstance).Add(key, val);
			} else {
				AddValueToCollection(collectionType, collectionInstance, newElement.GetValueFor(null));
			}
		}

		/// <summary>
		/// Finds a collection method such as <c>Add</c>/<c>Insert</c>/<c>Remove</c> on a collection
		/// type, looking through its interfaces when the type itself does not declare it publicly.
		/// </summary>
		/// <remarks>
		/// The reference reaches these methods with <c>Type.InvokeMember</c>/<c>GetMethod</c> on the
		/// collection type, which only sees members the class declares publicly. WinUI's
		/// C#/WinRT-projected collections implement their collection interfaces
		/// <b>explicitly</b>, so the class exposes no public <c>Add</c> at all: an
		/// <c>InvokeMember("Add", ...)</c> against <c>Microsoft.UI.Xaml.Controls.UIElementCollection</c>
		/// throws <c>MissingMethodException: Method 'UIElementCollection.Add' not found</c>
		/// (verified - this is what broke adding any child element to a <c>Panel</c>). Falling back
		/// to the interface declaration fixes it: invoking an interface <see cref="MethodInfo"/>
		/// against an implementing instance dispatches to the explicit implementation.
        /// </remarks>
		static MethodInfo FindCollectionMethod(Type collectionType, object collectionInstance, string name, int parameterCount, object lastArgument = null)
		{
			Type type = collectionInstance != null ? collectionInstance.GetType() : collectionType;

			MethodInfo Match(IEnumerable<MethodInfo> methods)
			{
				var candidates = methods.Where(m => m.Name == name && m.GetParameters().Length == parameterCount).ToList();
				if (candidates.Count == 0)
					return null;
				if (candidates.Count > 1 && lastArgument != null) {
					var typed = candidates.FirstOrDefault(
						m => m.GetParameters()[parameterCount - 1].ParameterType.IsInstanceOfType(lastArgument));
					if (typed != null)
						return typed;
				}
				return candidates[0];
			}

			var found = Match(type.GetMethods(BindingFlags.Public | BindingFlags.Instance));
			if (found != null)
				return found;

			foreach (Type iface in type.GetInterfaces()) {
				found = Match(iface.GetMethods());
				if (found != null)
					return found;
			}
			return null;
		}

		/// <summary>
		/// Adds an already-materialized value to the end of a collection.
		/// </summary>
		internal static void AddValueToCollection(Type collectionType, object collectionInstance, object value)
		{
			MethodInfo add = FindCollectionMethod(collectionType, collectionInstance, "Add", 1, value);
			if (add == null)
				throw new MissingMethodException(collectionType.FullName, "Add");
			add.Invoke(collectionInstance, new[] { value });
		}

		/// <summary>
		/// Empties a collection. Uses reflection because WinUI's collections implement only the
		/// generic collection interfaces (see <see cref="IsCollectionType"/>).
		/// </summary>
		internal static void ClearCollection(Type collectionType, object collectionInstance)
		{
			MethodInfo clear = FindCollectionMethod(collectionType, collectionInstance, "Clear", 0);
			if (clear == null)
				throw new MissingMethodException(collectionType.FullName, "Clear");
			clear.Invoke(collectionInstance, null);
		}

		/// <summary>
		/// Adds a value at the specified index in the collection.
		/// </summary>
		public static bool Insert(Type collectionType, object collectionInstance, XamlPropertyValue newElement, int index)
		{
			object value = newElement.GetValueFor(null);

			// Using IList, with possible Add instead of Insert, was primarily added as a workaround
			// for a peculiarity (or bug) with collections inside System.Windows.Input namespace.
			// See CollectionTests.InputCollectionsPeculiarityOrBug test method for details.
			var list = collectionInstance as IList;
			if (list != null) {
				if (list.Count == index) {
					list.Add(value);
				}
				else {
					list.Insert(index, value);
				}
				return true;
			} else {
				MethodInfo insert = FindCollectionMethod(collectionType, collectionInstance, "Insert", 2, value);
				if (insert != null) {
					insert.Invoke(collectionInstance, new[] { (object)index, value });
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// Adds a value at the specified index in the collection. A return value indicates whether the Insert succeeded.
		/// </summary>
		/// <returns>True if the Insert succeeded, false if the collection type does not support Insert.</returns>
		internal static bool TryInsert(Type collectionType, object collectionInstance, XamlPropertyValue newElement, int index)
		{
			try {
				return Insert(collectionType, collectionInstance, newElement, index);
			} catch (MissingMethodException) {
				return false;
			}
		}

		/// <summary>
		/// Removes the item at the specified index of the collection.
		/// </summary>
		/// <returns>True if the removal succeeded, false if the collection type does not support RemoveAt.</returns>
		public static bool RemoveItemAt(Type collectionType, object collectionInstance, int index)
		{
			MethodInfo m = FindCollectionMethod(collectionType, collectionInstance, "RemoveAt", 1, index);
			if (m != null) {
				m.Invoke(collectionInstance, new object[] { index });
				return true;
			} else {
				return false;
			}
		}

		/// <summary>
		/// Removes an item instance from the specified collection.
		/// </summary>
		public static void RemoveItem(Type collectionType, object collectionInstance, object item)
		{
			MethodInfo remove = FindCollectionMethod(collectionType, collectionInstance, "Remove", 1, item);
			if (remove != null) {
				remove.Invoke(collectionInstance, new[] { item });
				return;
			}
			collectionType.InvokeMember(
				"Remove", BindingFlags.Public | BindingFlags.InvokeMethod | BindingFlags.Instance,
				null, collectionInstance,
				new object[] { item },
				CultureInfo.InvariantCulture);
		}

		/// <summary>
		/// Removes an item instance from the specified collection.
		/// </summary>
		internal static void RemoveItem(Type collectionType, object collectionInstance, object item, XamlPropertyValue element)
		{
			var dictionary = collectionInstance as IDictionary;
			var xamlObject = element as XamlObject;

			if (dictionary != null && xamlObject != null) {
				dictionary.Remove(xamlObject.GetXamlAttribute("Key"));
			} else {
				// WinUI dictionaries only implement IDictionary<,> - see IsCollectionType's remarks.
				var genericDictionary = GetGenericDictionaryInterface(collectionInstance);
				if (genericDictionary != null && xamlObject != null) {
					GenericDictionaryRemove(genericDictionary, collectionInstance, xamlObject.GetXamlAttribute("Key"));
				} else {
					RemoveItem(collectionType, collectionInstance, item);
				}
			}
		}
	}
}
