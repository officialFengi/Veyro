using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Veyro;

public sealed class VolumeSlider : Slider
{
    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (Template.FindName("PART_Track", this) is not Track track || !CaptureMouse())
        {
            base.OnPreviewMouseLeftButtonDown(e);
            return;
        }

        Focus();
        UpdateFromPointer(e, track);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsMouseCaptured && Template.FindName("PART_Track", this) is Track track)
        {
            UpdateFromPointer(e, track);
            e.Handled = true;
        }
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
            e.Handled = true;
        }
        base.OnPreviewMouseLeftButtonUp(e);
    }

    private void UpdateFromPointer(MouseEventArgs e, Track track)
    {
        var halfThumb = track.Thumb.ActualWidth / 2;
        var travel = track.ActualWidth - 2 * halfThumb;
        if (travel <= 0)
            return;

        var fraction = Math.Clamp((e.GetPosition(track).X - halfThumb) / travel, 0, 1);
        SetCurrentValue(ValueProperty, Math.Round(Minimum + fraction * (Maximum - Minimum)));
    }
}


