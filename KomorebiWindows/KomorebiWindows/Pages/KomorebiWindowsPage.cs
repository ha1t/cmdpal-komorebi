using System;
using System.Collections.Generic;
using KomorebiWindows.Commands;
using KomorebiWindows.Models;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace KomorebiWindows;

internal sealed partial class KomorebiWindowsPage : ListPage
{
    public KomorebiWindowsPage()
    {
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        Title = "Komorebi Windows";
        Name = "Switch";
        PlaceholderText = "Search windows by title or app name...";
        ShowDetails = false;
    }

    public override IListItem[] GetItems()
    {
        try
        {
            var state = KomorebiClient.GetState();
            var items = new List<IListItem>();

            var monitors = state.Monitors.Elements;
            for (var monIdx = 0; monIdx < monitors.Count; monIdx++)
            {
                var workspaces = monitors[monIdx].Workspaces.Elements;
                for (var wsIdx = 0; wsIdx < workspaces.Count; wsIdx++)
                {
                    var ws = workspaces[wsIdx];

                    foreach (var container in ws.Containers.Elements)
                    {
                        foreach (var window in container.Windows.Elements)
                        {
                            if (TryBuildItem(window, wsIdx, ws.Name, isFloating: false) is { } item)
                            {
                                items.Add(item);
                            }
                        }
                    }

                    foreach (var window in ws.FloatingWindows.Elements)
                    {
                        if (TryBuildItem(window, wsIdx, ws.Name, isFloating: true) is { } item)
                        {
                            items.Add(item);
                        }
                    }
                }
            }

            return [.. items];
        }
        catch (Exception ex)
        {
            return [
                new ListItem(new NoOpCommand())
                {
                    Title = $"Error: {ex.GetType().Name}",
                    Subtitle = ex.Message,
                },
            ];
        }
    }

    private static ListItem? TryBuildItem(KomorebiWindow w, int wsIdx, string wsName, bool isFloating)
    {
        if (string.IsNullOrWhiteSpace(w.Title))
        {
            return null;
        }

        var subtitle = isFloating
            ? $"{w.Exe} · WS {wsName} · floating"
            : $"{w.Exe} · WS {wsName}";

        var item = new ListItem(new FocusWindowCommand(w.Hwnd, wsIdx))
        {
            Title = w.Title,
            Subtitle = subtitle,
        };

        var exePath = Win32.GetExePathFromHwnd(w.Hwnd);
        if (!string.IsNullOrEmpty(exePath))
        {
            item.Icon = IconHelpers.FromRelativePath(exePath);
        }

        return item;
    }
}
