# Zenit — Agent Guide

Desktop app built with **Avalonia UI 11** and **.NET 10**, using **MVVM** with CommunityToolkit.Mvvm.

I used to use WinUI, now I use Avalonia. Remove any references to WinUI and keep the code simple and high-quality.

## Project structure

```
zenit/
├── App.axaml(.cs)              # App entry, auth gate on startup
├── Program.cs                  # Avalonia host
├── ViewLocator.cs              # ViewModels → Views convention
├── Zenit.csproj                # Single project (no separate Core library)
│
├── Models/                     # Domain models and DTOs
│   ├── Entities/               # EF entities (e.g. TokenEntity)
│   ├── CustomReports/
│   ├── SalaryPlans/
│   └── Vendedores/
│
├── Services/                   # All application services
│   └── SalaryPlans/
│
├── Infrastructure/             # Auth, Power BI, persistence, WhatsApp
│   ├── Auth/
│   ├── Persistence/
│   ├── PowerBi/
│   ├── Configuration/
│   └── Logging/
│
├── Data/                       # EF DbContext
├── Contracts/Services/         # Service interfaces
├── Helpers/
├── Mappers/
│
├── ViewModels/
│   ├── ViewModelBase.cs
│   ├── MainWindowViewModel.cs
│   └── ...
│
└── Views/
    ├── MainWindow.axaml
    └── ...
```

## Architecture rules

### MVVM

- **Views** (`*.axaml`): layout and bindings only — no business logic
- **ViewModels**: state, commands (`RelayCommand`), and navigation
- **Services** (`Services/`): side effects and persistence (e.g. `TokenManager`, API clients)
- Use `x:DataType` on views for compiled bindings

### View ↔ ViewModel mapping

`ViewLocator` resolves views by convention:

| ViewModel | View |
|-----------|------|
| `ViewModels.HomeViewModel` | `Views.HomeView` |
| `ViewModels.ReportsViewModel` | `Views.ReportsView` |

Pattern: replace `.ViewModels.` → `.Views.` and strip the `ViewModel` suffix.

When adding a page:

1. Create `ViewModels/FooViewModel.cs` extending `ViewModelBase`
2. Create `Views/FooView.axaml` + code-behind
3. Add a `NavigateFooCommand` in `MainWindowViewModel`
4. Add a sidebar button bound to that command

### Navigation

- `MainWindowViewModel.CurrentPage` holds the active page ViewModel
- `MainWindow` uses `<ContentControl Content="{Binding CurrentPage}"/>` + `ViewLocator`
- Do **not** use code-behind click handlers for navigation

### Sidebar

- `MainWindowViewModel.IsSidebarOpen` controls expanded (220px) vs collapsed (64px) width
- `ToggleSidebarCommand` toggles the sidebar from the ☰ button
- Collapsed mode shows icon/letter shortcuts with tooltips for each nav item

### Authentication flow

```
App startup
  ├─ token valid? → MainWindow
  └─ no token     → Auth (login window)
        └─ success → MainWindow, close Auth

Logout (sidebar)
  └─ clear token → Auth window
```

### Naming

- ViewModels: `{Name}ViewModel`
- Views: `{Name}View` (UserControl/Window)
- Services: `{Name}Service` under `Services/`
- Interfaces: `I{Name}Service` under `Contracts/Services/`

## Adding a new module (checklist)

- [ ] `ViewModels/{Name}ViewModel.cs`
- [ ] `Views/{Name}View.axaml` with `x:DataType`
- [ ] `[RelayCommand] Navigate{Name}()` in `MainWindowViewModel`
- [ ] Sidebar button in `MainWindow.axaml`
- [ ] Wire up in `Infrastructure/AppBootstrapper.cs`
- [ ] Verify ViewLocator resolves the pair (`dotnet build`)

## Dependencies

- `Avalonia` 11.x (Fluent theme)
- `CommunityToolkit.Mvvm` (ObservableObject, RelayCommand)
- `Microsoft.Identity.Client` (Power BI auth)
- `Npgsql.EntityFrameworkCore.PostgreSQL` (token persistence)
- `QuestPDF` (PDF export)

## Out of scope (for now)

- Real backend / OAuth
- DI container (construct services in `AppBootstrapper` until needed)
- Unit tests

Keep changes minimal and follow existing folder conventions.
