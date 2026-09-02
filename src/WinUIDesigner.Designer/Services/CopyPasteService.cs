using WinUIDesigner.DesignModel;
using WinUIDesigner.DesignModel.Services;

namespace WinUIDesigner.Designer.Services
{
    /// <summary>Concrete ICopyPasteService. Lives in WinUIDesigner.Designer, not DesignModel
    /// (the interface's home) - it calls ModelTools, and DesignModel must not reference
    /// Designer. Only Delete/CanDelete are implemented for real; Copy/Cut/Paste stay
    /// explicitly unimplemented (Phase 4c's narrow scope - no real clipboard integration this
    /// phase, see the spec's "Decisions already made"). Registered in DesignSurface.LoadDesigner,
    /// same pattern as OptionService.</summary>
    public class CopyPasteService : ICopyPasteService
    {
        public bool CanCopy(DesignContext designContext) => false;
        public void Copy(DesignContext designContext) { }
        public bool CanCut(DesignContext designContext) => false;
        public void Cut(DesignContext designContext) { }
        public bool CanPaste(DesignContext designContext) => false;
        public void Paste(DesignContext designContext) { }

        public bool CanDelete(DesignContext designContext)
        {
            return designContext != null && ModelTools.CanDeleteComponents(designContext.Services.Selection.SelectedItems);
        }

        public void Delete(DesignContext designContext)
        {
            if (designContext != null)
                ModelTools.DeleteComponents(designContext.Services.Selection.SelectedItems);
        }
    }
}
