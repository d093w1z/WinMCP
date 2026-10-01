using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace UiaSpike;

/// <summary>Scratch stand-in for the future TestApp: same core controls, plus a button that hangs the UI thread.</summary>
internal sealed class SpikeForm : Form
{
    private readonly TextBox _name;
    private readonly ComboBox _type;
    private readonly CheckBox _enable;
    private readonly Label _status;
    private readonly Label _events;
    private readonly System.Collections.Generic.List<string> _eventNames = new();
    private int _eventCount;

    public static void Run()
    {
        var thread = new Thread(() =>
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.Run(new SpikeForm());
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    private SpikeForm()
    {
        Name = "MainForm";
        Text = "WinMCP Spike Form";
        StartPosition = FormStartPosition.Manual;
        Location = new Point(100, 100);
        ClientSize = new Size(470, 250);
        AutoScaleMode = AutoScaleMode.Dpi;

        // Labels are added (and tab-ordered) immediately before their inputs so label heuristics can be observed.
        var tab = 0;
        Controls.Add(new Label { Name = "nameLabel", Text = "Name:", Location = new Point(12, 15), AutoSize = true, TabIndex = tab++ });
        _name = new TextBox { Name = "nameTextBox", Location = new Point(130, 12), Width = 200, TabIndex = tab++ };
        Controls.Add(_name);

        Controls.Add(new Label { Name = "typeLabel", Text = "Type:", Location = new Point(12, 45), AutoSize = true, TabIndex = tab++ });
        _type = new ComboBox { Name = "typeComboBox", Location = new Point(130, 42), Width = 200, DropDownStyle = ComboBoxStyle.DropDownList, TabIndex = tab++ };
        _type.Items.AddRange(new object[] { "Text", "HTML", "Markdown" });
        _type.SelectedIndex = 0;
        Controls.Add(_type);

        _enable = new CheckBox { Name = "enableCheckBox", Text = "Enable feature", Location = new Point(130, 72), AutoSize = true, Checked = true, TabIndex = tab++ };
        Controls.Add(_enable);

        var apply = new Button { Name = "applyButton", Text = "Apply", Location = new Point(130, 105), TabIndex = tab++ };
        var cancel = new Button { Name = "cancelButton", Text = "Cancel", Location = new Point(215, 105), TabIndex = tab++ };
        var advanced = new Button { Name = "advancedButton", Text = "Advanced…", Location = new Point(300, 105), Enabled = false, TabIndex = tab++ };
        var hang = new Button { Name = "hangButton", Text = "Hang 8s", Location = new Point(130, 140), TabIndex = tab++ };
        Controls.AddRange(new Control[] { apply, cancel, advanced, hang });

        Controls.Add(new TextBox { Name = "hiddenTextBox", Location = new Point(300, 140), Visible = false });

        _status = new Label { Name = "statusLabel", Text = "Status: Ready", Location = new Point(12, 185), AutoSize = true };
        _events = new Label { Name = "eventCounterLabel", Text = "Events: 0", Location = new Point(12, 210), AutoSize = true };
        Controls.Add(_status);
        Controls.Add(_events);

        _name.TextChanged += (_, _) => Bump("TextChanged");
        _type.SelectedIndexChanged += (_, _) => Bump("SelectedIndexChanged");
        _type.DropDown += (_, _) => Bump("DropDown");
        _type.DropDownClosed += (_, _) => Bump("DropDownClosed");
        _enable.CheckedChanged += (_, _) => Bump("CheckedChanged");
        apply.Click += (_, _) =>
            _status.Text = $"Status: Applied: Name={_name.Text}; Type={_type.SelectedItem}; Feature={(_enable.Checked ? "On" : "Off")}";
        cancel.Click += (_, _) => _status.Text = "Status: Ready";
        advanced.Click += (_, _) => _status.Text = "Status: ADVANCED CLICKED WHILE DISABLED";
        hang.Click += (_, _) => Thread.Sleep(TimeSpan.FromSeconds(8));
    }

    private void Bump(string evt)
    {
        _eventNames.Add(evt);
        _events.Text = $"Events: {++_eventCount} [{string.Join(", ", _eventNames)}]";
    }
}
