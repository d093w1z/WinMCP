#pragma once

#ifndef VC_EXTRALEAN
#define VC_EXTRALEAN
#endif

#include <sdkddkver.h>

#define _ATL_CSTRING_EXPLICIT_CONSTRUCTORS

#include <afxwin.h>
#include <afxext.h>
#include <afxdialogex.h>
#include <afxcmn.h>
#include <afxcontrolbars.h> // MFC Feature Pack controls (--features)

#include <memory>
#include <string>
#include <vector>

// Common Controls v6 (themed controls, as real MFC applications use).
#pragma comment(linker, "/manifestdependency:\"type='win32' name='Microsoft.Windows.Common-Controls' version='6.0.0.0' processorArchitecture='*' publicKeyToken='6595b64144ccf1df' language='*'\"")
