// M11 spike, research only: loads ProbeHook.dll into the UI thread of ONE window (thread-scoped WH_GETMESSAGE hook),
// asks it to report runtime classes, prints the report and unhooks. Usage: Injector.exe <hwnd in hex>
#include <windows.h>
#include <cstdio>
#include <string>

int wmain(int argc, wchar_t** argv)
{
    if (argc != 2)
    {
        fwprintf(stderr, L"usage: Injector.exe <hwnd hex>\n");
        return 2;
    }
    auto hwnd = reinterpret_cast<HWND>(static_cast<ULONG_PTR>(wcstoull(argv[1], nullptr, 16)));
    DWORD pid = 0;
    const DWORD thread = GetWindowThreadProcessId(hwnd, &pid);
    if (thread == 0)
    {
        fwprintf(stderr, L"no such window\n");
        return 1;
    }

    // DONT_RESOLVE_DLL_REFERENCES: the injector only needs the hook address; DllMain (MFC init) runs in the target.
    wchar_t self[MAX_PATH];
    GetModuleFileNameW(nullptr, self, MAX_PATH);
    std::wstring dll(self);
    dll = dll.substr(0, dll.find_last_of(L'\\') + 1) + L"ProbeHook.dll";
    HMODULE module = LoadLibraryExW(dll.c_str(), nullptr, DONT_RESOLVE_DLL_REFERENCES);
    auto proc = module != nullptr ? reinterpret_cast<HOOKPROC>(GetProcAddress(module, "ProbeHookProc")) : nullptr;
    if (proc == nullptr)
    {
        fwprintf(stderr, L"cannot load %s (%lu)\n", dll.c_str(), GetLastError());
        return 1;
    }

    wchar_t temp[MAX_PATH];
    GetTempPathW(MAX_PATH, temp);
    wchar_t report[MAX_PATH];
    swprintf_s(report, L"%swinmcp-m11-probe-%lu.txt", temp, pid);
    DeleteFileW(report);

    const DWORD start = GetTickCount();
    HHOOK hook = SetWindowsHookExW(WH_GETMESSAGE, proc, module, thread);
    if (hook == nullptr)
    {
        fwprintf(stderr, L"SetWindowsHookEx failed (%lu)\n", GetLastError());
        return 1;
    }
    PostMessageW(hwnd, RegisterWindowMessageW(L"WinMcp.M11.ProbeRuntimeClasses"), 0, 0);

    bool done = false;
    for (int i = 0; i < 100 && !done; i++)
    {
        Sleep(50);
        done = GetFileAttributesW(report) != INVALID_FILE_ATTRIBUTES;
    }
    Sleep(100);
    UnhookWindowsHookEx(hook);
    PostMessageW(hwnd, WM_NULL, 0, 0); // lets the target unload the DLL
    if (!done)
    {
        fwprintf(stderr, L"no report within 5 s\n");
        return 1;
    }

    FILE* in = nullptr;
    _wfopen_s(&in, report, L"r, ccs=UTF-8");
    wchar_t line[1024];
    while (in != nullptr && fgetws(line, 1024, in) != nullptr)
        fputws(line, stdout);
    if (in != nullptr)
        fclose(in);
    wprintf(L"(round trip %lu ms)\n", GetTickCount() - start);
    return 0;
}
