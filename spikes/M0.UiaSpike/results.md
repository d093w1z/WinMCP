
## Environment

- .NET 10.0.12, OS 10.0.26100.0, 64-bit process: True
- Main thread apartment: MTA
- DPI awareness: DPI_AWARENESS_PER_MONITOR_AWARE, system DPI: 96
- FlaUI timeout-related members on UIA3Automation: TransactionTimeout (TimeSpan, settable), ConnectionTimeout (TimeSpan, settable)

## WinForms spike form


### Tree fetch: naive TreeWalker vs CacheRequest(Subtree)

- Naive walk: 20 nodes, median 68.9 ms over 5 runs
- Cached:     20 nodes, median 32.5 ms over 5 runs  (speed-up x2.1)

### Control-view outline (cached)

```
Window "WinMCP Spike Form" #MainForm [WindowsForms10.Window.8.app.0.390be75_r3_ad1] hwnd=0x431298
  Text "Name:" #nameLabel [WindowsForms10.Static.app.0.390be75_r3_ad1] hwnd=0x26139A
  Edit "Name:" #nameTextBox [WindowsForms10.Edit.app.0.390be75_r3_ad1] hwnd=0x420DA6
  Text "Type:" #typeLabel [WindowsForms10.Static.app.0.390be75_r3_ad1] hwnd=0x70DBA
  ComboBox "Type:" #typeComboBox [WindowsForms10.ComboBox.app.0.390be75_r3_ad1] hwnd=0x370294
    Text "Type:" []
    Button "Open" []
  CheckBox "Enable feature" #enableCheckBox [WindowsForms10.Button.app.0.390be75_r3_ad1] hwnd=0x3A0D8A
  Button "Apply" #applyButton [WindowsForms10.Button.app.0.390be75_r3_ad1] hwnd=0x210BFC
  Button "Cancel" #cancelButton [WindowsForms10.Button.app.0.390be75_r3_ad1] hwnd=0x80DC8
  Button "Advanced…" #advancedButton [WindowsForms10.Button.app.0.390be75_r3_ad1] hwnd=0x1A12CE disabled
  Button "Hang 8s" #hangButton [WindowsForms10.Button.app.0.390be75_r3_ad1] hwnd=0x250280
  Text "Status: Ready" #statusLabel [WindowsForms10.Static.app.0.390be75_r3_ad1] hwnd=0x3E0CC2
  Text "Events: 0" #eventCounterLabel [WindowsForms10.Static.app.0.390be75_r3_ad1] hwnd=0x70DC4
  TitleBar "" #TitleBar []
    MenuBar "System" #SystemMenuBar []
      MenuItem "System" []
    Button "Minimize" #Minimize-Restore []
    Button "Maximize" #Maximize-Restore []
    Button "Close" #Close []
```

### DPI / bounds agreement (top-level window)

- GetDpiForWindow: 96
- UIA BoundingRectangle: x=100 y=100 w=486 h=289
- GetWindowRect:         x=100 y=100 w=486 h=289
- DWM extended frame:    x=107 y=100 w=472 h=282

### Golden scenario via UIA patterns (live, uncached)

- Locate 6 elements by AutomationId: 87 ms
- nameTextBox UIA Name (label heuristic): "Name:", LabeledBy: (none)
- Patterns on combo: ExpandCollapse, Invoke, LegacyIAccessible, Value
- Events before: Events: 0
- ValuePattern.SetValue("Mukesh"): 6.4 ms
  value_after = "Mukesh"
- ListItems visible under collapsed combo: 0 [], expand state: Collapsed
  HTML item not found while collapsed; expanding
- SelectionItemPattern.Select(HTML): 5.1 ms
  expand state right after Select: Expanded
  after Collapse(): Collapsed
  combo value_after = "HTML", selection = ""
- Checkbox toggle state: On
- InvokePattern.Invoke(Apply): 5.1 ms
  status after 2 ms: "Status: Applied: Name=Mukesh; Type=HTML; Feature=On"
- Events after: Events: 5 [TextChanged, TextChanged, DropDown, SelectedIndexChanged, DropDownClosed]
- Golden scenario total: 181 ms
- Hidden (Visible=false) textbox in control view: absent
- Disabled button: IsEnabled=False, Invoke pattern supported=True
  Invoke on disabled button did NOT throw; status now "Status: ADVANCED CLICKED WHILE DISABLED"

### Hung target (UI thread sleeps 8 s)

- [default UIA timeouts] live property read took 7502 ms -> ok: "Name:"
  IsHungAppWindow ~6 s into hang: False
- [TransactionTimeout = 2 s] live property read took 2017 ms -> COMException: Operation timed out. (0x80131505)
  IsHungAppWindow ~6 s into hang: True

## Classic Win32 dialog: charmap.exe (MFC stand-in)


### Tree fetch: naive TreeWalker vs CacheRequest(Subtree)

- Naive walk: 27 nodes, median 75.3 ms over 5 runs
- Cached:     27 nodes, median 132.0 ms over 5 runs  (speed-up x0.6)

### Control-view outline (cached)

```
Window "Character Map" [#32770] hwnd=0x2D0AC2
  Pane "Character Grid" #108 [CharGridWClass] hwnd=0x370D38
  Text "Characters to copy :" #107 [Static] hwnd=0x740D16
  Edit "Characters to copy :" #104 [RICHEDIT50W] hwnd=0x270D80
  Button "Select" #103 [Button] hwnd=0x2B0D9E
  Button "Copy" #102 [Button] hwnd=0x90DC6 disabled
  CheckBox "Advanced view" #119 [Button] hwnd=0x321294
  Text "Selected Character :" #500 [Static] hwnd=0xB0DCA
  Text "U+0021: Exclamation Mark" #501 [Static] hwnd=0x1B12CE
  Text "Font :" #141 [Static] hwnd=0x27139A
  ComboBox "Font :" #105 [ComboBox] hwnd=0xB0DB0
    Text "Font :" [] offscreen
    Button "Open" #DropDown []
  Hyperlink "Help" #100 [Button] hwnd=0x140A0C
  ScrollBar "" #204 [ScrollBar] hwnd=0x1D0DA2
    Button "Line up" #UpButton []
    Thumb "Position" #ScrollbarThumb []
    Button "Page down" #DownPageButton []
    Button "Line down" #DownButton []
  StatusBar "" #1002 [msctls_statusbar32] hwnd=0xD0DBC
    Text "" []
  TitleBar "" #TitleBar []
    MenuBar "System" #SystemMenuBar []
      MenuItem "System" []
    Button "Minimize" #Minimize-Restore []
    Button "Maximize" #Maximize-Restore [] disabled
    Button "Close" #Close []
```

### HWND ↔ control ID ↔ AutomationId

| ControlType | Name | ClassName | HWND | GetDlgCtrlID | AutomationId | AutomationId == ctrl ID |
|---|---|---|---|---|---|---|
| Pane | Character Grid | CharGridWClass | 0x370D38 | 108 | 108 | yes |
| Text | Characters to copy : | Static | 0x740D16 | 107 | 107 | yes |
| Edit | Characters to copy : | RICHEDIT50W | 0x270D80 | 104 | 104 | yes |
| Button | Select | Button | 0x2B0D9E | 103 | 103 | yes |
| Button | Copy | Button | 0x90DC6 | 102 | 102 | yes |
| CheckBox | Advanced view | Button | 0x321294 | 119 | 119 | yes |
| Text | Selected Character : | Static | 0xB0DCA | 500 | 500 | yes |
| Text | U+0021: Exclamation Mark | Static | 0x1B12CE | 501 | 501 | yes |
| Text | Font : | Static | 0x27139A | 141 | 141 | yes |
| ComboBox | Font : | ComboBox | 0xB0DB0 | 105 | 105 | yes |
| Hyperlink | Help | Button | 0x140A0C | 100 | 100 | yes |
| ScrollBar |  | ScrollBar | 0x1D0DA2 | 204 | 204 | yes |
| StatusBar |  | msctls_statusbar32 | 0xD0DBC | 1002 | 1002 | yes |

Elements without their own HWND (windowless): 13

### DPI / bounds agreement (top-level window)

- GetDpiForWindow: 96
- UIA BoundingRectangle: x=15 y=65 w=491 h=437
- GetWindowRect:         x=15 y=65 w=491 h=437
- DWM extended frame:    x=22 y=65 w=477 h=430
