using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace CrmMes.Desktop;

/// <summary>The hand-painted production planning board: rows are machines/projects being tracked at a
/// coarse weekly level (see PlanningProject), columns are weeks, and an Admin can drag across a row to
/// paint a chosen category onto those weeks. Everyone else sees the same board read-only — no separate
/// password gate, just this app's existing role system (see PlanningBoardController). Not built as a
/// MainWindow tab: it's a full custom canvas with its own toolbar and interaction model, closer in spirit
/// to the Terminale di reparto than to the CRUD lists the rest of the app is made of.</summary>
public partial class PlanningBoardWindow : Window
{
    private const double NameColumnWidth = 260;
    private const double WeekColumnWidth = 30;
    private const double HeaderRowHeight = 24;
    private const double ProjectRowHeight = 36;

    private static readonly Dictionary<string, string> StatusLabels = new()
    {
        ["Confermata"] = "Confermata",
        ["InValutazione"] = "In valutazione",
        ["InProduzione"] = "In produzione",
        ["Sospesa"] = "Sospesa",
        ["Consegnata"] = "Consegnata",
    };

    private static readonly Dictionary<string, string> StatusColors = new()
    {
        ["Confermata"] = "#B36F1B",
        ["InValutazione"] = "#8C7F6A",
        ["InProduzione"] = "#2E6F9E",
        ["Sospesa"] = "#C0392B",
        ["Consegnata"] = "#3D7A4C",
    };

    private readonly ApiClient _apiClient;
    private readonly bool _isAdmin;

    private List<PlanningCategoryDto> _categories = [];
    private List<PlanningProjectDto> _projects = [];
    private List<PlanningCellDto> _cells = [];
    private readonly Dictionary<(Guid ProjectId, int WeekIndex), Border> _weekCellBorders = new();

    private DateTime _rangeStart = StartOfWeek(DateTime.UtcNow);
    private int _weeksCount = 26;
    private string _searchText = "";
    private string _statusFilter = "";

    private PlanningCategoryDto? _selectedCategory;
    private bool _isEraseMode;

    private bool _isDragging;
    private Guid? _dragProjectId;
    private int _dragStartWeekIndex;
    private int _dragCurrentWeekIndex;

    public PlanningBoardWindow(ApiClient apiClient, bool isAdmin)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _isAdmin = isAdmin;
        SubtitleText.Text = _isAdmin
            ? "Trascina su una riga per dipingere le settimane con la categoria selezionata."
            : "Sei in sola lettura: solo un Admin può modificare il planning.";

        if (!_isAdmin)
        {
            ToolPanel.Visibility = Visibility.Collapsed;
            ManageCategoriesButton.Visibility = Visibility.Collapsed;
            NewProjectButton.Visibility = Visibility.Collapsed;
        }

        Loaded += async (_, _) => await LoadAllAsync();
    }

    private async Task LoadAllAsync()
    {
        try
        {
            _categories = (await _apiClient.GetPlanningCategoriesAsync()).ToList();
            _projects = (await _apiClient.GetPlanningProjectsAsync()).ToList();
            await LoadCellsAsync();
            BuildCategoryButtons();
            BuildGrid();
            UpdateTitle();
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async Task LoadCellsAsync()
    {
        _cells = (await _apiClient.GetPlanningCellsAsync(_rangeStart, _weeksCount)).ToList();
    }

    private void UpdateTitle()
    {
        var rangeEnd = _rangeStart.AddDays(_weeksCount * 7 - 1);
        TitleText.Text = $"Planning produzione — {_projects.Count} macchine · {_weeksCount} settimane";
        SubtitleText.Text = (_isAdmin
            ? "Trascina su una riga per dipingere le settimane con la categoria selezionata. "
            : "Sei in sola lettura: solo un Admin può modificare il planning. ")
            + $"{_rangeStart:dd/MM/yyyy} → {rangeEnd:dd/MM/yyyy}";
    }

    private void BuildCategoryButtons()
    {
        CategoryButtonsPanel.Items.Clear();
        foreach (var category in _categories.Where(c => c.IsActive).OrderBy(c => c.SequenceNumber))
        {
            var button = new ToggleButton
            {
                Content = $"{category.Code} · {category.Name}",
                Height = 30,
                Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(0, 4, 8, 0),
                Tag = category,
                Background = (Brush)new BrushConverter().ConvertFromString(category.ColorHex)!,
                Foreground = Brushes.White,
                IsChecked = _selectedCategory?.Id == category.Id,
            };
            button.Click += CategoryButton_Click;
            CategoryButtonsPanel.Items.Add(button);
        }
    }

    private void CategoryButton_Click(object sender, RoutedEventArgs e)
    {
        var clicked = (ToggleButton)sender;
        var category = (PlanningCategoryDto)clicked.Tag;

        _selectedCategory = category;
        _isEraseMode = false;
        EraseModeButton.IsChecked = false;
        FillModeButton.IsChecked = true;

        foreach (ToggleButton button in CategoryButtonsPanel.Items)
        {
            button.IsChecked = ReferenceEquals(button, clicked);
        }
    }

    private void FillModeButton_Click(object sender, RoutedEventArgs e)
    {
        _isEraseMode = false;
        EraseModeButton.IsChecked = false;
        FillModeButton.IsChecked = true;
    }

    private void EraseModeButton_Click(object sender, RoutedEventArgs e)
    {
        _isEraseMode = true;
        FillModeButton.IsChecked = false;
        EraseModeButton.IsChecked = true;
    }

    private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchText = SearchBox.Text.Trim();
        BuildGrid();
        await Task.CompletedTask;
    }

    private void StatusFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _statusFilter = (StatusFilterCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
        if (IsLoaded && _projects.Count > 0)
        {
            BuildGrid();
        }
    }

    private async void WeeksRangeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WeeksRangeCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string tag || !int.TryParse(tag, out var weeks))
        {
            return;
        }

        _weeksCount = weeks;
        if (IsLoaded && _projects.Count > 0)
        {
            await LoadCellsAsync();
            BuildGrid();
            UpdateTitle();
        }
    }

    private async void TodayButton_Click(object sender, RoutedEventArgs e)
    {
        _rangeStart = StartOfWeek(DateTime.UtcNow);
        await LoadCellsAsync();
        BuildGrid();
        UpdateTitle();
    }

    private async void PrevRangeButton_Click(object sender, RoutedEventArgs e)
    {
        _rangeStart = _rangeStart.AddDays(-_weeksCount * 7 / 2);
        await LoadCellsAsync();
        BuildGrid();
        UpdateTitle();
    }

    private async void NextRangeButton_Click(object sender, RoutedEventArgs e)
    {
        _rangeStart = _rangeStart.AddDays(_weeksCount * 7 / 2);
        await LoadCellsAsync();
        BuildGrid();
        UpdateTitle();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await LoadAllAsync();

    private async void NewProjectButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CreatePlanningProjectWindow(_apiClient) { Owner = this };
        dialog.ShowDialog();
        if (dialog.Created)
        {
            await LoadAllAsync();
        }
    }

    private async void ManageCategoriesButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ManagePlanningCategoriesWindow(_apiClient) { Owner = this };
        dialog.ShowDialog();
        await LoadAllAsync();
    }

    private async void EditProjectName_Click(PlanningProjectDto project)
    {
        var dialog = new CreatePlanningProjectWindow(_apiClient, project) { Owner = this };
        dialog.ShowDialog();
        if (dialog.Created)
        {
            await LoadAllAsync();
        }
    }

    private async void MoveProjectUp_Click(PlanningProjectDto project)
    {
        await MoveProjectAsync(project, -1);
    }

    private async void MoveProjectDown_Click(PlanningProjectDto project)
    {
        await MoveProjectAsync(project, 1);
    }

    private async Task MoveProjectAsync(PlanningProjectDto project, int direction)
    {
        var ordered = _projects.Where(p => p.IsActive).OrderBy(p => p.SequenceNumber).Select(p => p.Id).ToList();
        var index = ordered.IndexOf(project.Id);
        var newIndex = index + direction;
        if (newIndex < 0 || newIndex >= ordered.Count)
        {
            return;
        }

        (ordered[index], ordered[newIndex]) = (ordered[newIndex], ordered[index]);

        try
        {
            await _apiClient.ReorderPlanningProjectsAsync(ordered);
            await LoadAllAsync();
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async void StatusCombo_SelectionChanged(PlanningProjectDto project, string newStatus)
    {
        if (project.Status == newStatus)
        {
            return;
        }

        try
        {
            await _apiClient.EditPlanningProjectAsync(project.Id, project.Name, newStatus, project.Notes, project.WorkOrderId);
            await LoadAllAsync();
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private void BuildGrid()
    {
        BoardGrid.RowDefinitions.Clear();
        BoardGrid.ColumnDefinitions.Clear();
        BoardGrid.Children.Clear();
        _weekCellBorders.Clear();

        var visibleProjects = _projects
            .Where(p => p.IsActive &&
                (string.IsNullOrEmpty(_statusFilter) || p.Status == _statusFilter) &&
                (string.IsNullOrWhiteSpace(_searchText) || p.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(p => p.SequenceNumber)
            .ToList();

        BoardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(NameColumnWidth) });
        for (var w = 0; w < _weeksCount; w++)
        {
            BoardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(WeekColumnWidth) });
        }

        BoardGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HeaderRowHeight) });
        BoardGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HeaderRowHeight) });
        foreach (var _ in visibleProjects)
        {
            BoardGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(ProjectRowHeight) });
        }

        AddHeaderCorner();
        AddMonthHeaders();
        AddWeekNumberHeaders();

        for (var r = 0; r < visibleProjects.Count; r++)
        {
            AddProjectRow(visibleProjects[r], r + 2, r);
        }
    }

    private void AddHeaderCorner()
    {
        var corner = new Border { Background = Brushes.Transparent };
        Grid.SetColumn(corner, 0);
        Grid.SetRow(corner, 0);
        Grid.SetRowSpan(corner, 2);
        BoardGrid.Children.Add(corner);
    }

    private void AddMonthHeaders()
    {
        var culture = new CultureInfo("it-IT");
        var col = 0;
        while (col < _weeksCount)
        {
            var weekDate = _rangeStart.AddDays(col * 7);
            var span = 1;
            while (col + span < _weeksCount && _rangeStart.AddDays((col + span) * 7).Month == weekDate.Month)
            {
                span++;
            }

            var label = new TextBlock
            {
                // A month at either edge of the visible range can span just one narrow week column —
                // trimming (and clipping the container so an untrimmed remainder can't spill into the
                // neighboring month's columns) keeps it readable instead of the label overflowing on
                // both sides and getting cut into an unreadable fragment.
                Text = weekDate.ToString("MMMM yyyy", culture).ToUpper(culture),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
            };
            var labelContainer = new Border { Child = label, ClipToBounds = true };
            Grid.SetColumn(labelContainer, col + 1);
            Grid.SetColumnSpan(labelContainer, span);
            Grid.SetRow(labelContainer, 0);
            BoardGrid.Children.Add(labelContainer);

            col += span;
        }
    }

    private void AddWeekNumberHeaders()
    {
        var calendar = CultureInfo.InvariantCulture.Calendar;
        for (var w = 0; w < _weeksCount; w++)
        {
            var weekDate = _rangeStart.AddDays(w * 7);
            var weekNumber = calendar.GetWeekOfYear(weekDate, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
            var label = new TextBlock
            {
                Text = weekNumber.ToString(),
                FontSize = 10,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var border = new Border
            {
                BorderBrush = (Brush)FindResource("TextSecondaryBrush"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = label,
            };
            Grid.SetColumn(border, w + 1);
            Grid.SetRow(border, 1);
            BoardGrid.Children.Add(border);
        }
    }

    private void AddProjectRow(PlanningProjectDto project, int gridRow, int rowIndex)
    {
        var nameArea = new DockPanel { Margin = new Thickness(4, 2, 4, 2) };

        if (_isAdmin)
        {
            var reorderPanel = new StackPanel { Orientation = Orientation.Vertical };
            DockPanel.SetDock(reorderPanel, Dock.Right);
            var upButton = new Button { Content = "▲", Width = 18, Height = 16, FontSize = 8, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 1) };
            var downButton = new Button { Content = "▼", Width = 18, Height = 16, FontSize = 8, Padding = new Thickness(0) };
            upButton.Click += (_, _) => MoveProjectUp_Click(project);
            downButton.Click += (_, _) => MoveProjectDown_Click(project);
            reorderPanel.Children.Add(upButton);
            reorderPanel.Children.Add(downButton);
            nameArea.Children.Add(reorderPanel);
        }

        var statusCombo = new ComboBox { Width = 108, Height = 22, FontSize = 10 };
        DockPanel.SetDock(statusCombo, Dock.Right);
        foreach (var (code, label) in StatusLabels)
        {
            statusCombo.Items.Add(new ComboBoxItem { Content = label, Tag = code });
        }
        statusCombo.SelectedIndex = StatusLabels.Keys.ToList().IndexOf(project.Status);
        statusCombo.SelectionChanged += (_, _) =>
        {
            if (statusCombo.SelectedItem is ComboBoxItem item && item.Tag is string newStatus)
            {
                StatusCombo_SelectionChanged(project, newStatus);
            }
        };
        statusCombo.IsEnabled = _isAdmin;
        nameArea.Children.Add(statusCombo);

        var nameText = new TextBlock
        {
            Text = project.Name,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(4, 0, 4, 0),
            Cursor = _isAdmin ? Cursors.Hand : Cursors.Arrow,
            ToolTip = project.Notes,
        };
        if (_isAdmin)
        {
            nameText.MouseLeftButtonDown += (_, _) => EditProjectName_Click(project);
        }
        nameArea.Children.Add(nameText);

        var nameBorder = new Border
        {
            Child = nameArea,
            BorderBrush = (Brush)FindResource("TextSecondaryBrush"),
            BorderThickness = new Thickness(0, 0, 1, 1),
            Background = rowIndex % 2 == 0 ? Brushes.Transparent : new SolidColorBrush(Color.FromArgb(12, 0, 0, 0)),
        };
        Grid.SetColumn(nameBorder, 0);
        Grid.SetRow(nameBorder, gridRow);
        BoardGrid.Children.Add(nameBorder);

        for (var w = 0; w < _weeksCount; w++)
        {
            var weekDate = _rangeStart.AddDays(w * 7);
            var cellCategoryIds = _cells
                .Where(c => c.ProjectId == project.Id && c.WeekStart == weekDate)
                .Select(c => c.CategoryId)
                .ToList();

            var wrap = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            foreach (var categoryId in cellCategoryIds)
            {
                var category = _categories.FirstOrDefault(c => c.Id == categoryId);
                if (category is null)
                {
                    continue;
                }

                wrap.Children.Add(new Border
                {
                    Background = (Brush)new BrushConverter().ConvertFromString(category.ColorHex)!,
                    Width = 14,
                    Height = 14,
                    Margin = new Thickness(1),
                    Child = new TextBlock
                    {
                        Text = category.Code.Length > 0 ? category.Code[..1] : "",
                        FontSize = 8,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                });
            }

            var cellBorder = new Border
            {
                BorderBrush = (Brush)FindResource("TextSecondaryBrush"),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Background = rowIndex % 2 == 0 ? Brushes.Transparent : new SolidColorBrush(Color.FromArgb(12, 0, 0, 0)),
                Child = wrap,
                Tag = (project.Id, w),
            };
            if (_isAdmin)
            {
                cellBorder.MouseLeftButtonDown += Cell_MouseLeftButtonDown;
            }

            Grid.SetColumn(cellBorder, w + 1);
            Grid.SetRow(cellBorder, gridRow);
            BoardGrid.Children.Add(cellBorder);
            _weekCellBorders[(project.Id, w)] = cellBorder;
        }
    }

    private void Cell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_selectedCategory is null && !_isEraseMode)
        {
            ErrorText.Text = "Seleziona prima una categoria (o \"Cancella\") dalla barra strumenti.";
            return;
        }

        var border = (Border)sender;
        var (projectId, weekIndex) = ((Guid, int))border.Tag;

        _isDragging = true;
        _dragProjectId = projectId;
        _dragStartWeekIndex = weekIndex;
        _dragCurrentWeekIndex = weekIndex;

        BoardGrid.MouseMove += BoardGrid_MouseMove;
        BoardGrid.MouseLeftButtonUp += BoardGrid_MouseLeftButtonUp;
        BoardGrid.CaptureMouse();

        HighlightDragRange();
        e.Handled = true;
    }

    private void BoardGrid_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || _dragProjectId is null)
        {
            return;
        }

        var position = e.GetPosition(BoardGrid);
        var hit = BoardGrid.InputHitTest(position) as DependencyObject;
        var border = FindAncestorWeekCell(hit);
        if (border?.Tag is ValueTuple<Guid, int> tag && tag.Item1 == _dragProjectId.Value && tag.Item2 != _dragCurrentWeekIndex)
        {
            _dragCurrentWeekIndex = tag.Item2;
            HighlightDragRange();
        }
    }

    private async void BoardGrid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging || _dragProjectId is null)
        {
            return;
        }

        _isDragging = false;
        BoardGrid.ReleaseMouseCapture();
        BoardGrid.MouseMove -= BoardGrid_MouseMove;
        BoardGrid.MouseLeftButtonUp -= BoardGrid_MouseLeftButtonUp;

        var minWeek = Math.Min(_dragStartWeekIndex, _dragCurrentWeekIndex);
        var maxWeek = Math.Max(_dragStartWeekIndex, _dragCurrentWeekIndex);
        var weekStarts = Enumerable.Range(minWeek, maxWeek - minWeek + 1)
            .Select(w => _rangeStart.AddDays(w * 7))
            .ToList();
        var projectId = _dragProjectId.Value;
        _dragProjectId = null;

        try
        {
            if (_isEraseMode)
            {
                await _apiClient.ClearPlanningCellsAsync(projectId, weekStarts);
            }
            else if (_selectedCategory is not null)
            {
                await _apiClient.PaintPlanningCellsAsync(projectId, _selectedCategory.Id, weekStarts);
            }

            ErrorText.Text = "";
            await LoadCellsAsync();
            BuildGrid();
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
            BuildGrid();
        }
    }

    private void HighlightDragRange()
    {
        if (_dragProjectId is null)
        {
            return;
        }

        var minWeek = Math.Min(_dragStartWeekIndex, _dragCurrentWeekIndex);
        var maxWeek = Math.Max(_dragStartWeekIndex, _dragCurrentWeekIndex);
        var highlightBrush = _isEraseMode
            ? new SolidColorBrush(Color.FromArgb(80, 192, 57, 43))
            : new SolidColorBrush(Color.FromArgb(80, 46, 111, 158));

        for (var w = 0; w < _weeksCount; w++)
        {
            if (_weekCellBorders.TryGetValue((_dragProjectId.Value, w), out var border))
            {
                border.Opacity = 1;
                if (w >= minWeek && w <= maxWeek)
                {
                    border.Background = highlightBrush;
                }
            }
        }
    }

    private static Border? FindAncestorWeekCell(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is Border { Tag: ValueTuple<Guid, int> } border)
            {
                return border;
            }

            element = System.Windows.Media.VisualTreeHelper.GetParent(element);
        }

        return null;
    }

    private static DateTime StartOfWeek(DateTime date)
    {
        var diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
        return DateTime.SpecifyKind(date.Date.AddDays(-diff), DateTimeKind.Utc);
    }
}
