# WorkTracker WPF - Modern Time Tracking Application

A modern, production-ready WPF desktop application for tracking work time and submitting worklogs to Tempo/Jira.

## Features

### Core Functionality
- **Start/Stop Work Tracking**: Quickly start and stop work tracking with optional Jira ticket ID and description
- **Smart Input Parsing**: Automatically detects Jira ticket codes (e.g., PROJ-123) from input
- **Real-time Timer**: Live elapsed time display for active work entries
- **Work Entry Management**: View, edit, and delete work entries
- **Date-based Filtering**: Browse work entries by date with "Go to today" navigation
- **Tempo Integration**: Send worklogs to Tempo (daily or weekly) with failed worklog resubmission
- **Favorite Templates**: Save frequent work items as templates for quick start
- **Tray Icon Toggle**: Click system tray icon to show/hide main window

### User Experience
- **Modern Material Design UI**: Clean, professional interface using MaterialDesignThemes
- **Real-time Updates**: Work entry list refreshes automatically
- **Visual Feedback**: Color-coded status indicators (Active/Completed)
- **Toast Notifications**: Non-intrusive success/error messages
- **Validation**: Input validation with clear error messages
- **Responsive Layout**: Adapts to different screen sizes

## Architecture

### MVVM Pattern
The application strictly follows the MVVM (Model-View-ViewModel) pattern for clean separation of concerns:

- **Models**: Domain entities from WorkTracker.Domain
- **Views**: XAML files in `Views/` folder
- **ViewModels**: Business logic in `ViewModels/` folder

### Project Structure

```
WorkTracker.WPF/
├── Commands/               # ICommand implementations
│   ├── RelayCommand.cs            # Synchronous commands
│   └── AsyncRelayCommand.cs       # Async commands with proper await handling
│
├── Converters/            # Value converters for data binding
│   ├── TimeSpanToStringConverter.cs     # Format TimeSpan as "2h 30m"
│   ├── DateTimeToStringConverter.cs      # Format DateTime with custom formats
│   ├── BoolToVisibilityConverter.cs      # Bool to Visibility conversion
│   └── NullToVisibilityConverter.cs      # Null checking to Visibility
│
├── Resources/             # Resource dictionaries
│   ├── Colors.xaml                # Color definitions and brushes
│   └── Styles.xaml                # Reusable styles for controls
│
├── Services/              # Application services
│   ├── IDialogService.cs          # Dialog abstraction interface
│   ├── DialogService.cs           # WPF dialog implementation
│   ├── INotificationService.cs    # Notification abstraction
│   └── NotificationService.cs     # Toast notification implementation
│
├── ViewModels/            # ViewModels (business logic)
│   ├── ViewModelBase.cs           # Base class with INotifyPropertyChanged
│   ├── MainViewModel.cs           # Main window logic
│   ├── WorkEntryEditViewModel.cs  # Edit dialog logic
│   └── SendWorklogViewModel.cs    # Tempo submission logic
│
├── Views/                 # XAML views
│   ├── MainWindow.xaml            # Main application window
│   ├── WorkEntryEditDialog.xaml   # Edit work entry dialog
│   └── SendWorklogDialog.xaml     # Send to Tempo dialog
│
├── App.xaml               # Application resources
├── App.xaml.cs            # Dependency injection setup
└── appsettings.json       # Configuration file
```

## Key Design Patterns & Best Practices

### 1. MVVM Architecture
- **ViewModels** contain all business logic and are completely testable
- **Views** only contain XAML and minimal code-behind (DI setup only)
- **Data Binding** used exclusively for UI updates (no direct manipulation)

### 2. Dependency Injection
- Uses `Microsoft.Extensions.DependencyInjection` for IoC
- All services registered in `App.xaml.cs`
- ViewModels receive dependencies via constructor injection

### 3. Async/Await Patterns
- `AsyncRelayCommand` prevents concurrent execution
- Proper exception handling in all async operations
- UI thread marshaling handled automatically by WPF

### 4. Memory Management
- `INotifyPropertyChanged` implemented via `ViewModelBase`
- Proper use of `ObservableCollection` for dynamic lists
- Timer cleanup in MainViewModel

### 5. Separation of Concerns
- **DialogService**: Abstracts dialog creation from ViewModels
- **NotificationService**: Centralizes toast notifications
- **Converters**: Reusable data transformation logic

## How to Run

### Prerequisites
- .NET 10.0 SDK or later
- Windows 10/11

### Build & Run
```bash
cd src/WorkTracker.WPF
dotnet build
dotnet run
```

### Configuration
Edit `appsettings.json` to configure:
- Database path
- Jira API credentials
- Tempo API credentials

## UI Components

### Main Window
**Left Panel:**
- Active work card with real-time timer
- Start work input with smart Jira code detection
- Quick actions (Send to Tempo, Refresh)

**Right Panel:**
- Work entries DataGrid with sorting
- Date picker for filtering
- Inline edit/delete actions

### Work Entry Edit Dialog
- Ticket ID and description fields
- Start date/time pickers
- Optional end date/time
- Real-time validation

### Send Worklog Dialog
- Preview of entries to be sent
- Daily or weekly submission mode
- Progress indicator during submission
- Result summary

## Technical Highlights

### Smart Input Parsing
```csharp
// Automatically detects: "PROJ-123 Working on feature"
// TicketId: "PROJ-123"
// Description: "Working on feature"
```

### Real-time Timer
```csharp
// Updates every second via DispatcherTimer
// Format: "02:35:47" (hours:minutes:seconds)
```

### Material Design Integration
- MaterialDesignThemes NuGet package
- Modern card-based layout
- Elevation and shadows
- Color-coded status indicators

## Testing

The application is designed with testability in mind:
- ViewModels are fully testable (no WPF dependencies)
- Services use interface abstraction
- Business logic separated from UI logic

## Performance Considerations

1. **UI Virtualization**: DataGrid uses virtualization for large lists
2. **Binding Optimization**: OneWay bindings where appropriate
3. **Resource Freezing**: Static resources defined once
4. **Async Operations**: Long-running tasks don't block UI

## Future Enhancements

Potential improvements:
- Keyboard shortcuts (Ctrl+N, Ctrl+S, etc.)
- Export to CSV/Excel
- Statistics and reporting
- Multiple work session support
- Offline mode with sync

## Dependencies

### NuGet Packages
- **MaterialDesignThemes** (5.3.1): Modern UI components
- **MaterialDesignColors** (5.3.1): Color palette
- **CommunityToolkit.Mvvm** (8.4.2): MVVM helpers
- **Hardcodet.NotifyIcon.Wpf** (2.0.1): System tray icon
- **FontAwesome6.Svg** (2.5.1): Icon set
- **Microsoft.Extensions.Hosting** (10.0.5): Dependency injection
- **Microsoft.Extensions.Configuration.Json** (10.0.5): Configuration
- **Microsoft.Extensions.Logging** (10.0.5): Logging

### Project References
- **WorkTracker.UI.Shared**: Shared models, services, orchestrators
- **WorkTracker.Application**: Business logic and services
- **WorkTracker.Infrastructure**: Data access and API clients

## Code Style

The codebase follows WPF and C# best practices:
- XML documentation comments for public APIs
- Meaningful naming conventions
- Async/await patterns
- Null safety (nullable reference types enabled)
- Resource localization ready

## License

Part of the WorkTracker project.

## Support

For issues or questions, please refer to the main WorkTracker documentation.
