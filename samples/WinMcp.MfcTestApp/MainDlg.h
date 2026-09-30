#pragma once

#include "framework.h"
#include "resource.h"
#include "CanvasWnd.h"

// MFC twin of samples/WinMcp.TestApp: same status strings and event-log names, so one test suite covers both.
class CMainDlg : public CDialogEx
{
public:
    CMainDlg() : CDialogEx(IDD_MAIN) {}

protected:
    BOOL OnInitDialog() override;
    void OnOK() override {}     // Enter must not close the app
    void OnCancel() override {} // nor Esc; closing is via the caption button (OnClose)

    afx_msg void OnClose();
    afx_msg void OnTimer(UINT_PTR id);
    afx_msg void OnNameChanged();
    afx_msg void OnTypeChanged();
    afx_msg void OnTypeDropDown();
    afx_msg void OnTypeCloseUp();
    afx_msg void OnEnableClicked();
    afx_msg void OnApply();
    afx_msg void OnCancelClicked();
    afx_msg void OnAdvanced();
    afx_msg void OnSlowApply();
    afx_msg void OnAddField();
    afx_msg void OnFreeze();
    afx_msg void OnDialog();
    afx_msg void OnDynamicChanged(UINT id);
    afx_msg void OnListItemChanged(NMHDR* header, LRESULT* result);
    afx_msg void OnTreeSelChanged(NMHDR* header, LRESULT* result);
    afx_msg void OnTreeItemExpanded(NMHDR* header, LRESULT* result);
    afx_msg void OnMenuFileNew();
    afx_msg void OnMenuEditClearName();
    afx_msg void OnMenuHelpAbout();
    DECLARE_MESSAGE_MAP()

private:
    static constexpr UINT_PTR SlowApplyTimer = 1;
    static constexpr int MaxDynamicFields = 3;

    void Apply();
    void Reset();
    void Record(const CString& eventName);
    void RenderEventLog();
    void UpdateAdvancedEnabled();
    void SetStatus(const CString& status) { SetDlgItemTextW(IDC_STATIC_STATUS, status); }
    CString Text(int id) const;

    CComboBox m_type;
    CListCtrl m_list;
    CTreeCtrl m_tree;
    CCanvasWnd m_canvas;
    std::vector<CString> m_eventLog;
    std::vector<std::unique_ptr<CWnd>> m_dynamicControls;
    int m_dynamicFieldCount = 0;
    bool m_resetting = false;
};
