using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TinySolitaire.Game;
using TinySolitaire.Services;
using Windows.Foundation;

namespace TinySolitaire.Views;

public sealed partial class FlyoutPage : Page, IDisposable
{
    private readonly Stopwatch _clock = new();
    private readonly Task _initialization;
    private readonly Dictionary<CardLocation, Button> _cards = [];
    private readonly Dictionary<CardLocation, FrameworkElement> _dropTargets = [];
    private SolitaireGame? _game;
    private CardLocation? _selected;
    private CardLocation? _pointerSource;
    private Point _pressPoint;
    private Point _grabOffset;
    private Button? _pressedButton;
    private Pointer? _pressedPointer;
    private readonly TranslateTransform _dragTransform = new();
    private readonly List<Button> _draggedCards = [];
    private long _lastDragFinished;
    private int _clockBase;
    private bool _visible;
    private bool _disposed;
    private bool _dialogOpen;
    private Button? _stockButton;
    private int _stockCardStyle = -1;

    public bool IsDragging { get; private set; }
    public event Action? DragEnded;

    public FlyoutPage()
    {
        InitializeComponent();
        _initialization = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        NewButton.IsEnabled = UndoButton.IsEnabled = HintButton.IsEnabled = false;
        (SolitaireGame game, string? warning) = await GameStorage.LoadAsync();
        if (_disposed)
            return;
        _game = game;
        ShowError(warning ?? SettingsService.Error);
        Render();
        if (_visible)
        {
            StartClock();
            FocusStock();
        }
    }

    public void OnShown()
    {
        _visible = true;
        if (_game is not null)
        {
            Render();
            StartClock();
            FocusStock();
        }
    }

    public void OnHidden()
    {
        HintTip.IsOpen = false;
        _visible = false;
        _autoFinishCancellation?.Cancel();
        EndDrag();
        StopCardAnimations();
        StopVictoryAnimation();
        PauseClock();
        _ = SaveAsync();
    }

    public void Dispose()
    {
        HintTip.IsOpen = false;
        _visible = false;
        _autoFinishCancellation?.Cancel();
        EndDrag();
        StopCardAnimations();
        StopVictoryAnimation();
        _disposed = true;
        PauseClock();
    }

    private void FocusStock()
    {
        if (_cards.TryGetValue(new(PileKind.Stock), out Button? stock) && stock.IsEnabled)
            stock.Focus(FocusState.Programmatic);
        else
            NewButton.Focus(FocusState.Programmatic);
    }

    public bool ClearSelection()
    {
        bool hintWasOpen = HintTip.IsOpen;
        HintTip.IsOpen = false;
        if (IsDragging)
        {
            EndDrag(animateReturn: true);
            SetSelection(null);
            return true;
        }
        if (_selected is null)
            return hintWasOpen;
        SetSelection(null);
        return true;
    }

    public async Task FlushAsync()
    {
        await _initialization;
        PauseClock();
        await SaveAsync();
    }

    private void SetSelection(CardLocation? source)
    {
        HintTip.IsOpen = false;
        _selected = source;
        foreach ((CardLocation location, Button card) in _cards)
        {
            if (location.Kind == PileKind.Stock) continue;
            card.Style = CardVisual.Style(location == source ? "SelectedCardButtonStyle" : "PlayingCardButtonStyle");
            string name = AutomationProperties.GetName(card).Replace(", selected", "");
            AutomationProperties.SetName(card, location == source ? name + ", selected" : name);
        }
    }
    private void StartClock()
    {
        if (_game is null || _clock.IsRunning || _game.IsWon || _game.State.Moves == 0)
            return;
        _clockBase = _game.State.ElapsedSeconds;
        _clock.Restart();
    }

    private void UpdateClock()
    {
        if (_game is null)
            return;
        if (_clock.IsRunning)
            _game.State.ElapsedSeconds = _clockBase + (int)_clock.Elapsed.TotalSeconds;
    }

    private void PauseClock()
    {
        UpdateClock();
        _clock.Stop();
    }

    private async Task SaveAsync()
    {
        if (_game is null)
            return;
        UpdateClock();
        string? error = await GameStorage.SaveAsync(_game.Export());
        if (!_disposed)
            ShowError(error);
    }

    private async Task ChangedAsync(CardLocation? focus = null)
    {
        _selected = null;
        bool celebrate = VictoryPolicy.ShouldCelebrate(_renderedWon, _game!.IsWon, _visible);
        if (_game!.IsWon)
        {
            PauseClock();
        }
        else if (_visible)
            StartClock();
        Render(animate: _visible);
        int version = _renderVersion;
        Task animation = _cardAnimationTask;
        if (focus is { } location && _cards.TryGetValue(location, out Button? button))
            button.Focus(FocusState.Programmatic);
        await Task.WhenAll(SaveAsync(), animation);
        if (celebrate && _visible && !_disposed && version == _renderVersion)
            PlayVictoryAnimation();
    }

    private void Render(bool animate = false)
    {
        HintTip.IsOpen = false;
        StopVictoryAnimation();
        var positions = animate && _visible && _motionSettings.AnimationsEnabled ? CaptureCardPositions() : null;
        StopCardAnimations();
        EndDrag();
        if (_game is null)
            return;
        _renderVersion++;
        GameState state = _game.State;
        UpdateClock();
        NewButton.IsEnabled = HintButton.IsEnabled = true;
        UndoButton.IsEnabled = _game.CanUndo;
        FinishButton.IsEnabled = _game.CanAutoFinish;
        for (int i = TopPiles.Children.Count - 1; i >= 0; i--)
            if (TopPiles.Children[i] != _stockButton)
                TopPiles.Children.RemoveAt(i);
        TableauGrid.Children.Clear();
        _cards.Clear();
        _dropTargets.Clear();
        _cardElements.Clear();
        _cardPlacements = CardLayout.GetPlacements(state).ToArray();

        Button stock = UpdateStock(state);
        _cards[new(PileKind.Stock)] = stock;
        _cardElements[new(PileKind.Stock)] = stock;

        if (state.Waste.Count == 0)
            Put(TopPiles, Slot("\u00B7", "Waste pile, empty", "EmptyWaste", false), 60);
        int firstWaste = Math.Max(0, state.Waste.Count - state.DrawCount);
        foreach (CardPlacement placement in _cardPlacements.Where(p =>
            p.Location.Kind == PileKind.Waste && p.Location == p.VisualLocation))
        {
            Button card = MakeCard(placement.Card, placement.Location, placement.Location.Index == state.Waste.Count - 1);
            Put(TopPiles, card, 60 + Math.Max(0, placement.Location.Index - firstWaste) * 14);
        }
        for (int i = 0; i < 4; i++)
        {
            CardLocation target = new(PileKind.Foundation, i);
            List<Card> pile = state.Foundations[i];
            Button button = pile.Count == 0
                ? Slot(new[] { "\u2663", "\u2666", "\u2665", "\u2660" }[i], $"Foundation {i + 1}, empty. Place an ace.", $"Foundation{i}")
                : MakeCard(pile[^1], new(PileKind.Foundation, i, pile.Count - 1));
            if (pile.Count == 0)
            {
                button.Click += async (_, _) => await DestinationAsync(target);
            }
            Put(TopPiles, button, (i + 3) * 54);
            _dropTargets[target] = button;
        }

        for (int i = 0; i < 7; i++)
        {
            List<Card> pile = state.Tableau[i];
            Canvas column = new() { Width = 48, Height = Math.Max(72, 72 + (pile.Count - 1) * 21), VerticalAlignment = VerticalAlignment.Top };
            Grid.SetColumn(column, i);
            CardLocation target = new(PileKind.Tableau, i);
            Button empty = Slot("", $"Column {i + 1}, empty. Place a king.", $"Column{i}");
            empty.Click += async (_, _) => await DestinationAsync(target);
            _dropTargets[target] = column;
            if (pile.Count == 0)
                column.Children.Add(empty);
            for (int j = 0; j < pile.Count; j++)
            {
                FrameworkElement card = pile[j].FaceUp
                    ? MakeCard(pile[j], new(PileKind.Tableau, i, j))
                    : CardVisual.Back(SettingsService.CardStyle);
                Canvas.SetTop(card, j * 21);
                column.Children.Add(card);
                _cardElements[new(PileKind.Tableau, i, j)] = card;
            }
            TableauGrid.Children.Add(column);
        }
        if (positions is not null)
            _cardAnimationTask = AnimateCards(positions);
        _renderedWon = _game.IsWon;
        App.Current.SetTrayStatus(_game.IsWon ? "Deal complete" : $"Draw {state.DrawCount} \u00b7 {state.Moves} moves");
    }

    private Button UpdateStock(GameState state)
    {
        if (_stockButton is null)
        {
            _stockButton = Slot("\u21BB", "Stock pile", "StockPile");
            _stockButton.Click += async (_, _) => await DrawAsync();
            Put(TopPiles, _stockButton, 0);
        }
        if (state.Stock.Count > 0)
        {
            int pattern = SettingsService.CardStyle;
            if (_stockButton.Content is not Border || _stockCardStyle != pattern)
            {
                _stockButton.Content = CardVisual.Back(pattern);
                _stockCardStyle = pattern;
            }
        }
        else if (_stockButton.Content is not TextBlock)
        {
            _stockButton.Content = new TextBlock
            {
                Text = "\u21BB",
                FontSize = 25,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
        }
        _stockButton.IsEnabled = state.Stock.Count > 0 || state.Waste.Count > 0;
        AutomationProperties.SetName(_stockButton, state.Stock.Count == 0 ? "Recycle waste" : $"Draw {state.DrawCount} cards");
        return _stockButton;
    }

    private Button MakeCard(Card card, CardLocation source, bool playable = true)
    {
        Button button = new()
        {
            Style = CardVisual.Style(_selected == source ? "SelectedCardButtonStyle" : "PlayingCardButtonStyle"),
            Content = CardVisual.Face(card),
            IsTabStop = playable,
            IsHitTestVisible = playable,
        };
        AutomationProperties.SetAutomationId(button, $"Card_{source.Kind}_{source.Pile}_{source.Index}");
        AutomationProperties.SetName(button, $"{card.Name}, {source.Kind.ToString().ToLowerInvariant()} {source.Pile + 1}{(_selected == source ? ", selected" : "")}");
        _cards[source] = button;
        _cardElements[source] = button;
        if (!playable)
            return button;
        button.Click += async (_, _) => await CardClickedAsync(source);
        button.DoubleTapped += async (_, args) =>
        {
            args.Handled = true;
            if (!IsBusy && _game!.CanSelect(source) && _game.TryAutoMove(source))
                await ChangedAsync();
        };
        WireCardDrag(button, source);
        return button;
    }

    private void WireCardDrag(Button button, CardLocation source)
    {
        button.AddHandler(PointerPressedEvent, new PointerEventHandler((_, args) =>
        {
            if (!IsBusy && _pressedPointer is null && args.GetCurrentPoint(button).Properties.IsLeftButtonPressed && _game!.CanSelect(source))
            {
                _pointerSource = source;
                _pressedButton = button;
                _pressedPointer = args.Pointer;
                _pressPoint = args.GetCurrentPoint(DragLayer).Position;
                Point origin = button.TransformToVisual(DragLayer).TransformPoint(new Point());
                _grabOffset = new(_pressPoint.X - origin.X, _pressPoint.Y - origin.Y);
                button.CapturePointer(args.Pointer);
            }
        }), true);
        button.AddHandler(PointerMovedEvent, new PointerEventHandler((_, args) =>
        {
            if (_pointerSource != source || args.Pointer.PointerId != _pressedPointer?.PointerId
                || !args.GetCurrentPoint(button).Properties.IsLeftButtonPressed)
                return;
            Point current = args.GetCurrentPoint(DragLayer).Position;
            if (!IsDragging && Math.Abs(current.X - _pressPoint.X) + Math.Abs(current.Y - _pressPoint.Y) > 6)
                BeginDrag(source);
            if (IsDragging)
                MoveDragPreview(current);
        }), true);
        button.AddHandler(PointerReleasedEvent, new PointerEventHandler(async (_, args) =>
        {
            if (_pointerSource != source || args.Pointer.PointerId != _pressedPointer?.PointerId)
                return;
            bool wasDragging = IsDragging;
            CardLocation? destination = wasDragging
                ? FindDropTarget(source, args.GetCurrentPoint(DragLayer).Position, button)
                : null;
            if (!wasDragging)
            {
                EndDrag();
                return;
            }
            args.Handled = true;
            if (destination is { } target && _game!.TryMove(source, target))
                await ChangedAsync();
            else
                EndDrag(animateReturn: true);
        }), true);
        button.AddHandler(PointerCanceledEvent, new PointerEventHandler((_, args) =>
        {
            if (_pressedButton == button && args.Pointer.PointerId == _pressedPointer?.PointerId)
                EndDrag(animateReturn: true);
        }), true);
        button.AddHandler(PointerCaptureLostEvent, new PointerEventHandler((_, args) =>
        {
            uint pointerId = args.Pointer.PointerId;
            // Button may release capture before its PointerReleased handler runs.
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_pressedButton == button && _pressedPointer?.PointerId == pointerId)
                    EndDrag(animateReturn: true);
            });
        }), true);
    }

    private CardLocation? FindDropTarget(CardLocation source, Point position, Button card)
    {
        Rect viewport = GetTableauViewport(DragLayer);
        List<DropTarget> targets = [];
        foreach ((CardLocation location, FrameworkElement element) in _dropTargets)
        {
            Rect bounds = element.TransformToVisual(DragLayer)
                .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            targets.Add(location.Kind == PileKind.Tableau
                ? new(location, bounds.Left, viewport.Top, bounds.Width, viewport.Height)
                : new(location, bounds.Left, bounds.Top, bounds.Width, bounds.Height));
        }
        return DropTargetPolicy.Find(source, position.X - _grabOffset.X, position.Y - _grabOffset.Y,
            card.ActualWidth, card.ActualHeight, targets);
    }

    private void BeginDrag(CardLocation source)
    {
        IsDragging = true;
        SetSelection(source);
        IReadOnlyList<Card> pile = source.Kind switch
        {
            PileKind.Tableau => _game!.State.Tableau[source.Pile],
            PileKind.Foundation => _game!.State.Foundations[source.Pile],
            _ => _game!.State.Waste,
        };
        int count = pile.Count - source.Index;
        ThemeShadow shadow = new();
        shadow.Receivers.Add(GameLayout);
        Canvas preview = new()
        {
            Width = 48,
            Height = 72 + (count - 1) * 21,
            RenderTransform = _dragTransform,
            Shadow = shadow,
            Translation = new(0, 0, 32),
        };
        for (int i = source.Index; i < pile.Count; i++)
        {
            Border face = CardVisual.Front(pile[i]);
            Canvas.SetTop(face, (i - source.Index) * 21);
            preview.Children.Add(face);
            if (_cards.TryGetValue(new(source.Kind, source.Pile, i), out Button? original))
            {
                original.Opacity = 0;
                _draggedCards.Add(original);
            }
        }
        DragLayer.Children.Add(preview);
    }

    private void MoveDragPreview(Point position)
    {
        _dragTransform.X = position.X - _grabOffset.X;
        _dragTransform.Y = position.Y - _grabOffset.Y;
    }

    private void EndDrag(bool animateReturn = false)
    {
        bool wasDragging = IsDragging;
        var positions = wasDragging && animateReturn && _visible && _motionSettings.AnimationsEnabled
            ? CaptureCardPositions() : null;
        IsDragging = false;
        _pointerSource = null;
        DragLayer.Children.Clear();
        foreach (Button original in _draggedCards)
            original.Opacity = 1;
        _draggedCards.Clear();
        Button? button = _pressedButton;
        Pointer? pointer = _pressedPointer;
        _pressedButton = null;
        _pressedPointer = null;
        if (button is not null && pointer is not null)
            button.ReleasePointerCapture(pointer);
        if (wasDragging)
        {
            _lastDragFinished = Stopwatch.GetTimestamp();
            DragEnded?.Invoke();
        }
        if (positions is not null)
            _cardAnimationTask = AnimateCards(positions);
    }

    private async Task CardClickedAsync(CardLocation source)
    {
        if (_game is null || !_game.CanSelect(source) || IsBusy
            || (_lastDragFinished != 0 && Stopwatch.GetElapsedTime(_lastDragFinished).TotalMilliseconds < 200))
            return;
        if (_selected is { } selected && _game.TryMove(selected, new(source.Kind, source.Pile)))
        {
            await ChangedAsync();
            return;
        }
        SetSelection(_selected == source ? null : source);
        if (_cards.TryGetValue(source, out Button? button))
            button.Focus(FocusState.Programmatic);
    }

    private async Task DestinationAsync(CardLocation target)
    {
        if (!IsBusy && _selected is { } source && _game!.TryMove(source, target))
            await ChangedAsync();
    }

    private async Task DrawAsync()
    {
        if (_game is not null && !IsBusy && _game.Draw())
            await ChangedAsync(new(PileKind.Stock));
    }

    private async Task UndoAsync()
    {
        if (_game is not null && !IsBusy && _game.Undo())
            await ChangedAsync();
    }

    private void ShowHint()
    {
        if (_game is null || IsBusy)
            return;
        if (HintTip.IsOpen)
        {
            HintTip.IsOpen = false;
            return;
        }
        GameHint hint = _game.FindHint();
        SetSelection(hint.Source);
        HintMessage.Text = hint.Message;
        CardLocation? focus = hint.Source ?? hint.Target;
        if (focus is { } source && _cards.TryGetValue(source, out Button? button))
            button.Focus(FocusState.Programmatic);
        HintTip.IsOpen = true;
    }

    private async Task NewDealAsync(bool restart)
    {
        await _initialization;
        if (_game is null || IsBusy)
            return;
        if (_game.State.Moves > 0 && !_game.IsWon)
        {
            _dialogOpen = true;
            try
            {
                ContentDialog dialog = new()
                {
                    XamlRoot = XamlRoot,
                    Title = restart ? "Restart this deal?" : "Deal a new game?",
                    Content = "Your current progress and undo history will be replaced.",
                    PrimaryButtonText = restart ? "Restart" : "New game",
                    CloseButtonText = "Keep playing",
                    DefaultButton = ContentDialogButton.Close,
                };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                    return;
            }
            finally { _dialogOpen = false; }
        }
        PauseClock();
        if (restart)
            _game.Restart();
        else
            _game = SolitaireGame.New(SettingsService.DrawCount);
        await ChangedAsync();
    }

    private static Button Slot(string text, string name, string id, bool interactive = true)
    {
        Button button = new()
        {
            Style = CardVisual.Style("SlotButtonStyle"),
            Content = new TextBlock { Text = text, FontSize = 25, HorizontalAlignment = HorizontalAlignment.Center },
            IsTabStop = interactive,
            IsHitTestVisible = interactive,
        };
        foreach (var theme in Application.Current.Resources.ThemeDictionaries)
        {
            ResourceDictionary brushes = (ResourceDictionary)theme.Value;
            button.Resources.ThemeDictionaries[theme.Key] = new ResourceDictionary
            {
                ["ButtonBackgroundPointerOver"] = brushes["SlotBrush"],
                ["ButtonBorderBrushPointerOver"] = brushes["SlotStrokeBrush"],
                ["ButtonForegroundPointerOver"] = brushes["TableMutedBrush"],
            };
        }
        AutomationProperties.SetAutomationId(button, id);
        AutomationProperties.SetName(button, name);
        return button;
    }

    private static void Put(Canvas canvas, UIElement element, double left)
    {
        Canvas.SetLeft(element, left);
        canvas.Children.Add(element);
    }

    private void ShowError(string? error)
    {
        ErrorBar.Message = error ?? "";
        ErrorBar.IsOpen = error is not null;
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => App.Current.ShowSettings();
    private void Hide_Click(object sender, RoutedEventArgs e) => App.Current.HideFlyout();
    private async void New_Click(object sender, RoutedEventArgs e) => await NewDealAsync(false);
    private async void Restart_Click(object sender, RoutedEventArgs e) => await NewDealAsync(true);
    private async void Undo_Click(object sender, RoutedEventArgs e) => await UndoAsync();
    private void Hint_Click(object sender, RoutedEventArgs e) => ShowHint();
    private async void Finish_Click(object sender, RoutedEventArgs e)
    {
        if (_game is null || IsBusy || !_game.CanAutoFinish)
            return;
        _autoFinishing = true;
        using CancellationTokenSource cancellation = new();
        _autoFinishCancellation = cancellation;
        try
        {
            while (_visible && !_disposed && !cancellation.IsCancellationRequested && _game.CanAutoFinish)
            {
                bool moved = false;
                for (int pile = 0; pile < _game.State.Tableau.Count; pile++)
                {
                    int count = _game.State.Tableau[pile].Count;
                    if (count == 0 || !_game.TryAutoMove(new(PileKind.Tableau, pile, count - 1)))
                        continue;
                    moved = true;
                    await ChangedAsync();
                    break;
                }
                if (!moved)
                    break;
            }
        }
        finally
        {
            _autoFinishCancellation = null;
            _autoFinishing = false;
        }
    }
    private async void Help_Click(object sender, RoutedEventArgs e)
    {
        if (IsBusy)
            return;
        _dialogOpen = true;
        try
        {
            await new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "A little Klondike",
                Content = "Build columns down in alternating colors. Move a face-up card and the run beneath it together. Only kings fill empty columns.\n\nBuild the four foundations from ace to king, one suit per pile. Select a card, then a destination; drag a card or double-click to send it to a foundation.\n\nD: draw \u00b7 H: hint \u00b7 Ctrl+Z: undo \u00b7 Ctrl+N: new deal\nEsc: deselect, then hide to tray.\n\nUnlimited redeals. Your deal saves automatically.",
                CloseButtonText = "Let's play",
            }.ShowAsync();
        }
        finally { _dialogOpen = false; }
    }
    private async void New_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    { args.Handled = true; await NewDealAsync(false); }
    private async void Undo_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    { args.Handled = true; await UndoAsync(); }
    private void Hint_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    { args.Handled = true; ShowHint(); }
    private async void Draw_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    { args.Handled = true; await DrawAsync(); }
}
