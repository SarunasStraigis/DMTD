using System.Windows;
using Velopack;
using Velopack.Sources;

namespace PhaseLab.Shell.Services;

public static class UpdateService
{
    private const string GitHubRepoUrl = "https://github.com/SarunasStraigis/DMTD";

    public static async Task CheckForUpdatesAsync()
    {
        try
        {
            var source = new GithubSource(GitHubRepoUrl, accessToken: null, prerelease: false);
            var updateManager = new UpdateManager(source);

            if (!updateManager.IsInstalled)
            {
                return;
            }

            var update = await updateManager.CheckForUpdatesAsync();
            if (update is null)
            {
                return;
            }

            var version = update.TargetFullRelease.Version.ToString();
            var owner = Application.Current?.MainWindow;
            var result = MessageBox.Show(
                owner,
                $"A new version of PhaseLab is available ({version}).\n\nDownload and install now? The app will restart.",
                "Update available",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            var progressWindow = new UpdateProgressWindow(owner);
            progressWindow.Show();

            try
            {
                await updateManager.DownloadUpdatesAsync(update, progress =>
                {
                    progressWindow.Dispatcher.Invoke(() => progressWindow.SetProgress(progress));
                });

                progressWindow.SetInstalling();
                updateManager.ApplyUpdatesAndRestart(update);
            }
            finally
            {
                progressWindow.Close();
            }
        }
        catch
        {
            // Ignore update failures — the app should still run offline.
        }
    }
}
