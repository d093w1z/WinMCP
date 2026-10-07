#pragma once

#include "framework.h"

class CTestApp : public CWinApp
{
public:
    BOOL InitInstance() override;

    // --position x,y (same as the WinForms TestApp), applied by the main dialog.
    bool HasPosition = false;
    CPoint Position;

    // --frame: start the CFrameWnd app (toolbar, status bar, view) instead of the dialog.
    bool Frame = false;

    // --features: the dialog with owner-drawn and MFC Feature Pack controls.
    bool Features = false;

private:
    bool ParseArguments();
};

extern CTestApp theApp;
