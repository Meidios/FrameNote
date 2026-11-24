using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FrameNote;

internal sealed class PointerLight : Border
{
    readonly RadialGradientBrush mask = new(Color.FromArgb(43, 255, 255, 255), Colors.Transparent) {
        MappingMode = BrushMappingMode.Absolute, RadiusX = 64, RadiusY = 64
    };
    UIElement? group;
    bool lit;

    public PointerLight()
    {
        IsHitTestVisible = false;
        SetResourceReference(CornerRadiusProperty, "ControlInnerCorners");
        SetResourceReference(BackgroundProperty, "ink");
        OpacityMask = mask;
        Opacity = 0;
        Loaded += Attach;
        Unloaded += Detach;
        IsEnabledChanged += (_, _) => { if (!IsEnabled) Reset(); };
    }

    void Attach(object sender, RoutedEventArgs e)
    {
        if (TemplatedParent is not Control control) return;
        // Siblings share pointer input so a button can catch the light across a gap.
        group = VisualTreeHelper.GetParent(control) as Panel ?? (UIElement)control;
        group.MouseMove += Move;
        group.MouseLeave += Leave;
    }

    void Detach(object sender, RoutedEventArgs e)
    {
        if (group == null) return;
        group.MouseMove -= Move;
        group.MouseLeave -= Leave;
        group = null;
        Reset();
    }

    void Move(object sender, MouseEventArgs e)
    {
        if (!IsEnabled || !IsVisible) return;
        var p = e.GetPosition(this);
        bool near = p.X > -mask.RadiusX && p.X < ActualWidth + mask.RadiusX &&
                    p.Y > -mask.RadiusY && p.Y < ActualHeight + mask.RadiusY;
        if (near) mask.Center = mask.GradientOrigin = p;
        Fade(near);
    }

    void Leave(object sender, MouseEventArgs e) => Fade(false);

    void Fade(bool on)
    {
        if (lit == on) return;
        lit = on;
        var duration = TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? 140 : 0);
        BeginAnimation(OpacityProperty, new DoubleAnimation(on ? 1 : 0, duration));
    }

    void Reset()
    {
        lit = false;
        BeginAnimation(OpacityProperty, null);
    }
}
