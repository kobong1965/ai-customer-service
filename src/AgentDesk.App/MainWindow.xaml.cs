using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Net.Http;
using AgentDesk.App.ViewModels;
using AgentDesk.Automation;
using AgentDesk.Core;
using AgentDesk.Infrastructure;
using AgentDesk.Infrastructure.Updates;

namespace AgentDesk.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        var approvalStore = new FileSimulationApprovalStore();
        var approvalService = new SimulationApprovalService(approvalStore);
        _viewModel = new MainWindowViewModel(
            approvalService,
            new FileAppSettingsStore(),
            new WindowsCredentialSecretStore(),
            new WindowsPlatformAutomation(),
            new FileRunEventStore(),
            new FileKnowledgeStore(),
            new FileProductSizingStore(),
            new FileExperienceMemoryStore(),
            new FileAgentSkillStore(),
            new GitHubUpdateService(
                new HttpClient { Timeout = TimeSpan.FromMinutes(15) },
                "kobong1965",
                "ai-customer-service"));
        DataContext = _viewModel;
        _viewModel.SecretInputCleared += (_, _) => ApiKeyBox.Clear();
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await _viewModel.InitializeAsync();
    }

    private void OnApiKeyPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.PasswordBox passwordBox)
        {
            _viewModel.ApiKeyInput = passwordBox.Password;
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.S
            && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
            && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)
            && _viewModel.StopServiceCommand.CanExecute(null))
        {
            _viewModel.StopServiceCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnCalibrationPreviewClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Image image
            || image.Source is not { Width: > 0, Height: > 0 } source
            || image.ActualWidth <= 0
            || image.ActualHeight <= 0)
        {
            return;
        }

        var sourceAspect = source.Width / source.Height;
        var controlAspect = image.ActualWidth / image.ActualHeight;
        var renderedWidth = controlAspect > sourceAspect
            ? image.ActualHeight * sourceAspect
            : image.ActualWidth;
        var renderedHeight = controlAspect > sourceAspect
            ? image.ActualHeight
            : image.ActualWidth / sourceAspect;
        var offsetX = (image.ActualWidth - renderedWidth) / 2;
        var offsetY = (image.ActualHeight - renderedHeight) / 2;
        var point = e.GetPosition(image);
        if (point.X < offsetX || point.X > offsetX + renderedWidth
            || point.Y < offsetY || point.Y > offsetY + renderedHeight)
        {
            return;
        }

        _viewModel.SetCalibrationPoint(
            (point.X - offsetX) / renderedWidth,
            (point.Y - offsetY) / renderedHeight);
    }

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        Closing -= OnClosing;
        await _viewModel.DisposeAsync();
    }
}
