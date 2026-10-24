using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using TinySolitaire.Game;
using TinySolitaire.Services;
using Windows.Foundation;
using Windows.UI.ViewManagement;

namespace TinySolitaire.Views;

public sealed partial class FlyoutPage
{
    private readonly UISettings _motionSettings = new();
    private readonly Dictionary<CardLocation, FrameworkElement> _cardElements = [];
    private readonly List<FrameworkElement> _animationDestinations = [];
    private CardPlacement[] _cardPlacements = [];
    private CompositionScopedBatch? _cardAnimationBatch;
    private TaskCompletionSource? _cardAnimationCompletion;
    private Task _cardAnimationTask = Task.CompletedTask;
    private bool _autoFinishing;
    private CancellationTokenSource? _autoFinishCancellation;

    private bool IsAnimating => _cardAnimationBatch is not null;
    private bool IsBusy => _dialogOpen || IsDragging || IsAnimating || _autoFinishing;

    private readonly record struct CardSnapshot(CardPlacement Placement, Point Position, FrameworkElement Element, bool IsVisible);

    private Rect GetTableauViewport(UIElement relativeTo) => TableauScroller.TransformToVisual(relativeTo)
        .TransformBounds(new Rect(0, 0, TableauScroller.ActualWidth, TableauScroller.ActualHeight));

    private Dictionary<(Suit, int), CardSnapshot> CaptureCardPositions()
    {
        GameLayout.UpdateLayout();
        Rect viewport = GetTableauViewport(AnimationLayer);
        Dictionary<(Suit, int), CardSnapshot> positions = [];
        foreach (CardPlacement placement in _cardPlacements)
        {
            FrameworkElement element = _cardElements[placement.VisualLocation];
            Point position = element.TransformToVisual(AnimationLayer).TransformPoint(new Point());
            if (IsDragging && _pointerSource is { } source
                && placement.Location.Kind == source.Kind && placement.Location.Pile == source.Pile
                && placement.Location.Index >= source.Index)
            {
                position = new(_dragTransform.X, _dragTransform.Y + (placement.Location.Index - source.Index) * 21);
            }
            bool visible = placement.IsVisible && (placement.Location.Kind != PileKind.Tableau
                || (position.Y + element.ActualHeight > viewport.Top && position.Y < viewport.Bottom));
            positions[(placement.Card.Suit, placement.Card.Rank)] = new(placement, position, element, visible);
        }
        return positions;
    }

    private Task AnimateCards(Dictionary<(Suit, int), CardSnapshot> before)
    {
        StopCardAnimations();
        if (!_visible || _disposed || !_motionSettings.AnimationsEnabled)
            return Task.CompletedTask;

        var after = CaptureCardPositions();
        var moves = after.Where(pair => before.TryGetValue(pair.Key, out CardSnapshot previous)
            && (previous.IsVisible || pair.Value.IsVisible)
            && !CardLayout.BecomesCovered(previous.Placement, pair.Value.Placement)
            && (Math.Abs(previous.Position.X - pair.Value.Position.X) > 0.5
                || Math.Abs(previous.Position.Y - pair.Value.Position.Y) > 0.5)).ToArray();
        if (moves.Length == 0)
            return Task.CompletedTask;

        AnimationLayer.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, AnimationLayer.ActualWidth, GetTableauViewport(AnimationLayer).Bottom),
        };
        Compositor compositor = ElementCompositionPreview.GetElementVisual(AnimationLayer).Compositor;
        using CubicBezierEasingFunction easing = compositor.CreateCubicBezierEasingFunction(new(0.2f, 0), new(0, 1));
        _cardAnimationCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task completion = _cardAnimationCompletion.Task;
        _cardAnimationBatch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        _cardAnimationBatch.Completed += OnCardAnimationCompleted;
        GameLayout.IsHitTestVisible = false;

        foreach (var (identity, destination) in after)
        {
            if (before.TryGetValue(identity, out CardSnapshot origin) && origin.IsVisible
                && CardLayout.BecomesCovered(origin.Placement, destination.Placement))
                AddCardOverlay(origin.Placement.Card, origin.Position);
        }

        foreach (var (identity, destination) in moves)
        {
            CardSnapshot origin = before[identity];
            FrameworkElement overlay = AddCardOverlay(destination.Placement.Card, destination.Position);
            if (destination.Placement.IsVisible)
            {
                destination.Element.Opacity = 0;
                _animationDestinations.Add(destination.Element);
            }

            Visual visual = ElementCompositionPreview.GetElementVisual(overlay);
            Vector3 offset = new((float)(origin.Position.X - destination.Position.X),
                (float)(origin.Position.Y - destination.Position.Y), 0);
            using Vector3KeyFrameAnimation animation = compositor.CreateVector3KeyFrameAnimation();
            animation.Duration = TimeSpan.FromMilliseconds(_autoFinishing ? 140 : 240);
            animation.InsertKeyFrame(0, offset);
            animation.InsertKeyFrame(1, Vector3.Zero, easing);
            visual.StartAnimation("Translation", animation);
        }

        _cardAnimationBatch.End();
        return completion;
    }

    private FrameworkElement AddCardOverlay(Card card, Point position)
    {
        FrameworkElement overlay = card.FaceUp ? CardVisual.Front(card) : CardVisual.Back(SettingsService.CardStyle);
        Canvas.SetLeft(overlay, position.X);
        Canvas.SetTop(overlay, position.Y);
        AnimationLayer.Children.Add(overlay);
        ElementCompositionPreview.SetIsTranslationEnabled(overlay, true);
        return overlay;
    }

    private void OnCardAnimationCompleted(object sender, CompositionBatchCompletedEventArgs args)
    {
        if (sender is CompositionScopedBatch batch && batch == _cardAnimationBatch)
            StopCardAnimations();
    }

    private void StopCardAnimations()
    {
        if (_cardAnimationBatch is { } batch)
        {
            _cardAnimationBatch = null;
            batch.Completed -= OnCardAnimationCompleted;
            foreach (UIElement overlay in AnimationLayer.Children)
                ElementCompositionPreview.GetElementVisual(overlay).StopAnimation("Translation");
            batch.Dispose();
        }
        AnimationLayer.Children.Clear();
        foreach (FrameworkElement destination in _animationDestinations)
            destination.Opacity = 1;
        _animationDestinations.Clear();
        GameLayout.IsHitTestVisible = true;
        _cardAnimationCompletion?.TrySetResult();
        _cardAnimationCompletion = null;
        _cardAnimationTask = Task.CompletedTask;
    }
}
