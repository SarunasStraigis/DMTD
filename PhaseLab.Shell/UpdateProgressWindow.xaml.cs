using System.Windows;

namespace PhaseLab.Shell;

public partial class UpdateProgressWindow : Window
{
    public UpdateProgressWindow(Window? owner)
    {
        InitializeComponent();

        if (owner is not null)
        {
            Owner = owner;
        }
    }

    public void SetProgress(int percent)
    {
        var clamped = Math.Clamp(percent, 0, 100);
        DownloadProgress.IsIndeterminate = false;
        DownloadProgress.Value = clamped;
        PercentText.Text = $"{clamped}%";
    }

    public void SetInstalling()
    {
        StatusText.Text = "Installing update...";
        DownloadProgress.IsIndeterminate = true;
        PercentText.Text = string.Empty;
    }
}
