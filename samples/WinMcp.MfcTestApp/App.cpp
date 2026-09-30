#include "App.h"
#include "MainDlg.h"
#include "CanvasWnd.h"
#include "MainFrame.h"
#include "resource.h"

CTestApp theApp;

BOOL CTestApp::InitInstance()
{
    INITCOMMONCONTROLSEX controls{ sizeof(controls), ICC_WIN95_CLASSES };
    InitCommonControlsEx(&controls);
    CWinApp::InitInstance();

    if (!ParseArguments())
    {
        ::MessageBoxW(nullptr, L"Usage: WinMcp.MfcTestApp.exe [--frame] [--position x,y]", L"WinMCP MFC Test App", MB_OK | MB_ICONERROR);
        return FALSE;
    }

    if (Frame)
    {
        auto frame = new CMainFrame; // deletes itself when destroyed
        if (!frame->LoadFrame(IDR_FRAME, WS_OVERLAPPEDWINDOW))
            return FALSE;
        if (HasPosition)
            frame->SetWindowPos(nullptr, Position.x, Position.y, 640, 400, SWP_NOZORDER);
        m_pMainWnd = frame;
        frame->ShowWindow(SW_SHOW);
        frame->UpdateWindow();
        return TRUE; // run the message loop
    }

    CCanvasWnd::RegisterWindowClass();

    CMainDlg dialog;
    m_pMainWnd = &dialog;
    dialog.DoModal();
    return FALSE; // no message loop after the dialog closes
}

bool CTestApp::ParseArguments()
{
    for (int i = 1; i < __argc; i++)
    {
        const std::wstring argument = __wargv[i];
        if (argument == L"--frame")
        {
            Frame = true;
        }
        else if (argument == L"--position" && i + 1 < __argc)
        {
            int x = 0, y = 0;
            if (swscanf_s(__wargv[++i], L"%d,%d", &x, &y) != 2)
                return false;
            HasPosition = true;
            Position = CPoint(x, y);
        }
        else
        {
            return false;
        }
    }
    return true;
}
