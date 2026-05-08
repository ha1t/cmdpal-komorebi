using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using KomorebiWindows.Models;

namespace KomorebiWindows;

internal static class KomorebiClient
{
    private static string? _resolvedExePath;

    public static KomorebiState GetState()
    {
        // komorebic は UTF-8 で出力するので、システム既定 (Shift-JIS 等) ではなく UTF-8 で読む
        var psi = new ProcessStartInfo(ResolveExePath(), "state")
        {
            RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi)!;
        var json = p.StandardOutput.ReadToEnd();
        p.WaitForExit(2000);
        return JsonSerializer.Deserialize(json, KomorebiJsonContext.Default.KomorebiState) ?? new KomorebiState();
    }

    public static void RunCommand(string args)
    {
        var psi = new ProcessStartInfo(ResolveExePath(), args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi)!;
        p.WaitForExit(2000);
    }

    // MSIX/AppContainer 内では PATH が空になることがあるため、
    // 既知のインストール場所を順に試して komorebic.exe を解決する。
    private static string ResolveExePath()
    {
        if (_resolvedExePath is not null)
        {
            return _resolvedExePath;
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new List<string>
        {
            @"C:\Program Files\komorebi\bin\komorebic.exe",
            Path.Combine(userProfile, ".cargo", "bin", "komorebic.exe"),
            "komorebic.exe", // PATH に通っていれば最後にこれで試す
        };

        foreach (var c in candidates)
        {
            if (c == "komorebic.exe" || File.Exists(c))
            {
                _resolvedExePath = c;
                return c;
            }
        }

        throw new FileNotFoundException(
            "komorebic.exe was not found. Tried: " + string.Join(", ", candidates));
    }
}
