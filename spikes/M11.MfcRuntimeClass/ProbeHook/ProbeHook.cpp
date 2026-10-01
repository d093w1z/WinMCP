// M11 spike, research only: runs INSIDE an MFC process to read what no out-of-process API can see — the C++ runtime
// class behind each window (CWnd::FromHandlePermanent → GetRuntimeClass). Never part of the WinMCP server.
//
// Built as an MFC *extension* DLL on purpose: it has no module state of its own, so on the target's UI thread it sees
// the application's handle map. That map is per thread (and per MFC module), which is why the code runs from a
// WH_GETMESSAGE hook on the window's own thread rather than from a remote thread.
#include <afxwin.h>
#include <afxdllx.h>
#include <string>

static AFX_EXTENSION_MODULE ProbeModule = { NULL, NULL };
static UINT ProbeMessage = RegisterWindowMessageW(L"WinMcp.M11.ProbeRuntimeClasses");

extern "C" int APIENTRY DllMain(HINSTANCE instance, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
        return AfxInitExtensionModule(ProbeModule, instance);
    if (reason == DLL_PROCESS_DETACH)
        AfxTermExtensionModule(ProbeModule);
    return 1;
}

static std::wstring Chain(const CRuntimeClass* runtimeClass)
{
    std::wstring chain;
    for (auto c = runtimeClass; c != nullptr; c = c->m_pfnGetBaseClass != nullptr ? c->m_pfnGetBaseClass() : nullptr)
    {
        if (!chain.empty())
            chain += L" : ";
        chain += CString(c->m_lpszClassName).GetString();
    }
    return chain;
}

static void Describe(HWND hwnd, int depth, FILE* out)
{
    wchar_t className[128] = {};
    GetClassNameW(hwnd, className, 127);
    CWnd* wnd = CWnd::FromHandlePermanent(hwnd); // only windows with a C++ object MFC created or attached
    fwprintf(out, L"%*s0x%p id=%d class=%s -> %s\n", depth * 2, L"", hwnd, GetDlgCtrlID(hwnd), className,
        wnd != nullptr ? Chain(wnd->GetRuntimeClass()).c_str() : L"(no permanent CWnd)");
    for (HWND child = GetWindow(hwnd, GW_CHILD); child != nullptr; child = GetWindow(child, GW_HWNDNEXT))
        Describe(child, depth + 1, out);
}

static BOOL CALLBACK DescribeTopLevel(HWND hwnd, LPARAM out)
{
    if (IsWindowVisible(hwnd))
        Describe(hwnd, 0, reinterpret_cast<FILE*>(out));
    return TRUE;
}

extern "C" __declspec(dllexport) LRESULT CALLBACK ProbeHookProc(int code, WPARAM wParam, LPARAM lParam)
{
    auto message = reinterpret_cast<MSG*>(lParam);
    if (code == HC_ACTION && wParam == PM_REMOVE && message->message == ProbeMessage)
    {
        wchar_t path[MAX_PATH];
        GetTempPathW(MAX_PATH, path);
        wchar_t file[MAX_PATH];
        swprintf_s(file, L"%swinmcp-m11-probe-%lu.txt", path, GetCurrentProcessId());
        FILE* out = nullptr;
        if (_wfopen_s(&out, file, L"w, ccs=UTF-8") == 0 && out != nullptr)
        {
            fwprintf(out, L"thread %lu, module state %p\n", GetCurrentThreadId(), AfxGetModuleState());
            EnumThreadWindows(GetCurrentThreadId(), DescribeTopLevel, reinterpret_cast<LPARAM>(out));
            fclose(out);
        }
        message->message = WM_NULL; // consumed
    }
    return CallNextHookEx(nullptr, code, wParam, lParam);
}
