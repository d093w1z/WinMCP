namespace WinMcp.Core.Native;

/// <summary>A module (EXE or DLL) loaded in a target process.</summary>
public sealed record LoadedModule(string Name, string Path);

/// <summary>A raw <c>RT_DIALOG</c> resource: a dialog template as the resource compiler wrote it.</summary>
/// <param name="Id">Numeric resource ID (e.g. <c>IDD_MAIN</c>'s value); null for string-named resources.</param>
public sealed record DialogResource(string Module, int? Id, string? Name, byte[] Data);

/// <summary>Read-only facts about target processes and their files, beyond window handles.</summary>
public interface INativeProcesses
{
    /// <returns>Null when the process can't be read (exited, elevated, protected).</returns>
    IReadOnlyList<LoadedModule>? GetModules(int pid);

    /// <returns>The file's version resource (e.g. <c>14.44.35207.1</c>); null when it has none.</returns>
    string? GetFileVersion(string path);

    /// <summary>Dialog templates of a module file, read as data (no code of the module runs).</summary>
    IReadOnlyList<DialogResource> GetDialogResources(string modulePath);

    /// <summary>
    /// Satellite resource DLLs next to an executable in language folders (<c>&lt;exe dir&gt;\1033\*.dll</c>) for the
    /// user's UI language and English — where MFC applications commonly keep their dialogs. Such DLLs are often loaded
    /// as data files, which don't show up in the module list.
    /// </summary>
    IReadOnlyList<string> GetSatelliteModules(string exePath);

    /// <summary>Whether the file contains every marker as a null-terminated ASCII string (e.g. class names compiled into it).</summary>
    bool FileContainsAsciiStrings(string path, IReadOnlyList<string> markers);
}
