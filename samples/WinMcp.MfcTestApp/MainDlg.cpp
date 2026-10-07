#include "MainDlg.h"
#include "App.h"
#include "ConfirmDlg.h"

BEGIN_MESSAGE_MAP(CMainDlg, CDialogEx)
    ON_WM_CLOSE()
    ON_WM_TIMER()
    ON_EN_CHANGE(IDC_EDIT_NAME, &CMainDlg::OnNameChanged)
    ON_CBN_SELCHANGE(IDC_COMBO_TYPE, &CMainDlg::OnTypeChanged)
    ON_CBN_DROPDOWN(IDC_COMBO_TYPE, &CMainDlg::OnTypeDropDown)
    ON_CBN_CLOSEUP(IDC_COMBO_TYPE, &CMainDlg::OnTypeCloseUp)
    ON_BN_CLICKED(IDC_CHECK_ENABLE, &CMainDlg::OnEnableClicked)
    ON_BN_CLICKED(IDC_BUTTON_APPLY, &CMainDlg::OnApply)
    ON_BN_CLICKED(IDC_BUTTON_CANCEL, &CMainDlg::OnCancelClicked)
    ON_BN_CLICKED(IDC_BUTTON_ADVANCED, &CMainDlg::OnAdvanced)
    ON_BN_CLICKED(IDC_BUTTON_SLOW_APPLY, &CMainDlg::OnSlowApply)
    ON_BN_CLICKED(IDC_BUTTON_ADD_FIELD, &CMainDlg::OnAddField)
    ON_BN_CLICKED(IDC_BUTTON_FREEZE, &CMainDlg::OnFreeze)
    ON_BN_CLICKED(IDC_BUTTON_DIALOG, &CMainDlg::OnDialog)
    ON_CONTROL_RANGE(EN_CHANGE, IDC_EDIT_DYNAMIC1, IDC_EDIT_DYNAMIC3, &CMainDlg::OnDynamicChanged)
    ON_NOTIFY(LVN_ITEMCHANGED, IDC_LIST_ITEMS, &CMainDlg::OnListItemChanged)
    ON_NOTIFY(TVN_SELCHANGED, IDC_TREE_ITEMS, &CMainDlg::OnTreeSelChanged)
    ON_NOTIFY(TVN_ITEMEXPANDED, IDC_TREE_ITEMS, &CMainDlg::OnTreeItemExpanded)
    ON_COMMAND(ID_MENU_FILE_NEW, &CMainDlg::OnMenuFileNew)
    ON_COMMAND(ID_MENU_EDIT_CLEARNAME, &CMainDlg::OnMenuEditClearName)
    ON_COMMAND(ID_MENU_HELP_ABOUT, &CMainDlg::OnMenuHelpAbout)
END_MESSAGE_MAP()

namespace
{
    struct Item { const wchar_t* Name; const wchar_t* Type; const wchar_t* Size; };
    constexpr Item Items[] = {
        { L"Alpha", L"Text", L"1 KB" }, { L"Beta", L"HTML", L"2 KB" }, { L"Gamma", L"Markdown", L"3 KB" },
        { L"Delta", L"Text", L"4 KB" }, { L"Epsilon", L"HTML", L"5 KB" },
    };
}

BOOL CMainDlg::OnInitDialog()
{
    m_resetting = true; // initialization must not show up in the event log
    CDialogEx::OnInitDialog();

    m_type.SubclassDlgItem(IDC_COMBO_TYPE, this);
    for (auto type : { L"Text", L"HTML", L"Markdown" })
        m_type.AddString(type);
    m_type.SetCurSel(0);
    CheckDlgButton(IDC_CHECK_ENABLE, BST_CHECKED);

    m_list.SubclassDlgItem(IDC_LIST_ITEMS, this);
    m_list.SetExtendedStyle(LVS_EX_FULLROWSELECT);
    m_list.InsertColumn(0, L"Name", LVCFMT_LEFT, 90);
    m_list.InsertColumn(1, L"Type", LVCFMT_LEFT, 70);
    m_list.InsertColumn(2, L"Size", LVCFMT_LEFT, 60);
    for (int i = 0; i < _countof(Items); i++)
    {
        m_list.InsertItem(i, Items[i].Name);
        m_list.SetItemText(i, 1, Items[i].Type);
        m_list.SetItemText(i, 2, Items[i].Size);
    }

    m_tree.SubclassDlgItem(IDC_TREE_ITEMS, this);
    const HTREEITEM documents = m_tree.InsertItem(L"Documents");
    const HTREEITEM reports = m_tree.InsertItem(L"Reports", documents);
    m_tree.InsertItem(L"Q1.txt", reports);
    m_tree.InsertItem(L"Q2.txt", reports);
    const HTREEITEM notes = m_tree.InsertItem(L"Notes", documents);
    m_tree.InsertItem(L"todo.txt", notes);

    m_canvas.SubclassDlgItem(IDC_CANVAS, this);

    if (theApp.HasPosition)
        SetWindowPos(nullptr, theApp.Position.x, theApp.Position.y, 0, 0, SWP_NOSIZE | SWP_NOZORDER);

    m_resetting = false;
    RenderEventLog();
    UpdateAdvancedEnabled();
    return TRUE;
}

void CMainDlg::OnClose()
{
    EndDialog(IDCANCEL);
}

CString CMainDlg::Text(int id) const
{
    CString text;
    GetDlgItemTextW(id, text);
    return text;
}

void CMainDlg::Apply()
{
    CString type;
    m_type.GetLBText(m_type.GetCurSel(), type);
    CString status;
    status.Format(L"Status: Applied: Name=%s; Type=%s; Feature=%s",
        Text(IDC_EDIT_NAME).GetString(), type.GetString(), IsDlgButtonChecked(IDC_CHECK_ENABLE) ? L"On" : L"Off");
    SetStatus(status);
}

// Restores every input to its default, removes dynamic fields, clears list/tree selection, collapses the tree and
// clears the event log, so tests can start from a known state without relaunching (TestApp contract).
void CMainDlg::Reset()
{
    m_resetting = true;
    KillTimer(SlowApplyTimer);
    SetDlgItemTextW(IDC_EDIT_NAME, L"");
    m_type.SetCurSel(0);
    CheckDlgButton(IDC_CHECK_ENABLE, BST_CHECKED);
    for (auto& control : m_dynamicControls)
        control->DestroyWindow();
    m_dynamicControls.clear();
    m_dynamicFieldCount = 0;
    for (int i = 0; i < m_list.GetItemCount(); i++)
        m_list.SetItemState(i, 0, LVIS_SELECTED | LVIS_FOCUSED);
    m_tree.SelectItem(nullptr);
    for (HTREEITEM item = m_tree.GetRootItem(); item != nullptr; item = m_tree.GetNextSiblingItem(item))
        m_tree.Expand(item, TVE_COLLAPSE);
    m_resetting = false;

    UpdateAdvancedEnabled();
    m_eventLog.clear();
    RenderEventLog();
    SetStatus(L"Status: Ready");
}

void CMainDlg::Record(const CString& eventName)
{
    if (m_resetting)
        return;
    m_eventLog.push_back(eventName);
    RenderEventLog();
}

void CMainDlg::RenderEventLog()
{
    CString joined;
    for (size_t i = 0; i < m_eventLog.size(); i++)
        joined += (i > 0 ? L", " : L"") + m_eventLog[i];
    CString text;
    text.Format(L"Events: %d [%s]", static_cast<int>(m_eventLog.size()), joined.GetString());
    SetDlgItemTextW(IDC_STATIC_EVENTS, text);
}

void CMainDlg::UpdateAdvancedEnabled()
{
    GetDlgItem(IDC_BUTTON_ADVANCED)->EnableWindow(IsDlgButtonChecked(IDC_CHECK_ENABLE) && !Text(IDC_EDIT_NAME).IsEmpty());
}

void CMainDlg::OnNameChanged() { Record(L"nameTextBox.TextChanged"); UpdateAdvancedEnabled(); }
void CMainDlg::OnTypeChanged() { Record(L"typeComboBox.SelectedIndexChanged"); }
void CMainDlg::OnTypeDropDown() { Record(L"typeComboBox.DropDown"); }
void CMainDlg::OnTypeCloseUp() { Record(L"typeComboBox.DropDownClosed"); }
void CMainDlg::OnEnableClicked() { Record(L"enableCheckBox.CheckedChanged"); UpdateAdvancedEnabled(); }
void CMainDlg::OnApply() { Apply(); }
void CMainDlg::OnCancelClicked() { Reset(); }
void CMainDlg::OnAdvanced() { SetStatus(L"Status: Advanced options opened"); }

void CMainDlg::OnSlowApply()
{
    SetStatus(L"Status: Applying...");
    SetTimer(SlowApplyTimer, 1500, nullptr);
}

void CMainDlg::OnTimer(UINT_PTR id)
{
    if (id == SlowApplyTimer)
    {
        KillTimer(SlowApplyTimer);
        Apply();
    }
    CDialogEx::OnTimer(id);
}

void CMainDlg::OnAddField()
{
    if (m_dynamicFieldCount == MaxDynamicFields)
        return;
    const int index = ++m_dynamicFieldCount;

    // Dialog units → pixels, like controls from the template (so layout follows the dialog font and DPI).
    CRect labelRect(7, 105 + (index - 1) * 18, 67, 113 + (index - 1) * 18);
    CRect editRect(80, 103 + (index - 1) * 18, 220, 117 + (index - 1) * 18);
    MapDialogRect(&labelRect);
    MapDialogRect(&editRect);

    CString caption;
    caption.Format(L"Dynamic %d:", index);
    auto label = std::make_unique<CStatic>();
    label->Create(caption, WS_CHILD | WS_VISIBLE, labelRect, this, IDC_STATIC_DYNAMIC1 + index - 1);
    label->SetFont(GetFont());
    auto edit = std::make_unique<CEdit>();
    edit->CreateEx(WS_EX_CLIENTEDGE, L"EDIT", L"", WS_CHILD | WS_VISIBLE | WS_TABSTOP | ES_AUTOHSCROLL, editRect, this, IDC_EDIT_DYNAMIC1 + index - 1);
    edit->SetFont(GetFont());
    m_dynamicControls.push_back(std::move(label));
    m_dynamicControls.push_back(std::move(edit));
    Record(L"addFieldButton.FieldAdded");
}

void CMainDlg::OnDynamicChanged(UINT id)
{
    CString name;
    name.Format(L"dynamicTextBox%d.TextChanged", static_cast<int>(id - IDC_EDIT_DYNAMIC1 + 1));
    Record(name);
}

void CMainDlg::OnFreeze()
{
    ::Sleep(8000); // blocks the UI thread, like a hung application
}

void CMainDlg::OnDialog()
{
    CConfirmDlg confirm(this);
    if (confirm.DoModal() == IDOK)
        SetStatus(L"Status: Dialog OK: " + confirm.Note);
    else
        SetStatus(L"Status: Dialog cancelled");
}

void CMainDlg::OnListItemChanged(NMHDR* header, LRESULT* result)
{
    const auto* change = reinterpret_cast<NMLISTVIEW*>(header);
    if ((change->uChanged & LVIF_STATE) && (change->uNewState & LVIS_SELECTED) && !(change->uOldState & LVIS_SELECTED))
    {
        Record(L"itemsListView.SelectedIndexChanged");
        if (!m_resetting)
            SetStatus(L"Status: Selected item: " + m_list.GetItemText(change->iItem, 0));
    }
    *result = 0;
}

void CMainDlg::OnTreeSelChanged(NMHDR* header, LRESULT* result)
{
    const auto* change = reinterpret_cast<NMTREEVIEW*>(header);
    if (change->itemNew.hItem != nullptr)
    {
        Record(L"itemsTreeView.AfterSelect");
        if (!m_resetting)
            SetStatus(L"Status: Selected node: " + m_tree.GetItemText(change->itemNew.hItem));
    }
    *result = 0;
}

void CMainDlg::OnTreeItemExpanded(NMHDR* header, LRESULT* result)
{
    const auto* change = reinterpret_cast<NMTREEVIEW*>(header);
    if (change->action == TVE_EXPAND)
        Record(L"itemsTreeView.AfterExpand(" + m_tree.GetItemText(change->itemNew.hItem) + L")");
    *result = 0;
}

void CMainDlg::OnMenuFileNew()
{
    Reset();
    SetStatus(L"Status: Menu: File > New");
}

void CMainDlg::OnMenuEditClearName()
{
    SetDlgItemTextW(IDC_EDIT_NAME, L"");
    SetStatus(L"Status: Menu: Edit > Clear name");
}

void CMainDlg::OnMenuHelpAbout()
{
    // A classic Win32 message box (#32770), as in the WinForms TestApp.
    ::MessageBoxW(GetSafeHwnd(), L"WinMCP MFC Test App", L"About WinMCP Test App", MB_OK | MB_ICONINFORMATION);
    SetStatus(L"Status: About closed");
}
