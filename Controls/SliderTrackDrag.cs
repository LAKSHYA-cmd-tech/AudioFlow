using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace AudioFlow.Controls;

/// <summary>Continues a track press as a drag; thumb presses keep native WPF behavior.</summary>
public static class SliderTrackDrag
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(SliderTrackDrag), new PropertyMetadata(false, EnabledChanged));
    private static readonly DependencyProperty DraggingProperty = DependencyProperty.RegisterAttached(
        "Dragging", typeof(bool), typeof(SliderTrackDrag), new PropertyMetadata(false));

    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

    private static void EnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Slider slider) return;
        if ((bool)e.NewValue)
        {
            // Slider's class handler marks move-to-point presses handled before instance handlers.
            slider.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(Press), true);
            slider.PreviewMouseMove += Move;
            slider.PreviewMouseLeftButtonUp += Release;
            slider.LostMouseCapture += LostCapture;
            slider.Unloaded += Unloaded;
        }
        else
        {
            End(slider);
            slider.RemoveHandler(UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(Press));
            slider.PreviewMouseMove -= Move;
            slider.PreviewMouseLeftButtonUp -= Release;
            slider.LostMouseCapture -= LostCapture;
            slider.Unloaded -= Unloaded;
        }
    }

    private static Track? GetTrack(Slider slider) => slider.Template?.FindName("PART_Track", slider) as Track;

    private static void Press(object sender, MouseButtonEventArgs e)
    {
        var slider = (Slider)sender;
        // Let the native thumb preserve its drag offset and keyboard behavior.
        for (var node = e.OriginalSource as DependencyObject; node is not null && node != slider;)
        {
            if (node is Thumb) return;
            node = node is Visual ? VisualTreeHelper.GetParent(node) : null;
        }
        var track = GetTrack(slider);
        if (track is null || !slider.IsEnabled) return;
        slider.Focus();
        if (!slider.CaptureMouse()) return;
        slider.SetValue(DraggingProperty, true);
        SetPosition(slider, track, e.GetPosition(track));
        e.Handled = true;
    }

    private static void Move(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var slider = (Slider)sender;
        if (!(bool)slider.GetValue(DraggingProperty)) return;
        if (e.LeftButton != MouseButtonState.Pressed) { End(slider); return; }
        if (GetTrack(slider) is Track track) SetPosition(slider, track, e.GetPosition(track));
        e.Handled = true;
    }

    private static void Release(object sender, MouseButtonEventArgs e)
    {
        var slider = (Slider)sender;
        if (!(bool)slider.GetValue(DraggingProperty)) return;
        if (GetTrack(slider) is Track track) SetPosition(slider, track, e.GetPosition(track));
        End(slider);
        e.Handled = true;
    }

    private static void SetPosition(Slider slider, Track track, System.Windows.Point point)
    {
        // Absolute geometry avoids depending on the thumb's last layout position during fast moves.
        var horizontal = slider.Orientation == System.Windows.Controls.Orientation.Horizontal;
        var thumbSize = horizontal ? track.Thumb.ActualWidth : track.Thumb.ActualHeight;
        var length = (horizontal ? track.ActualWidth : track.ActualHeight) - thumbSize;
        if (length <= 0) return;
        var fraction = ((horizontal ? point.X : point.Y) - thumbSize / 2) / length;
        if (!horizontal) fraction = 1 - fraction;
        if (slider.IsDirectionReversed) fraction = 1 - fraction;
        var value = slider.Minimum + Math.Clamp(fraction, 0, 1) * (slider.Maximum - slider.Minimum);
        if (double.IsFinite(value)) slider.SetCurrentValue(Slider.ValueProperty, value);
    }

    private static void End(Slider slider)
    {
        slider.SetValue(DraggingProperty, false);
        if (slider.IsMouseCaptured) slider.ReleaseMouseCapture();
    }
    private static void LostCapture(object sender, System.Windows.Input.MouseEventArgs e) =>
        ((Slider)sender).SetValue(DraggingProperty, false);
    private static void Unloaded(object sender, RoutedEventArgs e) => End((Slider)sender);
}
