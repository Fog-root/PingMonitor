using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DotaPingMonitor.Models;

namespace DotaPingMonitor.Views;

/// <summary>
/// Анимированная визуальная оболочка оверлея: непрерывный световой поток Ambient Flow.
/// Оптимизирована для нулевого потребления ресурсов при скрытии и выключении.
/// </summary>
public partial class OverlayVisualEffectControl : UserControl
{
    private Storyboard? _flowStoryboard;
    private bool _isEffectActive;
    private bool _isFading;
    private OverlayVisualEffectStyle _style = OverlayVisualEffectStyle.AmbientFlow;

    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(
            nameof(CornerRadius),
            typeof(CornerRadius),
            typeof(OverlayVisualEffectControl),
            new PropertyMetadata(new CornerRadius(16), OnCornerRadiusChanged));

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public OverlayVisualEffectStyle GlowStyle
    {
        get => _style;
        set
        {
            _style = value;
            ApplyStyle(value);
        }
    }

    public bool IsEffectActive => _isEffectActive;

    public OverlayVisualEffectControl()
    {
        InitializeComponent();
        Loaded += OverlayVisualEffectControl_Loaded;
        Unloaded += OverlayVisualEffectControl_Unloaded;
    }

    private void OverlayVisualEffectControl_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateBordersCornerRadius(CornerRadius);
        if (_isEffectActive)
        {
            StartStoryboard();
        }
    }

    private void OverlayVisualEffectControl_Unloaded(object sender, RoutedEventArgs e)
    {
        StopStoryboard();
    }

    private static void OnCornerRadiusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is OverlayVisualEffectControl control && e.NewValue is CornerRadius newRadius)
        {
            control.UpdateBordersCornerRadius(newRadius);
        }
    }

    public void UpdateBordersCornerRadius(CornerRadius radius)
    {
        double tl = radius.TopLeft;
        double tr = radius.TopRight;
        double br = radius.BottomRight;
        double bl = radius.BottomLeft;

        if (CoreContourBorder != null)
        {
            CoreContourBorder.CornerRadius = radius;
        }

        if (HaloGlowBorder != null)
        {
            HaloGlowBorder.CornerRadius = radius;
        }
    }

    private void EnsureStoryboard()
    {
        if (_flowStoryboard != null) return;

        var brush = TryFindResource("AmbientFlowBrush") as LinearGradientBrush;
        var translation = brush?.RelativeTransform as TranslateTransform;

        _flowStoryboard = new Storyboard();

        // 1. Непрерывный плавный горизонтальный световой поток (Ambient Wave Flow)
        if (translation != null && SystemParameters.ClientAreaAnimation)
        {
            var translateAnim = new DoubleAnimation
            {
                From = 0.0,
                To = -1.0,
                Duration = TimeSpan.FromSeconds(6.0),
                RepeatBehavior = RepeatBehavior.Forever
            };
            Storyboard.SetTarget(translateAnim, translation);
            Storyboard.SetTargetProperty(translateAnim, new PropertyPath(TranslateTransform.XProperty));
            _flowStoryboard.Children.Add(translateAnim);
        }

        // 2. Деликатная органическая пульсация интенсивности внешнего ореола (Atmospheric Breathing)
        if (HaloGlowBorder != null && SystemParameters.ClientAreaAnimation)
        {
            var pulseAnim = new DoubleAnimation
            {
                From = 0.55,
                To = 0.88,
                Duration = TimeSpan.FromSeconds(3.0),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            Storyboard.SetTarget(pulseAnim, HaloGlowBorder);
            Storyboard.SetTargetProperty(pulseAnim, new PropertyPath(UIElement.OpacityProperty));
            _flowStoryboard.Children.Add(pulseAnim);
        }
    }

    private void StartStoryboard()
    {
        EnsureStoryboard();
        _flowStoryboard?.Begin(this, isControllable: true);
    }

    private void StopStoryboard()
    {
        _flowStoryboard?.Stop(this);
    }

    /// <summary>
    /// Плавное включение визуального эффекта с мягкой анимацией проявления.
    /// </summary>
    public void Activate(bool animated = true)
    {
        if (_isEffectActive && !_isFading) return;
        _isEffectActive = true;
        _isFading = false;

        Visibility = Visibility.Visible;
        StartStoryboard();

        if (!animated || !SystemParameters.ClientAreaAnimation)
        {
            EffectContainer.BeginAnimation(UIElement.OpacityProperty, null);
            EffectContainer.Opacity = 1.0;
            return;
        }

        var fadeIn = new DoubleAnimation
        {
            From = EffectContainer.Opacity,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(280),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        fadeIn.Completed += (_, _) =>
        {
            if (_isEffectActive)
            {
                EffectContainer.Opacity = 1.0;
            }
        };
        EffectContainer.BeginAnimation(UIElement.OpacityProperty, fadeIn);
    }

    /// <summary>
    /// Плавное выключение эффекта со снятием всей нагрузки с GPU/CPU после затухания.
    /// </summary>
    public void Deactivate(bool animated = true)
    {
        if (!_isEffectActive && !_isFading) return;
        _isEffectActive = false;

        if (!animated || !SystemParameters.ClientAreaAnimation)
        {
            StopStoryboard();
            EffectContainer.BeginAnimation(UIElement.OpacityProperty, null);
            EffectContainer.Opacity = 0.0;
            Visibility = Visibility.Collapsed;
            _isFading = false;
            return;
        }

        _isFading = true;
        var fadeOut = new DoubleAnimation
        {
            From = EffectContainer.Opacity,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };

        fadeOut.Completed += (_, _) =>
        {
            if (!_isEffectActive)
            {
                StopStoryboard();
                EffectContainer.BeginAnimation(UIElement.OpacityProperty, null);
                EffectContainer.Opacity = 0.0;
                Visibility = Visibility.Collapsed;
            }
            _isFading = false;
        };

        EffectContainer.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    public void Toggle(bool animated = true)
    {
        if (_isEffectActive)
        {
            Deactivate(animated);
        }
        else
        {
            Activate(animated);
        }
    }

    public void ApplyStyle(OverlayVisualEffectStyle style)
    {
        _style = style;
        if (HaloDropShadow != null)
        {
            HaloDropShadow.Color = (Color)ColorConverter.ConvertFromString("#00D2FF");
            HaloDropShadow.BlurRadius = 12;
            HaloDropShadow.Opacity = 0.58;
        }
    }
}
