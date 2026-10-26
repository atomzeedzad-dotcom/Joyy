using System.Diagnostics;
using Joyy.Core;

namespace Joyy.App;

/// <summary>Pick a running game (or type its .exe name) for a new per-game profile.</summary>
internal sealed class AddGameDialog : Form
{
    readonly ComboBox _process = new() { Width = 320, DropDownStyle = ComboBoxStyle.DropDown };
    readonly TextBox _name = new() { Width = 320 };

    public AddGameDialog()
    {
        Text = "Add game profile";
        Icon = Icons.App;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);
        BackColor = Theme.Card;
        ForeColor = Theme.Text;
        Theme.Input(_process);
        Theme.Input(_name);

        foreach (var name in RunningWindowedProcesses())
            _process.Items.Add(name);
        _process.TextChanged += (_, _) => { if (_name.Tag is null) _name.Text = ProcessName; };
        _name.TextChanged += (_, _) => { if (_name.Focused) _name.Tag = "edited"; };

        var ok = Theme.MakeButton("Add", primary: true);
        ok.DialogResult = DialogResult.OK;
        var cancel = Theme.MakeButton("Cancel");
        cancel.DialogResult = DialogResult.Cancel;
        ok.Click += (_, e) =>
        {
            if (ProcessName.Length == 0)
            {
                MessageBox.Show(this, "Enter the game's program name, for example oldgame.exe.", Text);
                DialogResult = DialogResult.None;
            }
        };
        AcceptButton = ok;
        CancelButton = cancel;

        var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        layout.Controls.Add(Theme.Heading("Game program (.exe)", 10f));
        layout.Controls.Add(new Label
        {
            Text = "Start the game first to pick it from the list, or type its .exe name.",
            AutoSize = true, ForeColor = Theme.Muted, MaximumSize = new Size(320, 0),
        });
        layout.Controls.Add(_process);
        var nameHeading = Theme.Heading("Profile name", 10f);
        nameHeading.Margin = new Padding(3, 12, 3, 3);
        layout.Controls.Add(nameHeading);
        layout.Controls.Add(_name);
        var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons);
        Controls.Add(layout);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.DarkTitleBar(this);
    }

    public string ProcessName => ProfileSelector.NormalizeProcessName(_process.Text);

    public string DisplayName => _name.Text.Trim() is { Length: > 0 } n ? n : ProcessName;

    static IEnumerable<string> RunningWindowedProcesses()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.MainWindowHandle != 0 && p.Id != Environment.ProcessId)
                    names.Add(p.ProcessName);
            }
            catch (InvalidOperationException) { } // exited while we looked
            finally { p.Dispose(); }
        }
        return names;
    }
}
