using Microsoft.UI.Xaml;
using QuestPDF.Infrastructure;

namespace PensionCompass;

public partial class App : Application
{
    public static Window? Window { get; private set; }

    public App()
    {
        // QuestPDF requires this declaration before any document generation.
        // Community license = free for individuals / small companies; see https://www.questpdf.com/license/
        QuestPDF.Settings.License = LicenseType.Community;

        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Window = new MainWindow();
        Window.Activate();
    }
}
