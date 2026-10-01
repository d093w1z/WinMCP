#include "CanvasWnd.h"

BEGIN_MESSAGE_MAP(CCanvasWnd, CWnd)
    ON_WM_PAINT()
END_MESSAGE_MAP()

void CCanvasWnd::RegisterWindowClass()
{
    WNDCLASSW wc{};
    wc.lpfnWndProc = ::DefWindowProcW; // replaced by MFC when the dialog subclasses the control
    wc.hInstance = AfxGetInstanceHandle();
    wc.hCursor = ::LoadCursor(nullptr, IDC_ARROW);
    wc.hbrBackground = static_cast<HBRUSH>(::GetStockObject(WHITE_BRUSH));
    wc.lpszClassName = ClassName;
    AfxRegisterClass(&wc);
}

void CCanvasWnd::OnPaint()
{
    CPaintDC dc(this);
    CRect client;
    GetClientRect(&client);
    dc.FillSolidRect(client, RGB(255, 255, 255));
    dc.SetTextColor(RGB(0, 0, 139));
    dc.SetBkMode(TRANSPARENT);
    dc.SelectObject(GetParent()->GetFont());
    dc.TextOutW(8, 4, L"Custom drawn: 42");
    CRect box(client.right - 60, 4, client.right - 8, client.bottom - 4);
    dc.Draw3dRect(box, RGB(0, 0, 139), RGB(0, 0, 139));
}
