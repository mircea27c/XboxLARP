namespace XboxLARP;

/// <summary>Read-only reference screen showing what every configured controller chord does -
/// generated live from controller.config.json, so it can never drift out of sync with what
/// the app actually does.</summary>
public sealed class BindingsForm : Form
{
    private const int Pad = 20;
    private const int FormWidth = 620;

    public BindingsForm(ControllerConfig config)
    {
        Text = "Controller Layout";
        Icon = AppIcon.Get();
        AutoScaleMode = AutoScaleMode.Font;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);

        BuildUi(config);
    }

    private void BuildUi(ControllerConfig config)
    {
        int contentWidth = FormWidth - 2 * Pad;

        var info = new Label
        {
            Text = "Hold the modifier button(s), then press the trigger button. Bindings are editable in controller.config.json.",
            Left = Pad,
            Top = Pad,
            Width = contentWidth,
            Height = 40,
        };
        Controls.Add(info);

        var list = new ListView
        {
            Left = Pad,
            Top = Pad + info.Height + 6,
            Width = contentWidth,
            Height = 320,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
        };
        list.Columns.Add("Chord", 220);
        list.Columns.Add("Does", contentWidth - 224);

        foreach (var binding in config.Bindings)
        {
            string chord = string.Join(" + ", binding.Modifiers.Select(FriendlyButtonName).Append(FriendlyButtonName(binding.Trigger)));
            string description = DescribeAction(binding.Action);
            list.Items.Add(new ListViewItem([chord, description]));
        }
        Controls.Add(list);

        var closeButton = new Button { Text = "Close", Left = Pad + contentWidth - 100, Top = list.Top + list.Height + 14, Width = 100, Height = 34 };
        closeButton.Click += (_, _) => Close();
        Controls.Add(closeButton);

        ClientSize = new Size(FormWidth, closeButton.Top + closeButton.Height + Pad);
    }

    private static string FriendlyButtonName(string button) => button switch
    {
        "Guide" => "Xbox Button",
        "Start" => "Start",
        "Back" => "Back/View",
        "LeftShoulder" => "LB",
        "RightShoulder" => "RB",
        "LeftThumb" => "L3 (Left Stick Click)",
        "RightThumb" => "R3 (Right Stick Click)",
        "DPadUp" => "D-Pad Up",
        "DPadDown" => "D-Pad Down",
        "DPadLeft" => "D-Pad Left",
        "DPadRight" => "D-Pad Right",
        _ => button, // A, B, X, Y already read fine as-is
    };

    private static string DescribeAction(string action) => action switch
    {
        "EnterGameMode" => "Enter Game Mode: switch to the game monitor and launch Playnite Fullscreen",
        "CloseGameMode" => "Close Game Mode: close Playnite and restore your normal monitors",
        "NavUp" => "Send the Up arrow key",
        "NavDown" => "Send the Down arrow key",
        "NavLeft" => "Send the Left arrow key",
        "NavRight" => "Send the Right arrow key",
        "NavEnter" => "Send the Enter key",
        "NavEsc" => "Send the Escape key",
        "NavAltTabForward" => "Alt+Tab - tap repeatedly to cycle forward, release the modifier to select",
        "NavAltTabBackward" => "Alt+Shift+Tab - tap repeatedly to cycle backward, release the modifier to select",
        _ => action,
    };
}
