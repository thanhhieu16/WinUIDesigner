using System;
using System.Collections.Generic;

namespace WinUIDesigner.DesignModel
{
    /// <summary>The one member of the reference's Metadata.cs (458 lines, a reflection-heavy
    /// design-time-attribute system already out of scope per Phase 3's "no property-grid UI or
    /// metadata") that DefaultPlacementBehavior (Task 3) actually calls. Ported alone, same
    /// precedent as Phase 3's Known-gap-#2 (PlacementOperation.GetRealElementSize).</summary>
    public static class PlacementMetadata
    {
        static readonly HashSet<Type> placementDisabled = new HashSet<Type>();

        public static void RegisterPlacementDisabled(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            lock (placementDisabled) placementDisabled.Add(type);
        }

        public static bool IsPlacementDisabled(Type type)
        {
            lock (placementDisabled) return placementDisabled.Contains(type);
        }
    }
}
