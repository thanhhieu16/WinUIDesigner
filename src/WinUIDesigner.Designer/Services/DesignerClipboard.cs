using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

namespace WinUIDesigner.Designer.Services
{
    /// <summary>
    /// The clipboard CopyPasteService reads and writes. A seam rather than direct calls for two
    /// reasons (clipboard spec Decision 2): WinUI reads the clipboard asynchronously, and blocking
    /// on that on the UI thread can deadlock; and no test may depend on the clipboard of the
    /// machine it runs on.
    /// </summary>
    public interface IDesignerClipboard
    {
        /// <summary>Puts <paramref name="xaml"/> on the clipboard, as designer data and as text.</summary>
        void SetXaml(string xaml);

        /// <summary>True when the clipboard holds designer data.</summary>
        bool ContainsXaml { get; }

        /// <summary>The designer data on the clipboard, or null when there is none.</summary>
        Task<string> GetXamlAsync();

        /// <summary>Raised when anything replaces the clipboard's content.</summary>
        event EventHandler ContentChanged;
    }

    /// <summary>The Windows clipboard, through Windows.ApplicationModel.DataTransfer.</summary>
    public sealed class SystemDesignerClipboard : IDesignerClipboard
    {
        /// <summary>
        /// The data format id copied controls travel under. WinUI's DataPackage has no XAML
        /// format, where the reference used WPF's TextDataFormat.Xaml.
        /// </summary>
        public const string Format = "WinUIDesigner.Xaml";

        public static readonly SystemDesignerClipboard Instance = new SystemDesignerClipboard();

        EventHandler contentChanged;

        SystemDesignerClipboard()
        {
        }

        public void SetXaml(string xaml)
        {
            var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
            package.SetData(Format, xaml);
            // Also as text, so copied controls can be pasted into the XAML editor or anywhere else.
            package.SetText(xaml);
            Clipboard.SetContent(package);
        }

        public bool ContainsXaml
        {
            get
            {
                try
                {
                    return Clipboard.GetContent().Contains(Format);
                }
                catch (Exception)
                {
                    // Another process can hold the clipboard open; reading then fails. Nothing to
                    // paste is the right answer for that moment.
                    return false;
                }
            }
        }

        public async Task<string> GetXamlAsync()
        {
            var content = Clipboard.GetContent();
            if (!content.Contains(Format))
                return null;
            return await content.GetDataAsync(Format) as string;
        }

        /// <summary>Forwards Clipboard.ContentChanged, subscribing to it only once someone listens.</summary>
        public event EventHandler ContentChanged
        {
            add
            {
                if (contentChanged == null)
                    Clipboard.ContentChanged += OnClipboardContentChanged;
                contentChanged += value;
            }
            remove
            {
                contentChanged -= value;
                if (contentChanged == null)
                    Clipboard.ContentChanged -= OnClipboardContentChanged;
            }
        }

        void OnClipboardContentChanged(object sender, object e)
        {
            var handler = contentChanged;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Several copied items travel as one string: each item's XAML, joined by a delimiter, as the
    /// reference's XamlEditOperations does.
    /// </summary>
    internal static class ClipboardPayload
    {
        internal const string Delimiter = "<!--WinUIDesigner.Delimiter-->";

        internal static string Join(IEnumerable<string> xamls)
        {
            return string.Join(Delimiter, xamls);
        }

        internal static IList<string> Split(string payload)
        {
            if (string.IsNullOrEmpty(payload))
                return new string[0];
            return payload.Split(new[] { Delimiter }, StringSplitOptions.None)
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .ToList();
        }
    }
}
