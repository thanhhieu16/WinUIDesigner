using System;
using System.Globalization;
using System.Text;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace WinUIDesigner.XamlDom
{
	/// <summary>
	/// Writes a <see cref="PathGeometry"/> as XAML path mini-language, the form
	/// <c>Path.Data</c> takes as an attribute.
	/// </summary>
	/// <remarks>
	/// <para>
	/// WinUI has no <c>XamlWriter</c>, so there is nothing to fall back to: the mini-language is
	/// written by hand here. Only <see cref="PathGeometry"/> is written.
	/// <c>RectangleGeometry</c>, <c>EllipseGeometry</c>, <c>LineGeometry</c> and
	/// <c>GeometryGroup</c> have no mini-language form, and rewriting one into an equivalent path
	/// string would change the type recorded in the user's document - fixing a data-loss bug by
	/// mutating data.
	/// </para>
	/// <para>
	/// One command is emitted per resulting segment, never a run of coordinates. WinUI's parser
	/// expands a run: <c>L20,5 30,10</c> reads back as two <c>LineSegment</c>s rather than one
	/// <c>PolyLineSegment</c>, so the run form would produce text that changes on the next save
	/// even when nothing was edited. Emitting one command per segment makes this writer a fixed
	/// point - measured over a figure carrying all seven segment types.
	/// </para>
	/// </remarks>
	internal static class GeometryMiniLanguageWriter
	{
		/// <summary>
		/// Gets whether <paramref name="geometry"/> has a faithful mini-language form.
		/// </summary>
		public static bool CanWrite(Geometry geometry)
		{
			var path = geometry as PathGeometry;
			if (path == null || path.Transform != null)
				return false;

			foreach (var figure in path.Figures) {
				// IsFilled=false has no mini-language representation, so writing such a figure
				// would silently turn it into a filled one.
				if (!figure.IsFilled)
					return false;

				foreach (var segment in figure.Segments) {
					if (!(segment is LineSegment || segment is PolyLineSegment ||
					      segment is BezierSegment || segment is PolyBezierSegment ||
					      segment is QuadraticBezierSegment || segment is PolyQuadraticBezierSegment ||
					      segment is ArcSegment))
						// Refusing the whole geometry beats dropping one segment: a silently
						// shortened path is exactly the failure mode this phase exists to remove.
						return false;

					// Write's poly-segment loops consume points three (Bezier) or two
					// (QuadraticBezier) at a time and stop as soon as a full group no longer
					// fits, silently dropping any remainder. A point count that isn't an exact,
					// positive multiple of the command's arity - and an empty PolyLineSegment,
					// which contributes nothing and reads back as a different figure - must
					// refuse the whole geometry here rather than let Write drop points.
					var polyLine = segment as PolyLineSegment;
					if (polyLine != null && polyLine.Points.Count == 0)
						return false;

					var polyBezier = segment as PolyBezierSegment;
					if (polyBezier != null &&
					    (polyBezier.Points.Count == 0 || polyBezier.Points.Count % 3 != 0))
						return false;

					var polyQuadratic = segment as PolyQuadraticBezierSegment;
					if (polyQuadratic != null &&
					    (polyQuadratic.Points.Count == 0 || polyQuadratic.Points.Count % 2 != 0))
						return false;
				}
			}

			return true;
		}

		/// <summary>
		/// Writes <paramref name="geometry"/> as mini-language. Only call this when
		/// <see cref="CanWrite"/> returned <c>true</c> for it.
		/// </summary>
		public static string Write(PathGeometry geometry)
		{
			if (geometry == null)
				throw new ArgumentNullException("geometry");

			var text = new StringBuilder();
			text.Append(geometry.FillRule == FillRule.Nonzero ? "F1" : "F0");

			foreach (var figure in geometry.Figures) {
				text.Append(" M").Append(Pt(figure.StartPoint));

				foreach (var segment in figure.Segments) {
					var line = segment as LineSegment;
					if (line != null) {
						text.Append(" L").Append(Pt(line.Point));
						continue;
					}

					var polyLine = segment as PolyLineSegment;
					if (polyLine != null) {
						foreach (var point in polyLine.Points)
							text.Append(" L").Append(Pt(point));
						continue;
					}

					var bezier = segment as BezierSegment;
					if (bezier != null) {
						text.Append(" C").Append(Pt(bezier.Point1))
						    .Append(' ').Append(Pt(bezier.Point2))
						    .Append(' ').Append(Pt(bezier.Point3));
						continue;
					}

					var polyBezier = segment as PolyBezierSegment;
					if (polyBezier != null) {
						for (int i = 0; i + 2 < polyBezier.Points.Count; i += 3)
							text.Append(" C").Append(Pt(polyBezier.Points[i]))
							    .Append(' ').Append(Pt(polyBezier.Points[i + 1]))
							    .Append(' ').Append(Pt(polyBezier.Points[i + 2]));
						continue;
					}

					var quadratic = segment as QuadraticBezierSegment;
					if (quadratic != null) {
						text.Append(" Q").Append(Pt(quadratic.Point1))
						    .Append(' ').Append(Pt(quadratic.Point2));
						continue;
					}

					var polyQuadratic = segment as PolyQuadraticBezierSegment;
					if (polyQuadratic != null) {
						for (int i = 0; i + 1 < polyQuadratic.Points.Count; i += 2)
							text.Append(" Q").Append(Pt(polyQuadratic.Points[i]))
							    .Append(' ').Append(Pt(polyQuadratic.Points[i + 1]));
						continue;
					}

					var arc = segment as ArcSegment;
					if (arc != null) {
						text.Append(" A").Append(Num(arc.Size.Width)).Append(',').Append(Num(arc.Size.Height))
						    .Append(' ').Append(Num(arc.RotationAngle))
						    .Append(' ').Append(arc.IsLargeArc ? "1" : "0")
						    .Append(' ').Append(arc.SweepDirection == SweepDirection.Clockwise ? "1" : "0")
						    .Append(' ').Append(Pt(arc.Point));
						continue;
					}

					throw new NotSupportedException(
						"CanWrite admitted an unsupported segment type: " + segment.GetType().FullName);
				}

				if (figure.IsClosed)
					text.Append(" Z");
			}

			return text.ToString();
		}

		internal static string Num(double value)
		{
			// "R" round-trips the double exactly, and the invariant culture keeps the decimal
			// separator a period under every user locale. Measured: under de-DE this still yields
			// "2.5", while Point.ToString() yields "1,5;2,5".
			return value.ToString("R", CultureInfo.InvariantCulture);
		}

		internal static string Pt(Point point)
		{
			return Num(point.X) + "," + Num(point.Y);
		}
	}
}
