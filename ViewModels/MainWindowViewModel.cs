using System.Collections.ObjectModel;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Zenit.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public ObservableCollection<NavigationItem> NavigationItems { get; } = new();

    [ObservableProperty] private NavigationItem? selectedPage;
    [ObservableProperty] private object? currentPage;
    [ObservableProperty] private bool isPageLoading;
    [ObservableProperty] private bool isNavigationPaneOpen = true;
    [ObservableProperty] private SplitViewDisplayMode navigationDisplayMode = SplitViewDisplayMode.Inline;

    public string WindowTitle => "Zenit";
    public string CurrentPageTitle => SelectedPage?.Title ?? "Zenit";
    
    public MainWindowViewModel(IEnumerable<NavigationItem> pages)
    {
        foreach (var page in pages)
            NavigationItems.Add(page);

        SelectedPage = NavigationItems.FirstOrDefault();
    }

    partial void OnSelectedPageChanged(NavigationItem? value)
    {
        CurrentPage = value?.ViewModel;
        OnPropertyChanged(nameof(CurrentPageTitle));

        if (NavigationDisplayMode is SplitViewDisplayMode.Overlay or SplitViewDisplayMode.CompactOverlay)
            IsNavigationPaneOpen = false;

        _ = ActivateSelectedPageAsync();
    }

    [RelayCommand]
    private void ToggleNavigationPane()
    {
        IsNavigationPaneOpen = !IsNavigationPaneOpen;
    }

    public void SetNavigationDisplayMode(SplitViewDisplayMode displayMode)
    {
        if (NavigationDisplayMode == displayMode)
            return;

        NavigationDisplayMode = displayMode;

        if (displayMode is SplitViewDisplayMode.Overlay or SplitViewDisplayMode.CompactOverlay)
            IsNavigationPaneOpen = false;
    }

    public async Task ActivateSelectedPageAsync()
    {
        if (SelectedPage == null)
            return;

        try
        {
            IsPageLoading = true;
            await SelectedPage.ActivateAsync();
        }
        finally
        {
            IsPageLoading = false;
        }
    }
}

public sealed class NavigationItem
{
    public required string Title { get; init; }
    public required object ViewModel { get; init; }
    public required Func<Task> ActivateAsync { get; init; }
}
