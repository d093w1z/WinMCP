namespace WinMcp.TestApp;

/// <summary>
/// TestApp v1. Every control has a stable <see cref="Control.Name"/> (= UIA AutomationId) and every behavior is
/// deterministic. The contract (names, texts, status strings) is relied on by tests — change it deliberately.
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

    private readonly TextBox _name;
    private readonly ComboBox _type;
    private readonly CheckBox _enable;
    private readonly Button _advanced;
    private readonly Label _status;
    private readonly Label _events;
    private readonly List<string> _eventLog = [];
    private readonly List<Control> _dynamicControls = [];
    private int _dynamicFieldCount;
    private bool _resetting;

    public MainForm(Point? position, int stressCount = 0)
    {
        // Layout below is in 96-DPI units. Declaring that baseline lets WinForms scale positions and sizes with the
        // fonts; without it only fonts scaled and captions were clipped at 150% (seen in M8's screenshots).
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Name = "MainForm";
        Text = "WinMCP Test App";
        ClientSize = new Size(480, 340);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        if (position is { } p)
        {
            StartPosition = FormStartPosition.Manual;
            Location = p;
        }

        // Each label is added and tab-ordered right before its input so UIA names the input after the label.
        var tab = 0;
        Controls.Add(new Label { Name = "nameLabel", Text = "Name:", Location = new Point(12, 15), AutoSize = true, TabIndex = tab++ });
        _name = new TextBox { Name = "nameTextBox", Location = new Point(130, 12), Width = 220, TabIndex = tab++ };
        Controls.Add(_name);

        Controls.Add(new Label { Name = "typeLabel", Text = "Type:", Location = new Point(12, 45), AutoSize = true, TabIndex = tab++ });
        _type = new ComboBox { Name = "typeComboBox", Location = new Point(130, 42), Width = 220, DropDownStyle = ComboBoxStyle.DropDownList, TabIndex = tab++ };
        _type.Items.AddRange(["Text", "HTML", "Markdown"]);
        _type.SelectedIndex = 0;
        Controls.Add(_type);

        _enable = new CheckBox { Name = "enableCheckBox", Text = "Enable feature", Location = new Point(130, 72), AutoSize = true, Checked = true, TabIndex = tab++ };
        Controls.Add(_enable);

        var apply = new Button { Name = "applyButton", Text = "Apply", Location = new Point(130, 105), Width = 80, TabIndex = tab++ };
        var cancel = new Button { Name = "cancelButton", Text = "Cancel", Location = new Point(215, 105), Width = 80, TabIndex = tab++ };
        _advanced = new Button { Name = "advancedButton", Text = "Advanced...", Location = new Point(300, 105), Width = 80, Enabled = false, TabIndex = tab++ };
        var slowApply = new Button { Name = "slowApplyButton", Text = "Slow apply", Location = new Point(130, 140), Width = 80, TabIndex = tab++ };
        var addField = new Button { Name = "addFieldButton", Text = "Add field", Location = new Point(215, 140), Width = 80, TabIndex = tab++ };
        // Blocks the UI thread: the app stops pumping messages, exactly like a hung application.
        var freeze = new Button { Name = "freezeButton", Text = "Freeze 8s", Location = new Point(300, 140), Width = 80, TabIndex = tab++ };
        Controls.AddRange([apply, cancel, _advanced, slowApply, addField, freeze]);

        // Never shown: tests that hidden controls are not exposed or actionable.
        Controls.Add(new TextBox { Name = "hiddenTextBox", Location = new Point(385, 140), Visible = false });

        if (stressCount > 0)
            AddStressPanel(stressCount);

        _status = new Label { Name = "statusLabel", Text = ReadyStatus, Location = new Point(12, 280), AutoSize = true };
        _events = new Label { Name = "eventLogLabel", Location = new Point(12, 305), AutoSize = true, MaximumSize = new Size(456, 0) };
        Controls.Add(_status);
        Controls.Add(_events);
        RenderEventLog();

        _name.TextChanged += (_, _) => { Record("nameTextBox.TextChanged"); UpdateAdvancedEnabled(); };
        _type.SelectedIndexChanged += (_, _) => Record("typeComboBox.SelectedIndexChanged");
        _type.DropDown += (_, _) => Record("typeComboBox.DropDown");
        _type.DropDownClosed += (_, _) => Record("typeComboBox.DropDownClosed");
        _enable.CheckedChanged += (_, _) => { Record("enableCheckBox.CheckedChanged"); UpdateAdvancedEnabled(); };

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

        ResumeLayout(false);
        PerformLayout();
    }

    private void AddStressPanel(int count)
    {
        var panel = new FlowLayoutPanel { Name = "stressPanel", Location = new Point(12, 345), Size = new Size(456, 200), AutoScroll = true };
        panel.SuspendLayout();
        for (var i = 1; i <= count; i++)
            panel.Controls.Add(new Button { Name = $"stressButton{i}", Text = $"Item {i}", Width = 70 });
        panel.ResumeLayout();
        Controls.Add(panel);
        ClientSize = new Size(ClientSize.Width, 555);
    }

    private void Apply() =>
        _status.Text = $"Status: Applied: Name={_name.Text}; Type={_type.SelectedItem}; Feature={(_enable.Checked ? "On" : "Off")}";

    /// <summary>
    /// Restores every input to its default, removes dynamic fields and clears the event log,
    /// so tests can start from a known state without relaunching.
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
        var y = 175 + (index - 1) * 30;
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
}
