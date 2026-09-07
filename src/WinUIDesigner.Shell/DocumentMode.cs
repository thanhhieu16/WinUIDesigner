namespace WinUIDesigner.Shell
{
    /// <summary>Which view of a document is showing.</summary>
    /// <remarks>
    /// <c>Design</c> is deliberately the zero value, unlike the reference's
    /// <c>{ Xaml, Design }</c>. A <see cref="Document"/>'s constructor already runs
    /// <c>UpdateDesign()</c>, so with Design first a fresh document reports
    /// <see cref="Document.InDesignMode"/> without anyone assigning <see cref="Document.Mode"/>.
    /// The reference has to correct for its own zero value by assigning
    /// <c>Mode = DocumentMode.Design</c> when its view loads, which costs a second full
    /// <c>UpdateDesign()</c> per document opened. Spec Decision 11.
    /// </remarks>
    public enum DocumentMode
    {
        Design,
        Xaml
    }
}
