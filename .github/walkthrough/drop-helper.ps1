# Drags files onto a point on the screen the way a drag from Explorer does, for walkthrough.ps1's
# dropfile step. Run under Windows PowerShell with -STA: OLE drag and drop needs a
# single-threaded apartment.
#
# A small topmost window appears at (FromX, FromY). The left button is pressed over it and an OLE
# DoDragDrop starts with the files as CF_HDROP. A background thread moves the mouse to
# (ToX, ToY) in steps and releases the button there, which drops. DoDragDrop runs its own modal
# loop on this thread, so the mouse has to be driven from another one. Prints the drop effect.

param(
    [Parameter(Mandatory = $true)][int]$FromX,
    [Parameter(Mandatory = $true)][int]$FromY,
    [Parameter(Mandatory = $true)][int]$ToX,
    [Parameter(Mandatory = $true)][int]$ToY,
    [Parameter(Mandatory = $true)][string]$Paths,
    [Parameter(Mandatory = $true)][int]$ScreenWidth,
    [Parameter(Mandatory = $true)][int]$ScreenHeight
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -ReferencedAssemblies System.Windows.Forms, System.Drawing @'
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

public static class Dropper {
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();

    static int width, height;

    static void MoveAbs(int x, int y) {
        uint nx = (uint)Math.Round(x * 65535.0 / (width - 1));
        uint ny = (uint)Math.Round(y * 65535.0 / (height - 1));
        mouse_event(0x8001, nx, ny, 0, UIntPtr.Zero);
    }

    public static string Run(int fromX, int fromY, int toX, int toY, string[] paths, int screenWidth, int screenHeight) {
        width = screenWidth; height = screenHeight;
        string result = "not started";
        var form = new Form {
            StartPosition = FormStartPosition.Manual,
            FormBorderStyle = FormBorderStyle.None,
            Location = new System.Drawing.Point(fromX - 40, fromY - 40),
            Size = new System.Drawing.Size(80, 80),
            TopMost = true,
            ShowInTaskbar = false,
            BackColor = System.Drawing.Color.Orange
        };
        var data = new DataObject(DataFormats.FileDrop, paths);
        var seen = new System.Collections.Generic.List<string>();
        form.GiveFeedback += (gs, ge) => {
            var p = Cursor.Position;
            string entry = ge.Effect + "@" + (p.X / 50 * 50) + "," + (p.Y / 50 * 50);
            if (seen.Count == 0 || seen[seen.Count - 1] != entry) seen.Add(entry);
        };
        string ended = "";
        form.QueryContinueDrag += (qs, qe) => {
            if (qe.EscapePressed || (qe.KeyState & 1) == 0) {
                var p = Cursor.Position;
                ended = " ended: keyState=" + qe.KeyState + " escape=" + qe.EscapePressed + " at " + p.X + "," + p.Y;
            }
        };
        // The drag starts from the window's own MouseDown, as any drag source does: by then this
        // thread has processed the press, so OLE sees the button down. Called straight after
        // injecting the press, DoDragDrop read the button as up and dropped on the spot.
        form.MouseDown += (s, e) => {
            try {
                result = form.DoDragDrop(data, DragDropEffects.Copy | DragDropEffects.Link).ToString();
            } catch (Exception ex) {
                result = "failed: " + ex.Message;
            }
            result += " feedback: " + string.Join(" ", seen) + ended;
            form.BeginInvoke((Action)form.Close);
        };
        var mover = new Thread(() => {
            Thread.Sleep(500);
            MoveAbs(fromX, fromY);
            Thread.Sleep(200);
            mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(800);
            for (int i = 1; i <= 40; i++) {
                MoveAbs(fromX + (toX - fromX) * i / 40, fromY + (toY - fromY) * i / 40);
                Thread.Sleep(50);
            }
            Thread.Sleep(800);
            mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(3000);
            // Nothing started a drag: close the window so the step does not hang.
            try { form.BeginInvoke((Action)form.Close); } catch (Exception) { }
        });
        mover.IsBackground = true;
        form.Shown += (s, e) => mover.Start();
        Application.Run(form);
        return result;
    }
}
'@
[Dropper]::SetProcessDPIAware() | Out-Null
$effect = [Dropper]::Run($FromX, $FromY, $ToX, $ToY, ($Paths -split '\|'), $ScreenWidth, $ScreenHeight)
Write-Output "effect: $effect"
