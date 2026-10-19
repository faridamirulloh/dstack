using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using MiniCodex.Presentation;
using MiniCodex.Services;

namespace MiniCodex.Views;

public partial class AccountWindow : Window
{
    private const double MinimumPaneWidth = 640;
    private const double DividerWidth = 5;
    private readonly AppController _controller;
    private readonly WindowManager _windows;
    private readonly List<AccountPane> _panes = [];
    private bool _allowClose;
    private Task<bool>? _closeTask;
    public bool IsClosing { get; private set; }
    public IReadOnlyList<AccountPane> Panes => _panes;
    internal event EventHandler? ViewportChanged;

    public AccountWindow(AppController controller, WindowManager windows, Guid? profileId = null)
    {
        _controller = controller; _windows = windows;
        InitializeComponent();
        PaneScroller.ScrollChanged += (_, _) => ViewportChanged?.Invoke(this, EventArgs.Empty);
        SizeChanged += (_, _) => ViewportChanged?.Invoke(this, EventArgs.Empty);
        _controller.Changed += UpdateTitle;
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _controller.Changed -= UpdateTitle;
            foreach (var pane in _panes.ToArray()) pane.DisposePane();
            _panes.Clear();
        };
        if (profileId is { } id) AddPane(new AccountPane(controller, windows, id));
    }

    internal void AddPane(AccountPane pane)
    {
        if (IsClosing || _allowClose) throw new InvalidOperationException("This window is closing.");
        _panes.Add(pane);
        pane.AttachTo(this);
        RebuildPanes();
        if (_panes.Count > 1 && IsVisible) WindowState = WindowState.Maximized;
    }

    internal void RemovePane(AccountPane pane)
    {
        if (!_panes.Remove(pane)) return;
        RebuildPanes();
        if (_panes.Count == 0 && !IsClosing) { _allowClose = true; Close(); }
    }

    internal void FocusPane(AccountPane pane)
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show(); Activate();
        pane.BringIntoView();
        if (!pane.IsKeyboardFocusWithin) pane.FocusComposer();
    }

    internal bool IsPaneInView(AccountPane pane)
    {
        if (!ReferenceEquals(GetWindow(pane), this) || !pane.IsVisible || pane.ActualWidth <= 0) return false;
        var position = pane.TranslatePoint(new Point(), PaneScroller);
        return position.X < PaneScroller.ViewportWidth && position.X + pane.ActualWidth > 0;
    }

    private void RebuildPanes()
    {
        PaneGrid.Children.Clear(); PaneGrid.ColumnDefinitions.Clear();
        var minimumWidth = _panes.Count > 1 ? MinimumPaneWidth : 0;
        PaneGrid.MinWidth = _panes.Count * minimumWidth + Math.Max(0, _panes.Count - 1) * DividerWidth;
        foreach (var pane in _panes)
        {
            if (PaneGrid.ColumnDefinitions.Count > 0)
            {
                var dividerColumn = PaneGrid.ColumnDefinitions.Count;
                PaneGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(DividerWidth) });
                var divider = new GridSplitter
                {
                    Style = (Style)FindResource("AccountPaneDivider"), Tag = dividerColumn,
                    Width = DividerWidth, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
                    ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                    ShowsPreview = true
                };
                divider.SetResourceReference(BackgroundProperty, "LineBrush");
                System.Windows.Automation.AutomationProperties.SetName(divider, "Resize account panes");
                Panel.SetZIndex(divider, 1);
                Grid.SetColumn(divider, dividerColumn); PaneGrid.Children.Add(divider);
            }
            var column = PaneGrid.ColumnDefinitions.Count;
            PaneGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = minimumWidth });
            Grid.SetColumn(pane, column); PaneGrid.Children.Add(pane);
            pane.SetSplitMode(_panes.Count > 1);
        }
        UpdateTitle(this, EventArgs.Empty);
    }

    private bool TryGetDividerColumn(object sender, out int column)
    {
        column = sender is Button { Tag: int index } ? index : -1;
        return !IsClosing && !_windows.IsExiting && column > 0 && column < PaneGrid.ColumnDefinitions.Count - 1 && column % 2 == 1
            && !_panes[column / 2].IsClosing && !_panes[column / 2 + 1].IsClosing;
    }

    private void SwapPanes(object sender, RoutedEventArgs e)
    {
        if (!TryGetDividerColumn(sender, out var column)) return;
        var index = column / 2;
        (_panes[index], _panes[index + 1]) = (_panes[index + 1], _panes[index]);
        Grid.SetColumn(_panes[index], column - 1);
        Grid.SetColumn(_panes[index + 1], column + 1);
        PaneGrid.UpdateLayout();
        UpdateTitle(this, EventArgs.Empty);
        ViewportChanged?.Invoke(this, EventArgs.Empty);
        _windows.RememberAccountLayout();
    }

    private void RecenterDivider(object sender, RoutedEventArgs e)
    {
        if (!TryGetDividerColumn(sender, out var column)) return;
        var left = PaneGrid.ColumnDefinitions[column - 1];
        var right = PaneGrid.ColumnDefinitions[column + 1];
        var half = (left.Width.Value + right.Width.Value) / 2;
        left.Width = right.Width = new GridLength(half, GridUnitType.Star);
        PaneGrid.UpdateLayout();
        ViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateTitle(object? sender, EventArgs e) => Title = _panes.Count == 0 ? "Mini Codex" : "Mini Codex · " + string.Join(" | ", _panes.Select(pane => pane.ProfileName));

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose || _windows.IsExiting) return;
        e.Cancel = true;
        if (_closeTask is { IsCompleted: false }) return;
        _closeTask = ClosePanesAsync();
        await _closeTask;
    }

    private async Task<bool> ClosePanesAsync()
    {
        IsClosing = true;
        try
        {
            await Task.Yield();
            if (!_windows.RememberAccountLayout()) return false;
            foreach (var pane in _panes.ToArray())
                if (!await pane.CloseAccountAsync())
                {
                    _windows.RememberAccountLayout();
                    return false;
                }
            _allowClose = true; Close();
            return true;
        }
        finally { IsClosing = false; }
    }
}
