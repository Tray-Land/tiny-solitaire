using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI;

namespace TinySolitaire.Views;

public sealed partial class FlyoutPage
{
    private const int FireworkBurstCount = 13;
    private const int SparksPerBurst = 64;
    private const double BurstIntervalSeconds = 0.45;
    private const float SparkLifetimeSeconds = 2;

    private ContainerVisual? _victoryVisual;
    private DispatcherQueueTimer? _victoryTimer;
    private readonly List<SpriteVisual> _victoryParticles = [];
    private readonly List<CompositionColorBrush> _victoryBrushes = [];
    private bool _renderedWon;
    private int _renderVersion;

    private void PlayVictoryAnimation()
    {
        StopVictoryAnimation();
        if (!_visible || _disposed)
            return;

        VictoryMessage.Text = "You won!";
        VictoryBanner.Visibility = Visibility.Visible;
        bool animate = _motionSettings.AnimationsEnabled;
        if (animate)
            StartFireworks();

        _victoryTimer = DispatcherQueue.CreateTimer();
        _victoryTimer.IsRepeating = false;
        _victoryTimer.Interval = animate
            ? TimeSpan.FromSeconds((FireworkBurstCount - 1) * BurstIntervalSeconds + SparkLifetimeSeconds + 0.5)
            : TimeSpan.FromSeconds(3);
        _victoryTimer.Tick += OnVictoryTimerTick;
        _victoryTimer.Start();
    }

    private void StartFireworks()
    {
        Compositor compositor = ElementCompositionPreview.GetElementVisual(VictoryLayer).Compositor;
        _victoryVisual = compositor.CreateContainerVisual();
        ElementCompositionPreview.SetElementChildVisual(VictoryLayer, _victoryVisual);
        foreach (string key in new[] { "VictoryPrimaryColor", "VictorySecondaryColor", "VictoryAccentColor", "VictoryHighlightColor" })
            _victoryBrushes.Add(compositor.CreateColorBrush((Color)Application.Current.Resources[key]));

        using LinearEasingFunction linear = compositor.CreateLinearEasingFunction();
        for (int burst = 0; burst < FireworkBurstCount; burst++)
        {
            Vector3 origin = new(
                (float)VictoryLayer.ActualWidth * (0.15f + Random.Shared.NextSingle() * 0.7f),
                (float)VictoryLayer.ActualHeight * (0.18f + Random.Shared.NextSingle() * 0.3f), 0);
            TimeSpan delay = TimeSpan.FromSeconds(burst * BurstIntervalSeconds);
            using ScalarKeyFrameAnimation progress = compositor.CreateScalarKeyFrameAnimation();
            progress.Duration = TimeSpan.FromSeconds(SparkLifetimeSeconds);
            progress.DelayTime = delay;
            progress.InsertKeyFrame(0, 0);
            progress.InsertKeyFrame(1, 1, linear);

            using ScalarKeyFrameAnimation fade = compositor.CreateScalarKeyFrameAnimation();
            fade.Duration = progress.Duration;
            fade.DelayTime = delay;
            fade.InsertKeyFrame(0, 0);
            fade.InsertKeyFrame(0.04f, 1, linear);
            fade.InsertKeyFrame(0.6f, 1, linear);
            fade.InsertKeyFrame(1, 0, linear);

            for (int spark = 0; spark < SparksPerBurst; spark++)
            {
                float angle = MathF.Tau * spark / SparksPerBurst + Random.Shared.NextSingle() * 0.1f;
                float speed = 130 + Random.Shared.NextSingle() * 110;
                Vector3 velocity = new(MathF.Cos(angle) * speed, MathF.Sin(angle) * speed, 0);
                SpriteVisual particle = compositor.CreateSpriteVisual();
                particle.Size = new Vector2(5 + Random.Shared.NextSingle() * 3);
                particle.AnchorPoint = new Vector2(0.5f);
                particle.Offset = origin;
                particle.Opacity = 0;
                particle.RotationAngle = angle;
                particle.Brush = _victoryBrushes[(burst + spark % 2) % _victoryBrushes.Count];
                particle.Properties.InsertScalar("Progress", 0);
                _victoryVisual.Children.InsertAtTop(particle);
                _victoryParticles.Add(particle);

                using ExpressionAnimation trajectory = compositor.CreateExpressionAnimation(
                    "origin + velocity * clock.Progress + gravity * clock.Progress * clock.Progress");
                trajectory.SetVector3Parameter("origin", origin);
                trajectory.SetVector3Parameter("velocity", velocity * SparkLifetimeSeconds);
                trajectory.SetVector3Parameter("gravity", new Vector3(0, 80 * SparkLifetimeSeconds * SparkLifetimeSeconds, 0));
                trajectory.SetReferenceParameter("clock", particle.Properties);
                particle.StartAnimation("Offset", trajectory);
                particle.StartAnimation("Opacity", fade);
                particle.Properties.StartAnimation("Progress", progress);
            }
        }
    }

    private void OnVictoryTimerTick(DispatcherQueueTimer sender, object args) => StopVictoryAnimation();

    private void StopVictoryAnimation()
    {
        if (_victoryTimer is { } timer)
        {
            timer.Stop();
            timer.Tick -= OnVictoryTimerTick;
            _victoryTimer = null;
        }
        if (_victoryVisual is { } visual)
        {
            ElementCompositionPreview.SetElementChildVisual(VictoryLayer, null);
            visual.Children.RemoveAll();
            foreach (SpriteVisual particle in _victoryParticles)
            {
                particle.StopAnimation("Offset");
                particle.StopAnimation("Opacity");
                particle.Properties.StopAnimation("Progress");
                particle.Dispose();
            }
            _victoryParticles.Clear();
            visual.Dispose();
            _victoryVisual = null;
        }
        foreach (CompositionColorBrush brush in _victoryBrushes)
            brush.Dispose();
        _victoryBrushes.Clear();
        VictoryBanner.Visibility = Visibility.Collapsed;
        VictoryMessage.Text = "";
    }
}
