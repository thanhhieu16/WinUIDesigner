namespace WinUIDesigner.Designer.Controls.Thumbs
{
    /// <summary>No WPF-specific API - mechanical port (base class changed to DesignerThumb :
    /// Grid; the reference's DefaultStyleKeyProperty.OverrideMetadata static constructor is
    /// dropped, not translated - no purpose without a Control base or a generic.xaml
    /// style).</summary>
    public class RotateThumb : DesignerThumb
    {
        public RotateThumb()
        {
            ThumbVisible = true;
        }
    }
}
