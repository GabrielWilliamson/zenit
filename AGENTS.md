# Cappuccino — Agent Guide

Desktop app built with **Avalonia UI 11** and **.NET 10**, using **MVVM** with CommunityToolkit.Mvvm.

## Goal

Simple authenticated desktop shell:

1. **Login** — user must sign in before accessing the app
2. **Shell** — main window with a **collapsible sidebar** for navigation
3. **Pages** — swap content area between Home, Plans, and Reports

## Run the app

```bash
dotnet restore
dotnet run
```

Demo credentials: `jonh` / `12345`

Session token is persisted in `.cappuccino_token` (20 min expiry).

## Project structure

```
cappuccino/
├── App.axaml(.cs)              # App entry, auth gate on startup
├── Program.cs                  # Avalonia host
├── ViewLocator.cs              # ViewModels → Views convention
│
├── Services/
│   └── TokenService.cs         # Local auth token persistence
│
├── ViewModels/
│   ├── ViewModelBase.cs        # ObservableObject base
│   ├── MainWindowViewModel.cs  # Shell: navigation, sidebar toggle, logout
│   ├── Core/
│   │   └── LoginViewModel.cs   # Login form + validation
│   ├── Pages/
│   │   └── HomeViewModel.cs    # Home page
│   └── Modules/
│       ├── Plans/PlansViewModel.cs
│       └── Reports/ReportsViewModel.cs
│
└── Views/
    ├── MainWindow.axaml        # Shell: collapsible sidebar + ContentControl
    ├── Core/
    │   └── Auth.axaml          # Login window
    ├── Pages/
    │   └── Home.axaml
    └── Modules/
        ├── Plans/Plans.axaml
        └── Reports/Reports.axaml
```

## Architecture rules

### MVVM

- **Views** (`*.axaml`): layout and bindings only — no business logic
- **ViewModels**: state, commands (`RelayCommand`), and navigation
- **Services** (`Services/`): side effects and persistence (e.g. `TokenService`, future API clients)
- Use `x:DataType` on views for compiled bindings

### View ↔ ViewModel mapping

`ViewLocator` resolves views by convention:

| ViewModel | View |
|-----------|------|
| `ViewModels.Pages.HomeViewModel` | `Views.Pages.Home` |
| `ViewModels.Modules.Plans.PlansViewModel` | `Views.Modules.Plans.Plans` |
| `ViewModels.Modules.Reports.ReportsViewModel` | `Views.Modules.Reports.Reports` |

Pattern: replace `.ViewModels.` → `.Views.` and strip the `ViewModel` suffix.

When adding a page:

1. Create `ViewModels/.../FooViewModel.cs` extending `ViewModelBase`
2. Create `Views/.../Foo.axaml` + code-behind
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
- Views (UserControl/Window): `{Name}` (no `View` suffix)
- Services: `{Name}Service` under `Services/`
- Modules live under `Modules/{Feature}/`
- Shared/core screens under `Core/` or `Pages/`

## Adding a new module (checklist)

- [ ] `ViewModels/Modules/{Name}/{Name}ViewModel.cs`
- [ ] `Views/Modules/{Name}/{Name}.axaml` with `x:DataType`
- [ ] `[RelayCommand] Navigate{Name}()` in `MainWindowViewModel`
- [ ] Sidebar button in `MainWindow.axaml`
- [ ] Verify ViewLocator resolves the pair (`dotnet build`)

## Dependencies

- `Avalonia` 11.x (Fluent theme)
- `CommunityToolkit.Mvvm` (ObservableObject, RelayCommand)

## Out of scope (for now)

- Real backend / OAuth
- DI container (construct services in `App.axaml.cs` until needed)
- Unit tests

Keep changes minimal and follow existing folder conventions.
