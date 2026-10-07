using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using WinMcp.Core.Native;

namespace WinMcp.Windows;

/// <summary>
/// Module lists via PSAPI and dialog templates via the resource loader. Module files are mapped with
/// <c>LOAD_LIBRARY_AS_DATAFILE | LOAD_LIBRARY_AS_IMAGE_RESOURCE</c>: no code runs, no DllMain, no imports resolved.
/// </summary>
public sealed unsafe partial class Win32NativeProcesses : INativeProcesses
{
    private const uint ProcessQueryInformation = 0x0400, ProcessVmRead = 0x0010;
    private const uint ListModulesAll = 0x03;
    private const uint LoadLibraryAsDatafile = 0x02, LoadLibraryAsImageResource = 0x20;
    private const uint ResourceEnumLn = 0x01, ResourceEnumMui = 0x02;
    private const nint RtDialog = 5;
    private const int MaxDialogsPerModule = 2000;
    private const int MaxSatelliteModules = 8;

    public IReadOnlyList<LoadedModule>? GetModules(int pid)
    {
        var process = OpenProcess(ProcessQueryInformation | ProcessVmRead, false, (uint)pid);
        if (process == 0)
            return null; // elevated, protected or exited
        try
        {
            var handles = new nint[256];
            while (true)
            {
                if (!EnumProcessModulesEx(process, handles, (uint)(handles.Length * nint.Size), out var needed, ListModulesAll))
                    return null;
                var count = (int)(needed / (uint)nint.Size);
                if (count > handles.Length)
                {
                    handles = new nint[count];
                    continue;
                }

                var modules = new List<LoadedModule>(count);
                var buffer = new char[1024];
                for (var i = 0; i < count; i++)
                {
                    int length;
                    fixed (char* p = buffer)
                        length = (int)GetModuleFileNameExW(process, handles[i], p, (uint)buffer.Length);
                    if (length > 0)
                    {
                        var path = new string(buffer, 0, length);
                        modules.Add(new LoadedModule(Path.GetFileName(path), path));
                    }
                }
                return modules;
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }

    public string? GetFileVersion(string path)
    {
        try
        {
            return FileVersionInfo.GetVersionInfo(path).FileVersion is { Length: > 0 } version ? version : null;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    public IReadOnlyList<DialogResource> GetDialogResources(string modulePath)
    {
        var module = LoadLibraryExW(modulePath, 0, LoadLibraryAsDatafile | LoadLibraryAsImageResource);
        if (module == 0)
            return [];
        var state = new EnumState(module, modulePath);
        var handle = GCHandle.Alloc(state);
        try
        {
            // LN + MUI: system executables keep their dialogs in language files (charmap.exe → en-US\charmap.exe.mui).
            EnumResourceNamesExW(module, RtDialog, &OnResource, GCHandle.ToIntPtr(handle), ResourceEnumLn | ResourceEnumMui, 0);
            return state.Dialogs;
        }
        finally
        {
            handle.Free();
            FreeLibrary(module);
        }
    }

    public IReadOnlyList<string> GetSatelliteModules(string exePath)
    {
        var folder = Path.GetDirectoryName(exePath);
        if (folder is null)
            return [];
        var languages = new[] { CultureInfo.CurrentUICulture.LCID, 1033 }.Distinct();
        try
        {
            return languages
                .Select(lcid => Path.Combine(folder, lcid.ToString(CultureInfo.InvariantCulture)))
                .Where(Directory.Exists)
                .SelectMany(dir => Directory.EnumerateFiles(dir, "*.dll"))
                .Take(MaxSatelliteModules)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public bool FileContainsAsciiStrings(string path, IReadOnlyList<string> markers)
    {
        const int Chunk = 4 << 20;
        const long MaxBytes = 256L << 20; // executables beyond this are skipped rather than read
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
            if (file.Length > MaxBytes)
                return false;
            var patterns = markers.Select(m => System.Text.Encoding.ASCII.GetBytes(m + "\0")).ToList();
            var found = new bool[patterns.Count];
            var overlap = patterns.Max(p => p.Length) - 1;
            var buffer = new byte[Chunk + overlap];
            var carried = 0;
            int read;
            while ((read = file.Read(buffer, carried, Chunk)) > 0)
            {
                var window = buffer.AsSpan(0, carried + read);
                for (var i = 0; i < patterns.Count; i++)
                    found[i] |= window.IndexOf(patterns[i]) >= 0;
                if (found.All(f => f))
                    return true;
                carried = Math.Min(overlap, window.Length);
                window[^carried..].CopyTo(buffer);
            }
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed class EnumState(nint module, string path)
    {
        public nint Module { get; } = module;
        public string Path { get; } = path;
        public List<DialogResource> Dialogs { get; } = [];
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int OnResource(nint module, nint type, nint name, nint parameter)
    {
        var state = (EnumState)GCHandle.FromIntPtr(parameter).Target!;
        if (state.Dialogs.Count >= MaxDialogsPerModule)
            return 0;
        try
        {
            var resource = FindResourceW(state.Module, name, type);
            var size = resource == 0 ? 0 : SizeofResource(state.Module, resource);
            var loaded = size == 0 ? 0 : LoadResource(state.Module, resource);
            var data = loaded == 0 ? 0 : LockResource(loaded);
            if (data != 0)
            {
                var bytes = new byte[size];
                Marshal.Copy(data, bytes, 0, (int)size);
                // IS_INTRESOURCE: numeric IDs are passed as pointers with a zero high word.
                var isId = (name >> 16) == 0;
                state.Dialogs.Add(new DialogResource(state.Path, isId ? (int)name : null, isId ? null : Marshal.PtrToStringUni(name), bytes));
            }
        }
        catch (Exception)
        {
            // Never let an exception cross the native callback; skip the resource.
        }
        return 1;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [LibraryImport("psapi.dll", EntryPoint = "EnumProcessModulesEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumProcessModulesEx(nint process, [Out] nint[] modules, uint size, out uint needed, uint filter);

    [LibraryImport("psapi.dll", EntryPoint = "GetModuleFileNameExW")]
    private static partial uint GetModuleFileNameExW(nint process, nint module, char* fileName, uint size);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint LoadLibraryExW(string fileName, nint reserved, uint flags);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FreeLibrary(nint module);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumResourceNamesExW(
        nint module, nint type, delegate* unmanaged[Stdcall]<nint, nint, nint, nint, int> callback, nint parameter, uint flags, ushort language);

    [LibraryImport("kernel32.dll")]
    private static partial nint FindResourceW(nint module, nint name, nint type);

    [LibraryImport("kernel32.dll")]
    private static partial uint SizeofResource(nint module, nint resource);

    [LibraryImport("kernel32.dll")]
    private static partial nint LoadResource(nint module, nint resource);

    [LibraryImport("kernel32.dll")]
    private static partial nint LockResource(nint loaded);
}
