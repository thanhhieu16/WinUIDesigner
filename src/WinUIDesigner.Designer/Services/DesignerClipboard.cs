using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace WinUIDesigner.Designer.Services
{
    /// <summary>
    /// The clipboard CopyPasteService reads and writes. A seam rather than direct calls so that no
    /// test depends on the clipboard of the machine it runs on (clipboard spec Decision 2).
    /// </summary>
    public interface IDesignerClipboard
    {
        /// <summary>Puts <paramref name="xaml"/> on the clipboard, as designer data and as text.</summary>
        void SetXaml(string xaml);

        /// <summary>True when the clipboard holds designer data.</summary>
        bool ContainsXaml { get; }

        /// <summary>The designer data on the clipboard, or null when there is none.</summary>
        Task<string> GetXamlAsync();
    }

    /// <summary>The Windows clipboard, through the Win32 clipboard API.</summary>
    /// <remarks>
    /// Not Windows.ApplicationModel.DataTransfer.Clipboard. That was the first implementation, and
    /// on the walkthrough runner every Clipboard.SetContent failed with CO_E_NOTINITIALIZED
    /// (0x800401F0). The cause turned out to be the UI thread running in the multithreaded
    /// apartment, which Program.cs now prevents. The Win32 API was kept anyway: it is synchronous,
    /// has no apartment or broker requirement, and is proven on the runner.
    /// </remarks>
    public sealed class SystemDesignerClipboard : IDesignerClipboard
    {
        /// <summary>
        /// The registered clipboard format copied controls travel under. WinUI has no XAML format,
        /// where the reference used WPF's TextDataFormat.Xaml.
        /// </summary>
        public const string FormatName = "WinUIDesigner.Xaml";

        public static readonly SystemDesignerClipboard Instance = new SystemDesignerClipboard();

        const uint CF_UNICODETEXT = 13;
        const uint GMEM_MOVEABLE = 0x0002;

        readonly uint format;

        SystemDesignerClipboard()
        {
            format = RegisterClipboardFormat(FormatName);
        }

        public void SetXaml(string xaml)
        {
            if (xaml == null)
                throw new ArgumentNullException(nameof(xaml));
            Open();
            try
            {
                if (!EmptyClipboard())
                    throw new InvalidOperationException("EmptyClipboard failed: " + Marshal.GetLastWin32Error());
                SetString(format, xaml);
                // Also as text, so copied controls can be pasted into the XAML editor or anywhere else.
                SetString(CF_UNICODETEXT, xaml);
            }
            finally
            {
                CloseClipboard();
            }
        }

        public bool ContainsXaml
        {
            get { return format != 0 && IsClipboardFormatAvailable(format); }
        }

        public Task<string> GetXamlAsync()
        {
            if (!ContainsXaml)
                return Task.FromResult<string>(null);
            Open();
            try
            {
                var handle = GetClipboardData(format);
                if (handle == IntPtr.Zero)
                    return Task.FromResult<string>(null);
                var pointer = GlobalLock(handle);
                if (pointer == IntPtr.Zero)
                    return Task.FromResult<string>(null);
                try
                {
                    return Task.FromResult(Marshal.PtrToStringUni(pointer));
                }
                finally
                {
                    GlobalUnlock(handle);
                }
            }
            finally
            {
                CloseClipboard();
            }
        }

        /// <summary>
        /// Opens the clipboard, retrying briefly: another process holding it open for a moment
        /// (a clipboard manager, a remote-desktop client) is ordinary, and makes a single attempt
        /// fail.
        /// </summary>
        static void Open()
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                if (OpenClipboard(IntPtr.Zero))
                    return;
                Thread.Sleep(20);
            }
            throw new InvalidOperationException("The clipboard is in use by another application.");
        }

        static void SetString(uint clipboardFormat, string value)
        {
            int bytes = (value.Length + 1) * 2;
            var memory = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes);
            if (memory == IntPtr.Zero)
                throw new OutOfMemoryException();
            try
            {
                var pointer = GlobalLock(memory);
                if (pointer == IntPtr.Zero)
                    throw new InvalidOperationException("GlobalLock failed: " + Marshal.GetLastWin32Error());
                try
                {
                    Marshal.Copy(value.ToCharArray(), 0, pointer, value.Length);
                    Marshal.WriteInt16(pointer, value.Length * 2, 0);
                }
                finally
                {
                    GlobalUnlock(memory);
                }
                if (SetClipboardData(clipboardFormat, memory) == IntPtr.Zero)
                    throw new InvalidOperationException("SetClipboardData failed: " + Marshal.GetLastWin32Error());
                // The system owns the memory now.
                memory = IntPtr.Zero;
            }
            finally
            {
                if (memory != IntPtr.Zero)
                    GlobalFree(memory);
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool OpenClipboard(IntPtr newOwner);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr SetClipboardData(uint format, IntPtr memory);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr GetClipboardData(uint format);

        [DllImport("user32.dll")]
        static extern bool IsClipboardFormatAvailable(uint format);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern uint RegisterClipboardFormat(string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr GlobalLock(IntPtr memory);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GlobalUnlock(IntPtr memory);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr GlobalFree(IntPtr memory);
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
