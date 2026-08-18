namespace MikuMikuModel.GUI.Forms;

/// <summary>Displays indeterminate progress while a texture is encoded.</summary>
public sealed class TextureEncodingProgressForm : Form
{
    private readonly Label mLabel = new();
    private readonly ProgressBar mProgressBar = new();

    public TextureEncodingProgressForm(string message)
    {
        Text = "Processing texture";
        // The form is modeless (Show, not ShowDialog), so there is no owner
        // window to center against while the background encoder is running.
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ControlBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(420, 92);

        mLabel.Dock = DockStyle.Top;
        mLabel.Height = 42;
        mLabel.Padding = new Padding(12, 12, 12, 4);
        mLabel.Text = message;

        mProgressBar.Dock = DockStyle.Bottom;
        mProgressBar.Height = 20;
        mProgressBar.Style = ProgressBarStyle.Marquee;
        mProgressBar.MarqueeAnimationSpeed = 25;

        Controls.Add(mProgressBar);
        Controls.Add(mLabel);
    }
}
