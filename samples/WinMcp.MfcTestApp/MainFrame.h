#pragma once

#include "framework.h"

// View of the frame app: paints the counter, exposes nothing to UI Automation (like most real CView content).
class CCounterView : public CView
{
public:
    int Count = 0;

protected:
    void OnDraw(CDC* dc) override;
    void PostNcDestroy() override { delete this; } // views delete themselves, as in doc/view apps
};

// --frame: a classic SDI-style MFC frame (CFrameWnd + CView + CToolBar + CStatusBar) without doc/view plumbing.
class CMainFrame : public CFrameWnd
{
public:
    CMainFrame() = default;

protected:
    afx_msg int OnCreate(LPCREATESTRUCT create);
    afx_msg void OnSetFocus(CWnd* oldWnd);
    afx_msg void OnFrameNew();
    afx_msg void OnFrameCount();
    afx_msg void OnUpdateCountIndicator(CCmdUI* cmdUI);
    DECLARE_MESSAGE_MAP()

private:
    void SetCount(int count);

    CToolBar m_toolBar;
    CStatusBar m_statusBar;
    CCounterView* m_view = nullptr;
};
