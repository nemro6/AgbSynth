using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AgbSynth.App.ViewModels;

namespace AgbSynth.App.Views.Mixer;

public partial class MixerView : UserControl
{
    public MixerView()
    {
        InitializeComponent();
    }

    private void OnMixerMutePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: AgbMixerStrip strip })
            return;

        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsRightButtonPressed)
        {
            if (DataContext is MainWindowViewModel viewModel)
                viewModel.ToggleMixerSolo(strip.Channel);
            e.Handled = true;
            return;
        }

        if (point.Properties.IsLeftButtonPressed)
        {
            strip.IsMuted = !strip.IsMuted;
            e.Handled = true;
        }
    }

    private void OnMixerAlertClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: AgbMixerStrip strip } &&
            DataContext is MainWindowViewModel viewModel)
        {
            viewModel.DismissMixerAlert(strip);
        }
    }
}
