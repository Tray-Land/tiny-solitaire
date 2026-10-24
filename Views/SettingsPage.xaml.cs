using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TinySolitaire.Services;
using Windows.ApplicationModel;

namespace TinySolitaire.Views;

public sealed partial class SettingsPage : Page
{
    private readonly Action _goBack;
    private bool _loading = true;

    public SettingsPage(Action goBack)
    {
        _goBack = goBack;
        InitializeComponent();
        DrawChoice.SelectedIndex = SettingsService.DrawCount == 3 ? 1 : 0;
        CardChoice.ItemsSource = CardVisual.StyleNames;
        CardChoice.SelectedIndex = SettingsService.CardStyle;
        ThemeChoice.SelectedIndex = (int)SettingsService.Theme;
        RenderPreview();
        _loading = false;
    }

    public async Task OnShownAsync()
    {
        BackButton.Focus(FocusState.Programmatic);
        ShowError();
        ShowStartupState(await StartupService.GetStateAsync());
    }

    private void ShowStartupState(StartupTaskState? state)
    {
        _loading = true;
        StartupPanel.Visibility = state is null ? Visibility.Collapsed : Visibility.Visible;
        StartupToggle.IsOn = state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        StartupToggle.IsEnabled = state is StartupTaskState.Enabled or StartupTaskState.Disabled;
        StartupDescription.Text = state switch
        {
            StartupTaskState.DisabledByUser => "Turn it on in Windows Settings > Apps > Startup.",
            StartupTaskState.DisabledByPolicy or StartupTaskState.EnabledByPolicy => "Managed by your organization.",
            _ => "Keep a little game ready in your tray after sign-in.",
        };
        _loading = false;
    }

    private void RenderPreview()
    {
        CardPreview.Children.Clear();
        for (int i = 0; i < 4; i++)
        {
            Border card = CardVisual.Back(SettingsService.CardStyle);
            CardPreview.Children.Add(card);
        }
    }

    private void ShowError()
    {
        SettingsError.Message = SettingsService.Error ?? "";
        SettingsError.IsOpen = SettingsService.Error is not null;
    }

    private void Draw_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        SettingsService.DrawCount = DrawChoice.SelectedIndex == 1 ? 3 : 1;
        ShowError();
    }
    private void Card_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        SettingsService.CardStyle = CardChoice.SelectedIndex;
        RenderPreview();
        ShowError();
    }
    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        SettingsService.Theme = (AppTheme)ThemeChoice.SelectedIndex;
        App.Current.ApplyTheme();
        ShowError();
    }
    private async void Startup_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        ShowStartupState(await StartupService.SetEnabledAsync(StartupToggle.IsOn));
    }
    private void Back_Click(object sender, RoutedEventArgs e) => _goBack();
}
