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
using System.Linq;
using System.Reflection;
using Microsoft.UI.Xaml;
using Windows.Foundation;

namespace WinUIDesigner.DesignModel
{
	/// <summary>
	/// Ported from the reference's Metadata.cs (WpfDesign/Project/Metadata.cs, 458 lines) - the
	/// "live" subset only (its Category/IsAdvanced cluster is commented out and dead in the
	/// reference itself, so it is not ported here either). GetFullName is not re-ported - it
	/// already exists on DependencyProperty via DesignItemProperty.cs's
	/// DependencyPropertyExtensions, and is confirmed broken there (WinUI's DependencyProperty
	/// doesn't override ToString(), so it returns the same literal string for every property -
	/// see the Phase 4e spec's "Verified against the current codebase"). HideProperty/
	/// AddPopularProperty's DependencyProperty-taking overloads therefore key by the
	/// DependencyProperty object's own identity in a separate store from their Type,string
	/// overloads (spec Decision 3a) instead of unifying both into one GetFullName()-keyed string
	/// space the way the reference does. AddDefaultPropertyValue/GetDefaultPropertyValues key by
	/// a plain property-name string instead of DependencyProperty, since DesignItemProperty/
	/// DesignItemPropertyCollection are string-keyed only (spec Decision 5a - the same deviation
	/// ModelTools.ApplyTransform already made for the same reason). DisablePlacement/
	/// IsPlacementDisabled are merged in from PlacementMetadata.cs (deleted by Task 2), using the
	/// reference's own names rather than that file's RegisterPlacementDisabled.
	/// </summary>
	public static class Metadata
	{
		class NamedValue
		{
			public string Name { get; set; }
			public object Value { get; set; }
		}

		static readonly Dictionary<Type, List<object>> standardValues = new Dictionary<Type, List<object>>();
		static readonly Dictionary<DependencyProperty, object[]> standardValuesForDependencyPropertys = new Dictionary<DependencyProperty, object[]>();
		static readonly Dictionary<Type, List<NamedValue>> standardNamedValues = new Dictionary<Type, List<NamedValue>>();

		public static void AddStandardValues(Type type, Type valuesContainer)
		{
			AddStandardValues(type,
				valuesContainer.GetProperties(BindingFlags.Public | BindingFlags.Static)
					.Select(p => p.GetValue(null, null)));
		}

		public static void AddStandardValues(DependencyProperty dependencyProperty, params object[] values)
		{
			lock (standardValuesForDependencyPropertys) {
				standardValuesForDependencyPropertys[dependencyProperty] = values;
			}
		}

		public static void AddDoubleNamedStandardValues(Type type, Type valuesContainer)
		{
			var pList = valuesContainer.GetProperties(BindingFlags.Public | BindingFlags.Static)
				.Select(p => new NamedValue { Name = p.Name, Value = p.GetValue(null, null) });

			lock (standardNamedValues) {
				List<NamedValue> list;
				if (!standardNamedValues.TryGetValue(type, out list)) {
					list = new List<NamedValue>();
					standardNamedValues[type] = list;
				}
				foreach (var v in pList) {
					list.Add(v);
				}
			}
		}

		public static void AddStandardValues<T>(Type type, IEnumerable<T> values)
		{
			lock (standardValues) {
				List<object> list;
				if (!standardValues.TryGetValue(type, out list)) {
					list = new List<object>();
					standardValues[type] = list;
				}
				foreach (var v in values) {
					list.Add(v);
				}
			}
		}

		public static IEnumerable GetNamedStandardValues(Type type)
		{
			lock (standardNamedValues) {
				List<NamedValue> values;
				if (standardNamedValues.TryGetValue(type, out values)) {
					return values;
				}
			}
			return null;
		}

		public static IEnumerable GetStandardValues(Type type)
		{
			var baseT = Nullable.GetUnderlyingType(type);

			if (type.IsEnum) {
				return Enum.GetValues(type);
			}

			if (baseT != null && baseT.IsEnum) {
				return Enum.GetValues(baseT);
			}

			lock (standardValues) {
				List<object> values;
				if (standardValues.TryGetValue(type, out values)) {
					return values;
				}
			}
			return null;
		}

		public static IEnumerable GetStandardValues(DependencyProperty dependencyProperty)
		{
			lock (standardValuesForDependencyPropertys) {
				object[] values;
				if (standardValuesForDependencyPropertys.TryGetValue(dependencyProperty, out values)) {
					return values;
				}
			}
			return null;
		}

		static readonly HashSet<DependencyProperty> hiddenDependencyProperties = new HashSet<DependencyProperty>();
		static readonly HashSet<string> hiddenProperties = new HashSet<string>();

		public static void HideProperty(DependencyProperty p)
		{
			lock (hiddenDependencyProperties) hiddenDependencyProperties.Add(p);
		}

		public static void HideProperty(Type type, string member)
		{
			lock (hiddenProperties) hiddenProperties.Add(type.FullName + "." + member);
		}

		public static bool IsBrowsable(DesignItemProperty p)
		{
			if (p.DependencyProperty != null) {
				lock (hiddenDependencyProperties) {
					if (hiddenDependencyProperties.Contains(p.DependencyProperty)) return false;
				}
			}
			lock (hiddenProperties) {
				if (hiddenProperties.Contains(p.FullName)) return false;
			}
			return true;
		}

		static readonly HashSet<DependencyProperty> popularDependencyProperties = new HashSet<DependencyProperty>();
		static readonly HashSet<string> popularProperties = new HashSet<string>();

		public static void AddPopularProperty(DependencyProperty p)
		{
			lock (popularDependencyProperties) popularDependencyProperties.Add(p);
		}

		public static void AddPopularProperty(Type type, string member)
		{
			lock (popularProperties) popularProperties.Add(type.FullName + "." + member);
		}

		public static bool IsPopularProperty(DesignItemProperty p)
		{
			if (p.DependencyProperty != null) {
				lock (popularDependencyProperties) {
					if (popularDependencyProperties.Contains(p.DependencyProperty)) return true;
				}
			}
			lock (popularProperties) {
				if (popularProperties.Contains(p.FullName)) return true;
			}
			return false;
		}

		static readonly HashSet<Type> popularControls = new HashSet<Type>();

		public static void AddPopularControl(Type t)
		{
			lock (popularControls) popularControls.Add(t);
		}

		public static IEnumerable<Type> GetPopularControls()
		{
			lock (popularControls) return popularControls.ToArray();
		}

		public static bool IsPopularControl(Type t)
		{
			lock (popularControls) return popularControls.Contains(t);
		}

		static readonly Dictionary<DependencyProperty, NumberRange> ranges = new Dictionary<DependencyProperty, NumberRange>();

		public static void AddValueRange(DependencyProperty p, double min, double max)
		{
			lock (ranges) ranges[p] = new NumberRange { Min = min, Max = max };
		}

		public static NumberRange GetValueRange(DesignItemProperty p)
		{
			if (p.DependencyProperty == null) return null;
			lock (ranges) {
				NumberRange r;
				if (ranges.TryGetValue(p.DependencyProperty, out r)) return r;
			}
			return null;
		}

		static readonly HashSet<Type> placementDisabled = new HashSet<Type>();

		public static void DisablePlacement(Type type)
		{
			lock (placementDisabled) placementDisabled.Add(type);
		}

		public static bool IsPlacementDisabled(Type type)
		{
			lock (placementDisabled) return placementDisabled.Contains(type);
		}

		static readonly Dictionary<Type, Size> defaultSizes = new Dictionary<Type, Size>();

		public static void AddDefaultSize(Type t, Size s)
		{
			lock (defaultSizes) defaultSizes[t] = s;
		}

		public static Size? GetDefaultSize(Type t, bool checkBasetype = true)
		{
			lock (defaultSizes) {
				while (t != null) {
					Size s;
					if (defaultSizes.TryGetValue(t, out s)) return s;
					t = checkBasetype ? t.BaseType : null;
				}
			}
			return null;
		}

		static readonly Dictionary<Type, Dictionary<string, object>> standardPropertyValues = new Dictionary<Type, Dictionary<string, object>>();

		public static void AddDefaultPropertyValue(Type t, string propertyName, object value)
		{
			lock (standardPropertyValues) {
				Dictionary<string, object> forType;
				if (!standardPropertyValues.TryGetValue(t, out forType)) {
					forType = new Dictionary<string, object>();
					standardPropertyValues[t] = forType;
				}
				forType[propertyName] = value;
			}
		}

		public static Dictionary<string, object> GetDefaultPropertyValues(Type t)
		{
			lock (standardPropertyValues) {
				Dictionary<string, object> forType;
				if (standardPropertyValues.TryGetValue(t, out forType)) return forType;
				return null;
			}
		}
	}

	/// <summary>
	/// Represents the minimum and maximum valid value for a double property.
	/// </summary>
	public class NumberRange
	{
		public double Min { get; set; }
		public double Max { get; set; }
	}
}
