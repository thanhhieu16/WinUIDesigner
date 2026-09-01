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
using WinUIDesigner.DesignModel.Adorners;

namespace WinUIDesigner.DesignModel
{
	public struct DesignPanelHitTestResult : IEquatable<DesignPanelHitTestResult>
	{
		public static readonly DesignPanelHitTestResult NoHit = new DesignPanelHitTestResult();

		readonly UIElement _visualHit;
		AdornerPanel _adornerHit;
		DesignItem _modelHit;

		public UIElement VisualHit { get { return _visualHit; } }
		public AdornerPanel AdornerHit { get { return _adornerHit; } set { _adornerHit = value; } }
		public DesignItem ModelHit { get { return _modelHit; } set { _modelHit = value; } }

		public DesignPanelHitTestResult(UIElement visualHit)
		{
			_visualHit = visualHit;
			_adornerHit = null;
			_modelHit = null;
		}

		public override bool Equals(object obj) { return obj is DesignPanelHitTestResult && Equals((DesignPanelHitTestResult)obj); }
		public bool Equals(DesignPanelHitTestResult other) { return _visualHit == other._visualHit && _adornerHit == other._adornerHit && _modelHit == other._modelHit; }
		public override int GetHashCode() { return (_visualHit != null ? _visualHit.GetHashCode() : 0) ^ (_adornerHit != null ? _adornerHit.GetHashCode() : 0) ^ (_modelHit != null ? _modelHit.GetHashCode() : 0); }
		public static bool operator ==(DesignPanelHitTestResult l, DesignPanelHitTestResult r) { return l.Equals(r); }
		public static bool operator !=(DesignPanelHitTestResult l, DesignPanelHitTestResult r) { return !l.Equals(r); }
	}
}
