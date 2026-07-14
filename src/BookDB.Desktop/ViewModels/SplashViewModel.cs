using System;
using System.Reflection;
using Avalonia.Threading;
using BookDB.Desktop.Localization;
using BookDB.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BookDB.Desktop.ViewModels;

public partial class SplashViewModel : ObservableObject
{
    private readonly IStartupProgressReporter _reporter;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string _statusText = Resources.Splash_Status_Initializing;

    public SplashViewModel(IStartupProgressReporter reporter)
    {
        _reporter = reporter;
        _reporter.ProgressChanged += OnProgressChanged;
    }

    public string AppName => Resources.About_AppName;

    public string VersionText
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version != null
                ? $"{version.Major}.{version.Minor}.{version.Build}"
                : string.Empty;
        }
    }

    private void OnProgressChanged(ProgressUpdate<StartupStage> report)
    {
        // The reporter may raise on a background thread (migrations run via Task.Run);
        // marshal the property updates onto the UI thread.
        Dispatcher.UIThread.Post(() =>
        {
            ProgressValue = SplashProgressMath.ToPercent(report.Step, report.Current, report.Total);
            StatusText = DescribeStage(report);
        });

        // Startup is complete once Finishing is reported — stop listening so the singleton
        // reporter does not keep this transient ViewModel alive after the splash closes.
        if (report.Step == StartupStage.Finishing)
            _reporter.ProgressChanged -= OnProgressChanged;
    }

    private static string DescribeStage(ProgressUpdate<StartupStage> report) => report.Step switch
    {
        StartupStage.Initializing => Resources.Splash_Status_Initializing,
        StartupStage.ApplyingMigrations => report.Total > 0
            ? string.Format(Resources.Splash_Status_ApplyingMigrationsCount, report.Current, report.Total)
            : Resources.Splash_Status_ApplyingMigrations,
        StartupStage.LoadingLibrary => Resources.Splash_Status_LoadingLibrary,
        StartupStage.RestoringSession => Resources.Splash_Status_RestoringSession,
        StartupStage.Finishing => Resources.Splash_Status_Finishing,
        _ => Resources.Splash_Status_Initializing
    };
}
