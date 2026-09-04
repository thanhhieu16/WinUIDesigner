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
using System.Collections.Generic;
using Windows.Foundation;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using WinUIDesigner.DesignModel.Adorners;

namespace WinUIDesigner.DesignModel
{
	public interface ITool
	{
		InputCursor Cursor { get; }
		void Activate(IDesignPanel designPanel);
		void Deactivate(IDesignPanel designPanel);
	}

	public interface IToolService
	{
		ITool PointerTool { get; }
		ITool CurrentTool { get; set; }
		event EventHandler CurrentToolChanged;
	}

	public interface IDesignPanel
	{
		DesignContext Context { get; }
		bool IsContentHitTestVisible { get; set; }
		bool IsAdornerLayerHitTestVisible { get; set; }
		ICollection<AdornerPanel> Adorners { get; }
		DesignPanelHitTestResult HitTest(Point mousePosition, bool testAdorners, bool testDesignSurface, HitTestType hitTestType);
		void HitTest(Point mousePosition, bool testAdorners, bool testDesignSurface, Predicate<DesignPanelHitTestResult> callback, HitTestType hitTestType);

		// Added Phase 4d (spec decision 1): the reference's IDesignPanel : IInputElement gets
		// MouseMove/CaptureMouse()/LostMouseCapture/keyboard input for free from that WPF
		// interface. WinUI's IDesignPanel inherits nothing - every member below is explicit.
		// DesignPanel (the only implementation) satisfies all of these automatically via its own
		// inherited UIElement members (PointerPressed, PointerMoved, etc. are already public on
		// UIElement with matching signatures) - no forwarding code needed in DesignPanel itself,
		// except SetCursor (UIElement.ProtectedCursor is protected, not public).
		event PointerEventHandler PointerPressed;
		event PointerEventHandler PointerMoved;
		event PointerEventHandler PointerReleased;
		event PointerEventHandler PointerCaptureLost;
		event KeyEventHandler KeyDown;
		event DoubleTappedEventHandler DoubleTapped;
		event DragEventHandler DragEnter;
		event DragEventHandler DragOver;
		event DragEventHandler DragLeave;
		event DragEventHandler Drop;
		bool CapturePointer(Pointer pointer);
		void ReleasePointerCaptures();
		bool Focus(FocusState value);

		/// <summary>Sets the panel's visible pointer cursor to match the active ITool.Cursor.
		/// Not on UIElement as a public member (UIElement.ProtectedCursor is protected), so this
		/// needs an explicit DesignPanel implementation - see DesignerThumb.SetDesignCursor for
		/// the established equivalent pattern on a different UIElement subclass.</summary>
		void SetCursor(InputCursor cursor);
	}
}
