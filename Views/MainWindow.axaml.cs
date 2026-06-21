using Avalonia.Controls;
using Zenit.ViewModels;

namespace Zenit.Views;

public partial class MainWindow : Window
{
    private const double OverlayNavigationBreakpoint = 1260;

    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        SizeChanged += OnSizeChanged;
    }

    private void OnOpened(object? sender, System.EventArgs e)
    {
        UpdateNavigationDisplayMode(ClientSize.Width);
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged)
            return;

        UpdateNavigationDisplayMode(e.NewSize.Width);
    }

    private void UpdateNavigationDisplayMode(double width)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        viewModel.SetNavigationDisplayMode(
            width < OverlayNavigationBreakpoint
                ? SplitViewDisplayMode.Overlay
                : SplitViewDisplayMode.Inline);
    }
}
