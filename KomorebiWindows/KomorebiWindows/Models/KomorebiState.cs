using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace KomorebiWindows.Models;

internal record KomorebiState
{
    [JsonPropertyName("monitors")]
    public Ring<Monitor> Monitors { get; init; } = new();
}

internal record Ring<T>
{
    [JsonPropertyName("elements")]
    public List<T> Elements { get; init; } = new();

    [JsonPropertyName("focused")]
    public int Focused { get; init; }
}

internal record Monitor
{
    public string Name { get; init; } = "";
    public Ring<Workspace> Workspaces { get; init; } = new();
}

internal record Workspace
{
    public string Name { get; init; } = "";
    public Ring<Container> Containers { get; init; } = new();

    [JsonPropertyName("floating_windows")]
    public Ring<KomorebiWindow> FloatingWindows { get; init; } = new();
}

internal record Container
{
    public Ring<KomorebiWindow> Windows { get; init; } = new();
}

internal record KomorebiWindow
{
    public long Hwnd { get; init; }
    public string Title { get; init; } = "";
    public string Exe { get; init; } = "";
    public string Class { get; init; } = "";
}
