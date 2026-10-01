namespace WinMcp.TestApp;

/// <summary>
/// TestApp v2. Every control has a stable <see cref="Control.Name"/> (= UIA AutomationId) and every behavior is
/// deterministic. The contract (names, texts, status strings) is relied on by tests â€” change it deliberately.
/// v1: the form on the left. v2 (M9): menu bar, modal dialogs, and a tabbed panel with a list view, a tree view and a
/// custom-drawn control.
/// </summary>
internal sealed class MainForm : Form
{
    public const string ReadyStatus = "Status: Ready";
    public const string ApplyingStatus = "Status: Applying...";
    public const string AdvancedStatus = "Status: Advanced options opened";
    public static readonly TimeSpan SlowApplyDelay = TimeSpan.FromMilliseconds(1500);

    /// <summary>
    /// Exceeds WinMCP's 3 s UIA timeout and leaves a ~3 s window after Windows flags the app as hung (~5 s),
    /// so tests can observe both.
    /// </summary>
    public static readonly TimeSpan FreezeDuration = TimeSpan.FromSeconds(8);
    private const int MaxDynamicFields = 3;
    private const int MenuOffset = 30; // below the menu bar
    private static readonly (string Name, string Type, string Size)[] Items =
        [("Alpha", "Text", "1 KB"), ("Beta", "HTML", "2 KB"), ("Gamma", "Markdown", "3 KB"), ("Delta", "Text", "4 KB"), ("Epsilon", "HTML", "5 KB")];

    private readonly TextBox _name;
    private readonly ComboBox _type;
    private readonly CheckBox _enable;
    private readonly Button _advanced;
    private readonly Label _status;
    private readonly Label _events;
    private readonly TabControl _tabs;
    private readonly ListView _list;
    private readonly TreeView _tree;
    private readonly List<string> _eventLog = [];
    private readonly List<Control> _dynamicControls = [];
    private int _dynamicFieldCount;
    private bool _resetting;

    public MainForm(Point? position, int stressCount = 0, int rowCount = 0)
    {
        // Layout below is in 96-DPI units. Declaring that baseline lets WinForms scale positions and sizes with the
        // fonts; without it only fonts scaled and captions were clipped at 150% (seen in M8's screenshots).
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Name = "MainForm";
        Text = "WinMCP Test App";
        ClientSize = new Size(900, 370);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        if (position is { } p)
        {
            StartPosition = FormStartPosition.Manual;
            Location = p;
        }

        // ---- v1: the form ----
        // Each label is added and tab-ordered right before its input so UIA names the input after the label.
        var tab = 0;
        Controls.Add(new Label { Name = "nameLabel", Text = "Name:", Location = new Point(12, MenuOffset + 15), AutoSize = true, TabIndex = tab++ });
        _name = new TextBox { Name = "nameTextBox", Location = new Point(130, MenuOffset + 12), Width = 220, TabIndex = tab++ };
        Controls.Add(_name);

        Controls.Add(new Label { Name = "typeLabel", Text = "Type:", Location = new Point(12, MenuOffset + 45), AutoSize = true, TabIndex = tab++ });
        _type = new ComboBox { Name = "typeComboBox", Location = new Point(130, MenuOffset + 42), Width = 220, DropDownStyle = ComboBoxStyle.DropDownList, TabIndex = tab++ };
        _type.Items.AddRange(["Text", "HTML", "Markdown"]);
        _type.SelectedIndex = 0;
        Controls.Add(_type);

        _enable = new CheckBox { Name = "enableCheckBox", Text = "Enable feature", Location = new Point(130, MenuOffset + 72), AutoSize = true, Checked = true, TabIndex = tab++ };
        Controls.Add(_enable);

        var apply = new Button { Name = "applyButton", Text = "Apply", Location = new Point(130, MenuOffset + 105), Width = 80, TabIndex = tab++ };
        var cancel = new Button { Name = "cancelButton", Text = "Cancel", Location = new Point(215, MenuOffset + 105), Width = 80, TabIndex = tab++ };
        _advanced = new Button { Name = "advancedButton", Text = "Advanced...", Location = new Point(300, MenuOffset + 105), Width = 80, Enabled = false, TabIndex = tab++ };
        var slowApply = new Button { Name = "slowApplyButton", Text = "Slow apply", Location = new Point(130, MenuOffset + 140), Width = 80, TabIndex = tab++ };
        var addField = new Button { Name = "addFieldButton", Text = "Add field", Location = new Point(215, MenuOffset + 140), Width = 80, TabIndex = tab++ };
        // Blocks the UI thread: the app stops pumping messages, exactly like a hung application.
        var freeze = new Button { Name = "freezeButton", Text = "Freeze 8s", Location = new Point(300, MenuOffset + 140), Width = 80, TabIndex = tab++ };
        // v2: opens a modal WinForms dialog.
        var dialog = new Button { Name = "dialogButton", Text = "Dialog...", Location = new Point(385, MenuOffset + 140), Width = 80, TabIndex = tab++ };
        Controls.AddRange([apply, cancel, _advanced, slowApply, addField, freeze, dialog]);

        // Never shown: tests that hidden controls are not exposed or actionable.
        Controls.Add(new TextBox { Name = "hiddenTextBox", Location = new Point(385, MenuOffset + 105), Visible = false });

        _status = new Label { Name = "statusLabel", Text = ReadyStatus, Location = new Point(12, MenuOffset + 280), AutoSize = true };
        _events = new Label { Name = "eventLogLabel", Location = new Point(12, MenuOffset + 305), AutoSize = true, MaximumSize = new Size(876, 0) };
        Controls.Add(_status);
        Controls.Add(_events);

        // ---- v2: tabbed panel ----
        _list = new ListView
        {
            Name = "itemsListView", View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false,
            Dock = DockStyle.Fill, ContextMenuStrip = BuildContextMenu(),
        };
        _list.Columns.Add("Name", 120);
        _list.Columns.Add("Type", 100);
        _list.Columns.Add("Size", 80);
        _list.BeginUpdate();
        foreach (var (itemName, itemType, itemSize) in Items)
            _list.Items.Add(new ListViewItem([itemName, itemType, itemSize]) { Name = itemName });
        for (var i = 1; i <= rowCount; i++)
            _list.Items.Add(new ListViewItem([$"Row {i}", "Text", $"{i} KB"]) { Name = $"Row {i}" });
        _list.EndUpdate();

        _tree = new TreeView { Name = "itemsTreeView", Dock = DockStyle.Fill, HideSelection = false };
        var documents = new TreeNode("Documents") { Name = "Documents" };
        documents.Nodes.Add(new TreeNode("Reports", [new TreeNode("Q1.txt") { Name = "Q1.txt" }, new TreeNode("Q2.txt") { Name = "Q2.txt" }]) { Name = "Reports" });
        documents.Nodes.Add(new TreeNode("Notes", [new TreeNode("todo.txt") { Name = "todo.txt" }]) { Name = "Notes" });
        _tree.Nodes.Add(documents);

        var itemsTab = new TabPage("Items") { Name = "itemsTab" };
        itemsTab.Controls.Add(_list);
        var treeTab = new TabPage("Tree") { Name = "treeTab" };
        treeTab.Controls.Add(_tree);
        var canvasTab = new TabPage("Canvas") { Name = "canvasTab" };
        canvasTab.Controls.Add(new CanvasControl { Name = "canvasPanel", Dock = DockStyle.Fill });
        // Without an explicit accessible name, UIA's label heuristic names the tab control after the preceding label
        // ("Events: 0 []") — observed in M9. Real applications hit this too; see docs/limitations.md.
        _tabs = new TabControl { Name = "detailsTabs", AccessibleName = "Details", Location = new Point(490, MenuOffset + 12), Size = new Size(398, 250) };
        _tabs.TabPages.AddRange([itemsTab, treeTab, canvasTab]);
        Controls.Add(_tabs);

        Controls.Add(BuildMenu()); // added last: docks above everything else

        if (stressCount > 0)
            AddStressPanel(stressCount);

        RenderEventLog();

        _name.TextChanged += (_, _) => { Record("nameTextBox.TextChanged"); UpdateAdvancedEnabled(); };
        _type.SelectedIndexChanged += (_, _) => Record("typeComboBox.SelectedIndexChanged");
        _type.DropDown += (_, _) => Record("typeComboBox.DropDown");
        _type.DropDownClosed += (_, _) => Record("typeComboBox.DropDownClosed");
        _enable.CheckedChanged += (_, _) => { Record("enableCheckBox.CheckedChanged"); UpdateAdvancedEnabled(); };
        _list.SelectedIndexChanged += (_, _) =>
        {
            Record("itemsListView.SelectedIndexChanged");
            if (_list.SelectedItems.Count == 1)
                _status.Text = $"Status: Selected item: {_list.SelectedItems[0].Text}";
        };
        _tree.AfterSelect += (_, e) => { Record("itemsTreeView.AfterSelect"); _status.Text = $"Status: Selected node: {e.Node?.Text}"; };
        _tree.AfterExpand += (_, e) => Record($"itemsTreeView.AfterExpand({e.Node?.Text})");
        _tabs.SelectedIndexChanged += (_, _) => Record($"detailsTabs.SelectedIndexChanged({_tabs.SelectedTab?.Text})");

        apply.Click += (_, _) => Apply();
        cancel.Click += (_, _) => Reset();
        _advanced.Click += (_, _) => _status.Text = AdvancedStatus;
        slowApply.Click += async (_, _) =>
        {
            _status.Text = ApplyingStatus;
            await Task.Delay(SlowApplyDelay);
            Apply();
        };
        addField.Click += (_, _) => AddDynamicField();
        freeze.Click += (_, _) => Thread.Sleep(FreezeDuration);
        dialog.Click += (_, _) => ShowConfirmDialog();

        ResumeLayout(false);
        PerformLayout();
    }

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip { Name = "mainMenu" };
        var file = new ToolStripMenuItem("&File") { Name = "fileMenu" };
        file.DropDownItems.Add(new ToolStripMenuItem("&New", null, (_, _) => { Reset(); _status.Text = "Status: Menu: File > New"; }) { Name = "fileNewMenuItem" });
        var edit = new ToolStripMenuItem("&Edit") { Name = "editMenu" };
        edit.DropDownItems.Add(new ToolStripMenuItem("&Clear name", null, (_, _) => { _name.Text = ""; _status.Text = "Status: Menu: Edit > Clear name"; }) { Name = "editClearNameMenuItem" });
        var help = new ToolStripMenuItem("&Help") { Name = "helpMenu" };
        help.DropDownItems.Add(new ToolStripMenuItem("&About...", null, (_, _) =>
        {
            // A classic Win32 message box (#32770), unlike the WinForms dialog behind the Dialog... button.
            MessageBox.Show(this, "WinMCP Test App v2", "About WinMCP Test App", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _status.Text = "Status: About closed";
        }) { Name = "helpAboutMenuItem" });
        menu.Items.AddRange([file, edit, help]);
        return menu;
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip { Name = "itemsContextMenu" };
        foreach (var action in new[] { "Rename", "Delete" })
            menu.Items.Add(new ToolStripMenuItem(action, null, (_, _) =>
                _status.Text = $"Status: Context: {action} {(_list.SelectedItems.Count == 1 ? _list.SelectedItems[0].Text : "(none)")}") { Name = $"context{action}MenuItem" });
        return menu;
    }

    /// <summary>Modal WinForms dialog. Its result is reported in the status after it closes.</summary>
    private void ShowConfirmDialog()
    {
        using var confirm = new Form
        {
            Name = "ConfirmDialog", Text = "Confirm", FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false,
            StartPosition = FormStartPosition.CenterParent, AutoScaleDimensions = new SizeF(96F, 96F), AutoScaleMode = AutoScaleMode.Dpi,
            ClientSize = new Size(320, 120), ShowInTaskbar = false,
        };
        var note = new TextBox { Name = "dialogNoteTextBox", Location = new Point(80, 20), Width = 220, TabIndex = 1 };
        var ok = new Button { Name = "dialogOkButton", Text = "OK", DialogResult = DialogResult.OK, Location = new Point(140, 75), Width = 75, TabIndex = 2 };
        var dismiss = new Button { Name = "dialogCancelButton", Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(225, 75), Width = 75, TabIndex = 3 };
        confirm.Controls.AddRange([new Label { Name = "dialogNoteLabel", Text = "Note:", Location = new Point(20, 23), AutoSize = true, TabIndex = 0 }, note, ok, dismiss]);
        confirm.AcceptButton = ok;
        confirm.CancelButton = dismiss;

        _status.Text = confirm.ShowDialog(this) == DialogResult.OK ? $"Status: Dialog OK: {note.Text}" : "Status: Dialog cancelled";
    }

    private void AddStressPanel(int count)
    {
        var panel = new FlowLayoutPanel { Name = "stressPanel", Location = new Point(12, MenuOffset + 345), Size = new Size(876, 200), AutoScroll = true };
        panel.SuspendLayout();
        for (var i = 1; i <= count; i++)
            panel.Controls.Add(new Button { Name = $"stressButton{i}", Text = $"Item {i}", Width = 70 });
        panel.ResumeLayout();
        Controls.Add(panel);
        ClientSize = new Size(ClientSize.Width, 585);
    }

    private void Apply() =>
        _status.Text = $"Status: Applied: Name={_name.Text}; Type={_type.SelectedItem}; Feature={(_enable.Checked ? "On" : "Off")}";

    /// <summary>
    /// Restores every input to its default, removes dynamic fields, clears list/tree selection, collapses the tree,
    /// shows the first tab and clears the event log, so tests can start from a known state without relaunching.
    /// </summary>
    private void Reset()
    {
        _resetting = true;
        _name.Text = "";
        _type.SelectedIndex = 0;
        _enable.Checked = true;
        foreach (var control in _dynamicControls)
        {
            Controls.Remove(control);
            control.Dispose();
        }
        _dynamicControls.Clear();
        _dynamicFieldCount = 0;
        _list.SelectedItems.Clear();
        _tree.SelectedNode = null;
        _tree.CollapseAll();
        _tabs.SelectedIndex = 0;
        _resetting = false;

        UpdateAdvancedEnabled();
        _eventLog.Clear();
        RenderEventLog();
        _status.Text = ReadyStatus;
    }

    private void AddDynamicField()
    {
        if (_dynamicFieldCount == MaxDynamicFields) return;

        var index = ++_dynamicFieldCount;
        // Added after the form's one-time autoscaling, so convert 96-DPI layout units explicitly.
        var y = MenuOffset + 175 + (index - 1) * 30;
        var label = new Label { Name = $"dynamicLabel{index}", Text = $"Dynamic {index}:", Location = Scaled(12, y + 3), AutoSize = true };
        var box = new TextBox { Name = $"dynamicTextBox{index}", Location = Scaled(130, y), Width = LogicalToDeviceUnits(220) };
        box.TextChanged += (_, _) => Record($"{box.Name}.TextChanged");
        Controls.Add(label);
        Controls.Add(box);
        _dynamicControls.Add(label);
        _dynamicControls.Add(box);
        Record("addFieldButton.FieldAdded");
    }

    private Point Scaled(int x, int y) => new(LogicalToDeviceUnits(x), LogicalToDeviceUnits(y));

    private void UpdateAdvancedEnabled() => _advanced.Enabled = _enable.Checked && _name.Text.Length > 0;

    private void Record(string eventName)
    {
        if (_resetting) return;
        _eventLog.Add(eventName);
        RenderEventLog();
    }

    private void RenderEventLog() => _events.Text = $"Events: {_eventLog.Count} [{string.Join(", ", _eventLog)}]";

    /// <summary>Paints its own content and exposes nothing to accessibility â€” like many custom MFC CWnd controls.</summary>
    private sealed class CanvasControl : Control
    {
        public CanvasControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Color.White;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.DrawString("Custom drawn: 42", Font, Brushes.DarkBlue, LogicalToDeviceUnits(12), LogicalToDeviceUnits(12));
            e.Graphics.DrawRectangle(Pens.DarkBlue, LogicalToDeviceUnits(10), LogicalToDeviceUnits(40), LogicalToDeviceUnits(120), LogicalToDeviceUnits(60));
        }
    }
}
