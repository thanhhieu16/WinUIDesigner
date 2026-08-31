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
using NUnit.Framework;
using WinUIDesigner.XamlDom;

namespace WinUIDesigner.XamlDom.Tests
{
	/// <remarks>
	/// <para>
	/// Two systematic substitutions against the reference file, both called for by the brief:
	/// </para>
	/// <para>
	/// 1. The default xmlns is <see cref="XamlConstants.PresentationNamespace"/> (the value Task 2
	/// confirmed against the real WinUI XAML compiler) rather than the reference's older
	/// <c>.../netfx/2007/xaml/presentation</c> URI.
	/// </para>
	/// <para>
	/// 2. <c>Window</c> is replaced by <c>Grid</c> as the root element. WinUI's
	/// <c>Microsoft.UI.Xaml.Window</c> does not derive from <c>FrameworkElement</c> (its base is
	/// <c>DependencyObject</c>), has no <c>Width</c>/<c>Height</c>/<c>Resources</c> properties to
	/// exercise, and is not loadable as a bare XAML root. The point of these tests is the parser,
	/// not <c>Window</c> specifically, so <c>Grid</c> stands in - exactly the substitution the
	/// brief authorises.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class SimpleLoadTests : TestHelper
	{
		const string P = XamlConstants.PresentationNamespace;
		const string X = XamlConstants.XamlNamespace;
		const string T = XamlTypeFinderTests.XamlDomTestsNamespace;

		[Test]
		public void EmptyGrid()
		{
			TestLoading(@"
<Grid
  xmlns=""" + P + @"""
  xmlns:x=""" + X + @""">
</Grid>
			");
		}

		[Test]
		public void GridWithAttributes()
		{
			TestLoading(@"
<Grid
  xmlns=""" + P + @"""
  xmlns:x=""" + X + @"""
  Width=""300"" Height=""400"">
</Grid>
			");
		}

		[Test]
		public void GridWithElementAttribute()
		{
			TestLoading(@"
<Grid
  xmlns=""" + P + @"""
  xmlns:x=""" + X + @""">
  <Grid.Height>100</Grid.Height>
</Grid>
			");
		}

		/// <remarks>
		/// Not in the reference file. Added because nothing else here puts a UIElement inside a
		/// Panel, and that path was genuinely broken: WinUI's <c>UIElementCollection</c> implements
		/// its collection interfaces explicitly, so the reference's
		/// <c>InvokeMember("Add", ...)</c> against the collection type threw
		/// <c>MissingMethodException: Method 'UIElementCollection.Add' not found</c> - see
		/// <c>CollectionSupport.FindCollectionMethod</c>.
		/// </remarks>
		[Test]
		public void GridWithChildElements()
		{
			TestLoading(@"
<Grid
  xmlns=""" + P + @"""
  xmlns:x=""" + X + @""">
  <Button Content=""one"" />
  <TextBlock Text=""two"" />
</Grid>
			");
		}

		[Test]
		public void GridWithNestedPanelAndAttachedProperty()
		{
			TestLoading(@"
<Grid
  xmlns=""" + P + @"""
  xmlns:x=""" + X + @""">
  <StackPanel Grid.Row=""1"">
    <Button Content=""inner"" />
  </StackPanel>
</Grid>
			");
		}

		[Test]
		public void ExampleClassTest()
		{
			TestLoading(@"
<t:ExampleClass
  xmlns=""" + P + @"""
  xmlns:t=""" + T + @"""
  xmlns:x=""" + X + @""">
</t:ExampleClass>
			");
		}

		[Test]
		public void ExampleClassWithStringPropAttribute()
		{
			TestLoading(@"
<t:ExampleClass
  xmlns=""" + P + @"""
  xmlns:t=""" + T + @"""
  xmlns:x=""" + X + @"""
  StringProp=""a test string"">
</t:ExampleClass>
			");
		}

		[Test]
		public void ExampleClassWithEscapedBraceStringPropAttribute()
		{
			TestLoading(@"
<t:ExampleClass
  xmlns=""" + P + @"""
  xmlns:t=""" + T + @"""
  xmlns:x=""" + X + @"""
  StringProp=""{}{a test string}"">
</t:ExampleClass>
			");
		}

		[Test]
		public void ExampleClassWithFilePathStringPropAttribute()
		{
			TestLoading(@"
<t:ExampleClass
  xmlns=""" + P + @"""
  xmlns:t=""" + T + @"""
  xmlns:x=""" + X + @"""
  StringProp=""C:\Folder A\Sub,Folder,A\SubFolderB\file,with,commas and spaces.txt"">
</t:ExampleClass>
			");
		}

		[Test]
		public void ExampleClassUseDefaultProperty()
		{
			TestLoading(@"
<t:ExampleClass
  xmlns=""" + P + @"""
  xmlns:t=""" + T + @"""
  xmlns:x=""" + X + @""">
   a test string
</t:ExampleClass>
			");
		}

		[Test]
		public void ExampleClassUseDefaultPropertyExplicitly()
		{
			TestLoading(@"
<t:ExampleClass
  xmlns=""" + P + @"""
  xmlns:t=""" + T + @"""
  xmlns:x=""" + X + @""">
   <t:ExampleClass.StringProp>
      a test string
   </t:ExampleClass.StringProp>
</t:ExampleClass>
			");
		}

		[Test]
		public void ExampleClassUseDefaultPropertyBeforeOtherPropertyElement()
		{
			TestLoading(@"
<t:ExampleClass
  xmlns=""" + P + @"""
  xmlns:t=""" + T + @"""
  xmlns:x=""" + X + @""">
   a test string
   <t:ExampleClass.OtherProp>
      otherValue
   </t:ExampleClass.OtherProp>
</t:ExampleClass>
			");
		}

		[Test]
		public void ExampleClassUseDefaultPropertyAfterOtherPropertyElement()
		{
			TestLoading(@"
<t:ExampleClass
  xmlns=""" + P + @"""
  xmlns:t=""" + T + @"""
  xmlns:x=""" + X + @""">
   <t:ExampleClass.OtherProp>
      otherValue
   </t:ExampleClass.OtherProp>
   a test string
</t:ExampleClass>
			");
		}

		[Test]
		public void ExampleClassUseDefaultPropertyBetweenOtherPropertyElement()
		{
			TestLoading(@"
<t:ExampleClass
  xmlns=""" + P + @"""
  xmlns:t=""" + T + @"""
  xmlns:x=""" + X + @""">
   <t:ExampleClass.OtherProp>
      otherValue
   </t:ExampleClass.OtherProp>
   a test string
   <t:ExampleClass.OtherProp2>
      otherValue2
   </t:ExampleClass.OtherProp2>
</t:ExampleClass>
			");
		}

		[Test]
		public void Container()
		{
			TestLoading(@"
<ExampleClassContainer
  xmlns=""" + T + @"""
  xmlns:x=""" + X + @""">
   <ExampleClassContainer.List>
      <ExampleClass OtherProp=""a""> </ExampleClass>
      <ExampleClass OtherProp=""b"" />
      <ExampleClass OtherProp=""c"" />
   </ExampleClassContainer.List>
</ExampleClassContainer>
			");
		}

		[Test]
		public void ContainerImplicitList()
		{
			TestLoading(@"
<ExampleClassContainer
  xmlns=""" + T + @"""
  xmlns:x=""" + X + @""">
      <ExampleClass OtherProp=""a"" />
      <ExampleClass OtherProp=""b"" />
</ExampleClassContainer>
			");
		}

		[Test]
		public void ContainerExplicitList()
		{
			TestLoading(@"
<ExampleClassContainer
  xmlns=""" + T + @"""
  xmlns:x=""" + X + @""">
   <ExampleClassContainer.OtherList>
      <ExampleClassList>
         <ExampleClass OtherProp=""a""> </ExampleClass>
         <ExampleClass OtherProp=""b"" />
         <ExampleClass OtherProp=""c"" />
      </ExampleClassList>
   </ExampleClassContainer.OtherList>
</ExampleClassContainer>
			");
		}

		[Test]
		public void ContainerImplicitDictionary()
		{
			TestLoading(@"
<ExampleClassContainer
  xmlns=""" + T + @"""
  xmlns:x=""" + X + @""">
   <ExampleClassContainer.Dictionary>
     <ExampleClass x:Key=""key1"" OtherProp=""a""> </ExampleClass>
     <ExampleClass x:Key=""key2"" OtherProp=""b"" />
     <ExampleClass x:Key=""key3"" OtherProp=""c"" />
   </ExampleClassContainer.Dictionary>
</ExampleClassContainer>
			");
		}

		[Test]
		public void ContainerExplicitDictionary()
		{
			TestLoading(@"
<ExampleClassContainer
  xmlns=""" + T + @"""
  xmlns:x=""" + X + @""">
   <ExampleClassContainer.Dictionary>
      <ExampleClassDictionary>
         <ExampleClass x:Key=""key1"" OtherProp=""a""> </ExampleClass>
         <ExampleClass x:Key=""key2"" OtherProp=""b"" />
         <ExampleClass x:Key=""key3"" OtherProp=""c"" />
      </ExampleClassDictionary>
   </ExampleClassContainer.Dictionary>
</ExampleClassContainer>
			");
		}

		[Test]
		public void ResourceDictionaryImplicit()
		{
			TestLoading(@"
<Grid
  xmlns=""" + P + @"""
  xmlns:t=""" + T + @"""
  xmlns:x=""" + X + @""">
  <Grid.Resources>
     <t:ExampleClass x:Key=""key1"" OtherProp=""a""> </t:ExampleClass>
     <t:ExampleClass x:Key=""key2"" OtherProp=""b"" />
  </Grid.Resources>
</Grid>
			");
		}

		[Test]
		public void ResourceDictionaryExplicit()
		{
			TestLoading(@"
<Grid
  xmlns=""" + P + @"""
  xmlns:t=""" + T + @"""
  xmlns:x=""" + X + @""">
  <Grid.Resources>
     <ResourceDictionary>
        <t:ExampleClass x:Key=""key1"" OtherProp=""a""> </t:ExampleClass>
        <t:ExampleClass x:Key=""key2"" OtherProp=""b"" />
     </ResourceDictionary>
  </Grid.Resources>
</Grid>
			");
		}

		[Test]
		public void ExampleServiceTest()
		{
			TestLoading(@"
<t:ExampleDependencyObject
  xmlns=""" + P + @"""
  xmlns:t=""" + T + @"""
  xmlns:x=""" + X + @"""
  t:ExampleService.Example=""attached value"">
</t:ExampleDependencyObject>
			");
		}

		[Test]
		public void ExampleServiceCollectionTest()
		{
			TestLoading(@"
<t:ExampleDependencyObject
  xmlns=""" + P + @"""
  xmlns:t=""" + T + @"""
  xmlns:x=""" + X + @""">
   <t:ExampleService.ExampleCollection>
      <t:ExampleClassList>
         <t:ExampleClass OtherProp=""a""> </t:ExampleClass>
         <t:ExampleClass OtherProp=""b"" />
         <t:ExampleClass OtherProp=""c"" />
      </t:ExampleClassList>
   </t:ExampleService.ExampleCollection>
</t:ExampleDependencyObject>
			");
		}

		[Test]
		public void ExampleClassObjectPropWithStringValue()
		{
			TestLoading(@"
<t:ExampleClass
  xmlns=""" + P + @"""
  xmlns:t=""" + T + @"""
  xmlns:x=""" + X + @"""
  ObjectProp=""a test string"">
</t:ExampleClass>
			");
		}

		// The reference's ExampleClassObjectPropWithTypeValue / ...WithTypeValue2 /
		// ...WithExplicitMarkupExtension / ...WithExplicitMarkupExtension2 tests all exercise
		// {x:Type ...} / <x:Type> / <x:TypeExtension>. WinUI has no TypeExtension runtime type at
		// all (the entire assembly contains exactly two public "*Extension"-named types,
		// IDataTemplateExtension and MarkupExtension), so neither WinUI's own XamlReader nor this
		// parser can resolve it; x:Type is a compiler intrinsic there. Recorded rather than
		// silently dropped - if a later phase adds a TypeExtensionPlaceholder alongside the
		// StaticResource/ThemeResource/Null/Reference ones, un-ignore these.
		[Test]
		[Ignore("x:Type has no WinUI runtime type (no TypeExtension in Microsoft.WinUI.dll); out of Phase 1 scope.")]
		public void ExampleClassObjectPropWithTypeValue()
		{
			TestLoading(@"
<t:ExampleClass
  xmlns=""" + P + @"""
  xmlns:t=""" + T + @"""
  xmlns:x=""" + X + @"""
  ObjectProp=""{x:Type t:ExampleClass}"">
</t:ExampleClass>
			");
		}

		[Test]
		public void ExampleClassObjectPropWithNullValue()
		{
			TestLoading(@"
<t:ExampleClass
  xmlns=""" + P + @"""
  xmlns:t=""" + T + @"""
  xmlns:x=""" + X + @"""
  ObjectProp=""{x:Null}"">
</t:ExampleClass>
			");
		}

		[Test]
		public void UsingAttachedPropertyOnDerivedClass()
		{
			TestLoading(@"
<Grid
  xmlns=""" + P + @"""
  xmlns:t=""" + T + @"""
  xmlns:x=""" + X + @"""
  t:DerivedExampleDependencyObject.Example=""test"">
</Grid>
			");
		}
	}
}
