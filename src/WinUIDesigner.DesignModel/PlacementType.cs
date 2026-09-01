namespace WinUIDesigner.DesignModel
{
    /// <summary>Describes how a placement is done. No WPF-specific API - mechanical port.</summary>
    public sealed class PlacementType
    {
        public static readonly PlacementType MovePoint = Register("MovePoint");
        public static readonly PlacementType Resize = Register("Resize");
        public static readonly PlacementType Move = Register("Move");
        public static readonly PlacementType MoveAndIgnoreOtherContainers = Register("MoveAndIgnoreOtherContainers");
        public static readonly PlacementType AddItem = Register("AddItem");
        public static readonly PlacementType Delete = Register("Delete");
        public static readonly PlacementType PasteItem = Register("PasteItem");
        public static readonly PlacementType DragDropFromOutside = Register("DragDropFromOutside");

        readonly string name;

        PlacementType(string name) { this.name = name; }

        public static PlacementType Register(string name) => new PlacementType(name);

        public override string ToString() => name;
    }
}
