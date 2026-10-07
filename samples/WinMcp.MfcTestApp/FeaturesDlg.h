#pragma once

#include "framework.h"
#include "resource.h"

// Owner-drawn button: paints itself; its window text is still set.
class COwnerDrawButton : public CButton
{
protected:
    void DrawItem(LPDRAWITEMSTRUCT item) override;
};

// --features: controls whose UI Automation support M11 measures (owner-drawn, MFC Feature Pack).
class CFeaturesDlg : public CDialogEx
{
    DECLARE_DYNAMIC(CFeaturesDlg) // as Class Wizard generates; CMainDlg deliberately has none (M11 runtime-class spike)

public:
    CFeaturesDlg() : CDialogEx(IDD_FEATURES) {}

protected:
    BOOL OnInitDialog() override;
    void DoDataExchange(CDataExchange* dx) override;
    void OnOK() override {}
    void OnCancel() override {}

    afx_msg void OnClose() { EndDialog(IDCANCEL); }
    afx_msg void OnOwnerDrawClicked() { SetStatus(L"Status: Owner-drawn clicked"); }
    afx_msg void OnMfcButtonClicked() { SetStatus(L"Status: MFC button clicked"); }
    afx_msg void OnColorChanged();
    afx_msg void OnFolderChanged();
    afx_msg void OnPhoneChanged();
    afx_msg LRESULT OnPropertyChanged(WPARAM control, LPARAM property);
    DECLARE_MESSAGE_MAP()

private:
    void SetStatus(const CString& status) { SetDlgItemTextW(IDC_FEATURES_STATUS, status); }

    COwnerDrawButton m_ownerDraw;
    CMFCButton m_mfcButton;
    CMFCColorButton m_color;
    CMFCEditBrowseCtrl m_folder;
    CMFCMaskedEdit m_phone;
    CMFCPropertyGridCtrl m_grid;
};
