using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Zenit.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public ObservableCollection<NavigationItem> NavigationItems { get; } = new();

    [ObservableProperty] private NavigationItem? selectedPage;
    [ObservableProperty] private object? currentPage;
    [ObservableProperty] private bool isPageLoading;

    public string WindowTitle => "Zenit";
    public string CurrentPageTitle => SelectedPage?.Title ?? "Zenit";
    public string CurrentPageSubtitle => SelectedPage?.Subtitle ?? "Migracion de Lemon a Avalonia";

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
        OnPropertyChanged(nameof(CurrentPageSubtitle));
        _ = ActivateSelectedPageAsync();
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
    public required string Subtitle { get; init; }
    public required object ViewModel { get; init; }
    public required Func<Task> ActivateAsync { get; init; }
}
