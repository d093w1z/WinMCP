#pragma once

#include "framework.h"

// Paints its own content and exposes nothing to accessibility — the typical custom CWnd control of legacy MFC apps.
class CCanvasWnd : public CWnd
{
public:
    static constexpr const wchar_t* ClassName = L"WinMcpCanvas";

    // Registered before the dialog is created, because its template refers to the class by name.
    static void RegisterWindowClass();

protected:
    afx_msg void OnPaint();
    DECLARE_MESSAGE_MAP()
};
