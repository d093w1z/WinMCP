#pragma once

#include "framework.h"
#include "resource.h"

// Modal CDialog, like the WinForms TestApp's "Confirm" dialog.
class CConfirmDlg : public CDialogEx
{
public:
    explicit CConfirmDlg(CWnd* parent) : CDialogEx(IDD_CONFIRM, parent) {}

    CString Note;

protected:
    void DoDataExchange(CDataExchange* dx) override
    {
        CDialogEx::DoDataExchange(dx);
        DDX_Text(dx, IDC_EDIT_NOTE, Note);
    }
};
