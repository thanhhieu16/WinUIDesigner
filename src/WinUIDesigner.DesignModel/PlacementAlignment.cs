using System;
using Microsoft.UI.Xaml;

namespace WinUIDesigner.DesignModel
{
    /// <summary>A combination of HorizontalAlignment/VerticalAlignment. No WPF-specific API -
    /// mechanical port; both alignment enums have the same members in WinUI.</summary>
    public struct PlacementAlignment : IEquatable<PlacementAlignment>
    {
        public HorizontalAlignment Horizontal { get; }
        public VerticalAlignment Vertical { get; }

        public PlacementAlignment(HorizontalAlignment horizontal, VerticalAlignment vertical)
        {
            if (horizontal == HorizontalAlignment.Stretch) throw new ArgumentException("Stretch is not a valid value", nameof(horizontal));
            if (vertical == VerticalAlignment.Stretch) throw new ArgumentException("Stretch is not a valid value", nameof(vertical));
            Horizontal = horizontal;
            Vertical = vertical;
        }

        public override bool Equals(object obj) => obj is PlacementAlignment other && Equals(other);
        public bool Equals(PlacementAlignment other) => Horizontal == other.Horizontal && Vertical == other.Vertical;
        public override int GetHashCode() { unchecked { return Horizontal.GetHashCode() * 27 + Vertical.GetHashCode(); } }
        public static bool operator ==(PlacementAlignment l, PlacementAlignment r) => l.Equals(r);
        public static bool operator !=(PlacementAlignment l, PlacementAlignment r) => !l.Equals(r);

        public static readonly PlacementAlignment TopLeft = new PlacementAlignment(HorizontalAlignment.Left, VerticalAlignment.Top);
        public static readonly PlacementAlignment Top = new PlacementAlignment(HorizontalAlignment.Center, VerticalAlignment.Top);
        public static readonly PlacementAlignment TopRight = new PlacementAlignment(HorizontalAlignment.Right, VerticalAlignment.Top);
        public static readonly PlacementAlignment Left = new PlacementAlignment(HorizontalAlignment.Left, VerticalAlignment.Center);
        public static readonly PlacementAlignment Center = new PlacementAlignment(HorizontalAlignment.Center, VerticalAlignment.Center);
        public static readonly PlacementAlignment Right = new PlacementAlignment(HorizontalAlignment.Right, VerticalAlignment.Center);
        public static readonly PlacementAlignment BottomLeft = new PlacementAlignment(HorizontalAlignment.Left, VerticalAlignment.Bottom);
        public static readonly PlacementAlignment Bottom = new PlacementAlignment(HorizontalAlignment.Center, VerticalAlignment.Bottom);
        public static readonly PlacementAlignment BottomRight = new PlacementAlignment(HorizontalAlignment.Right, VerticalAlignment.Bottom);
    }
}
