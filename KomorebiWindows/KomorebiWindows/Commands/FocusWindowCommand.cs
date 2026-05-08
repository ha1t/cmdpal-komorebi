using System;
using System.Threading;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace KomorebiWindows.Commands;

internal sealed partial class FocusWindowCommand : InvokableCommand
{
    private readonly long _hwnd;
    private readonly int _workspaceIdx;

    public FocusWindowCommand(long hwnd, int workspaceIdx)
    {
        _hwnd = hwnd;
        _workspaceIdx = workspaceIdx;
        Name = "Focus";
        Icon = new IconInfo(""); // SwitchApps
    }

    public override ICommandResult Invoke()
    {
        KomorebiClient.RunCommand($"focus-workspace {_workspaceIdx}");

        // komorebi の cloak 解除を待つ
        Thread.Sleep(50);

        var h = checked((IntPtr)_hwnd);
        Win32.ShowWindow(h, Win32.SW_RESTORE);
        Win32.SetForegroundWindow(h);

        return CommandResult.Hide();
    }
}
