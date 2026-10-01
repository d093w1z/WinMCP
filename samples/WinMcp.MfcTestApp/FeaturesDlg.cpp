#include "FeaturesDlg.h"
#include "App.h"

void COwnerDrawButton::DrawItem(LPDRAWITEMSTRUCT item)
{
    CDC* dc = CDC::FromHandle(item->hDC);
    CRect rect(item->rcItem);
    const bool pressed = (item->itemState & ODS_SELECTED) != 0;
    dc->FillSolidRect(rect, pressed ? RGB(0, 90, 160) : RGB(0, 120, 215));
    dc->SetTextColor(RGB(255, 255, 255));
    dc->SetBkMode(TRANSPARENT);
    dc->DrawTextW(L"Draw me", rect, DT_CENTER | DT_VCENTER | DT_SINGLELINE); // differs from the window text on purpose
    if (item->itemState & ODS_FOCUS)
        dc->DrawFocusRect(rect);
}

IMPLEMENT_DYNAMIC(CFeaturesDlg, CDialogEx)

BEGIN_MESSAGE_MAP(CFeaturesDlg, CDialogEx)
    ON_WM_CLOSE()
    ON_BN_CLICKED(IDC_OWNERDRAW_BUTTON, &CFeaturesDlg::OnOwnerDrawClicked)
    ON_BN_CLICKED(IDC_MFC_BUTTON, &CFeaturesDlg::OnMfcButtonClicked)
    ON_BN_CLICKED(IDC_MFC_COLOR, &CFeaturesDlg::OnColorChanged)
    ON_EN_CHANGE(IDC_MFC_EDITBROWSE, &CFeaturesDlg::OnFolderChanged)
    ON_EN_CHANGE(IDC_MFC_MASKED, &CFeaturesDlg::OnPhoneChanged)
    ON_REGISTERED_MESSAGE(AFX_WM_PROPERTY_CHANGED, &CFeaturesDlg::OnPropertyChanged)
END_MESSAGE_MAP()

void CFeaturesDlg::DoDataExchange(CDataExchange* dx)
{
    CDialogEx::DoDataExchange(dx);
    DDX_Control(dx, IDC_OWNERDRAW_BUTTON, m_ownerDraw);
    DDX_Control(dx, IDC_MFC_BUTTON, m_mfcButton);
    DDX_Control(dx, IDC_MFC_COLOR, m_color);
    DDX_Control(dx, IDC_MFC_EDITBROWSE, m_folder);
    DDX_Control(dx, IDC_MFC_MASKED, m_phone);
}

BOOL CFeaturesDlg::OnInitDialog()
{
    CDialogEx::OnInitDialog();

    m_color.SetColor(RGB(255, 0, 0));
    m_folder.EnableFileBrowseButton(); // the folder variant needs CWinAppEx::InitShellManager
    m_phone.EnableMask(L" ddd  ddd dddd", L"(___) ___-____", L'_'); // mask and template must be the same length
    m_phone.SetValidChars(nullptr);

    // Property grid in place of the hidden placeholder (the usual way to put one on a dialog).
    CWnd* placeholder = GetDlgItem(IDC_MFC_PROPGRID);
    CRect rect;
    placeholder->GetWindowRect(rect);
    ScreenToClient(rect);
    placeholder->DestroyWindow();
    m_grid.Create(WS_CHILD | WS_VISIBLE | WS_BORDER | WS_TABSTOP, rect, this, IDC_MFC_PROPGRID);
    m_grid.EnableHeaderCtrl(TRUE, L"Property", L"Value");
    m_grid.SetVSDotNetLook();
    auto general = new CMFCPropertyGridProperty(L"General");
    general->AddSubItem(new CMFCPropertyGridProperty(L"Name", COleVariant(L"Alpha"), L"Item name"));
    general->AddSubItem(new CMFCPropertyGridProperty(L"Enabled", COleVariant(static_cast<short>(VARIANT_TRUE), VT_BOOL), L"Feature switch"));
    auto type = new CMFCPropertyGridProperty(L"Type", COleVariant(L"Text"), L"Item type");
    type->AddOption(L"Text");
    type->AddOption(L"HTML");
    type->AddOption(L"Markdown");
    type->AllowEdit(FALSE);
    general->AddSubItem(type);
    m_grid.AddProperty(general);

    if (theApp.HasPosition)
        SetWindowPos(nullptr, theApp.Position.x, theApp.Position.y, 0, 0, SWP_NOSIZE | SWP_NOZORDER);
    return TRUE;
}

void CFeaturesDlg::OnColorChanged()
{
    CString status;
    const COLORREF color = m_color.GetColor();
    status.Format(L"Status: Color=%d,%d,%d", GetRValue(color), GetGValue(color), GetBValue(color));
    SetStatus(status);
}

void CFeaturesDlg::OnFolderChanged()
{
    CString text;
    m_folder.GetWindowTextW(text);
    SetStatus(L"Status: File=" + text);
}

void CFeaturesDlg::OnPhoneChanged()
{
    CString text;
    m_phone.GetWindowTextW(text);
    SetStatus(L"Status: Phone=" + text);
}

LRESULT CFeaturesDlg::OnPropertyChanged(WPARAM, LPARAM property)
{
    auto changed = reinterpret_cast<CMFCPropertyGridProperty*>(property);
    SetStatus(CString(L"Status: Property ") + changed->GetName() + L"=" + CString(changed->GetValue()));
    return 0;
}
