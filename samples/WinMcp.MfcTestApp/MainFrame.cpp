#include "MainFrame.h"
#include "resource.h"

void CCounterView::OnDraw(CDC* dc)
{
    CString text;
    text.Format(L"Count: %d", Count);
    dc->TextOutW(10, 10, text);
}

BEGIN_MESSAGE_MAP(CMainFrame, CFrameWnd)
    ON_WM_CREATE()
    ON_WM_SETFOCUS()
    ON_COMMAND(ID_FRAME_NEW, &CMainFrame::OnFrameNew)
    ON_COMMAND(ID_FRAME_COUNT, &CMainFrame::OnFrameCount)
    ON_UPDATE_COMMAND_UI(ID_INDICATOR_COUNT, &CMainFrame::OnUpdateCountIndicator)
END_MESSAGE_MAP()

namespace
{
    const UINT Indicators[] = { ID_SEPARATOR, ID_INDICATOR_COUNT };
    const UINT ToolBarButtons[] = { ID_FRAME_NEW, ID_FRAME_COUNT };
}

int CMainFrame::OnCreate(LPCREATESTRUCT create)
{
    if (CFrameWnd::OnCreate(create) == -1)
        return -1;

    m_view = new CCounterView;
    if (!m_view->Create(nullptr, nullptr, AFX_WS_DEFAULT_VIEW, CRect(0, 0, 0, 0), this, AFX_IDW_PANE_FIRST, nullptr))
        return -1;

    // No toolbar bitmap resource: the common controls' standard images (as many small utilities do).
    if (!m_toolBar.CreateEx(this, TBSTYLE_FLAT, WS_CHILD | WS_VISIBLE | CBRS_TOP | CBRS_TOOLTIPS | CBRS_FLYBY))
        return -1;
    TBADDBITMAP standardImages{ HINST_COMMCTRL, IDB_STD_SMALL_COLOR };
    m_toolBar.GetToolBarCtrl().SendMessage(TB_ADDBITMAP, 0, reinterpret_cast<LPARAM>(&standardImages));
    m_toolBar.SetSizes(CSize(23, 22), CSize(16, 16));
    m_toolBar.SetButtons(ToolBarButtons, _countof(ToolBarButtons));
    m_toolBar.SetButtonInfo(0, ID_FRAME_NEW, TBBS_BUTTON, STD_FILENEW);
    m_toolBar.SetButtonInfo(1, ID_FRAME_COUNT, TBBS_BUTTON, STD_PROPERTIES);

    if (!m_statusBar.Create(this) || !m_statusBar.SetIndicators(Indicators, _countof(Indicators)))
        return -1;
    return 0;
}

void CMainFrame::OnSetFocus(CWnd* /*oldWnd*/)
{
    if (m_view != nullptr)
        m_view->SetFocus();
}

void CMainFrame::SetCount(int count)
{
    m_view->Count = count;
    m_view->Invalidate();
}

void CMainFrame::OnFrameNew() { SetCount(0); }
void CMainFrame::OnFrameCount() { SetCount(m_view->Count + 1); }

// MFC idle processing keeps the pane current (the classic CAPS/NUM indicator mechanism).
void CMainFrame::OnUpdateCountIndicator(CCmdUI* cmdUI)
{
    cmdUI->Enable();
    CString text;
    text.Format(L"Count: %d", m_view != nullptr ? m_view->Count : 0);
    cmdUI->SetText(text);
}
