using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace Veyro;

public partial class MainWindow
{
    private Point reorderStart;
    private FrameworkElement? draggedLabel;
    private MixerChannel? draggedChannel;
    private MixerChannel? reorderTarget;
    private bool insertAfter;
    private Grid? draggedGrid;
    private LiftedRow? liftedRow;
    private AdornerLayer? dragLayer;
    private Point grabOffset;
    private readonly List<(Grid Grid, double Top, MixerChannel Row)> dragRows = new();

    private void BeginReorder(object sender, MouseButtonEventArgs e)
    {
        reorderStart = e.GetPosition(SessionScroll);
    }

    private void MoveReorder(object sender, MouseEventArgs e)
    {
        if (draggedLabel is not null)
        {
            UpdateDrag(e.GetPosition(SessionScroll));
            e.Handled = true;
            return;
        }
        if (e.LeftButton != MouseButtonState.Pressed ||
            sender is not FrameworkElement { DataContext: MixerChannel row } label ||
            row.IsOutput || row.Key.StartsWith("group:"))
            return;
        var delta = e.GetPosition(SessionScroll) - reorderStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        dragLayer = AdornerLayer.GetAdornerLayer(SessionScroll);
        if (dragLayer is null) return;
        draggedGrid = FindRow(label);
        if (draggedGrid is null) return;
        timer.Stop();
        draggedLabel = label;
        draggedChannel = row;
        reorderTarget = null;
        dragRows.Clear();
        foreach (var grid in VisualDescendants<Grid>(MixerRows).Where(grid => grid.Name == "ChannelRow"))
        {
            if (grid.DataContext is not MixerChannel channel || channel.IsOutput) continue;
            var top = grid.TranslatePoint(new Point(), SessionScroll).Y + SessionScroll.VerticalOffset;
            dragRows.Add((grid, top, channel));
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(draggedGrid.ActualWidth),
            (int)Math.Ceiling(draggedGrid.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(draggedGrid);
        bitmap.Freeze();
        var origin = draggedGrid.TranslatePoint(new Point(), SessionScroll);
        grabOffset = new Point(reorderStart.X - origin.X, reorderStart.Y - origin.Y);
        liftedRow = new LiftedRow(SessionScroll, bitmap, draggedGrid.RenderSize);
        dragLayer.Add(liftedRow);
        draggedGrid.Opacity = 0.16;
        foreach (var item in dragRows.Where(item => item.Grid != draggedGrid))
        {
            var rotate = new RotateTransform();
            var transforms = new TransformGroup();
            transforms.Children.Add(new TranslateTransform());
            transforms.Children.Add(rotate);
            item.Grid.RenderTransformOrigin = new Point(0.5, 0.5);
            item.Grid.RenderTransform = transforms;
            var wobble = new DoubleAnimation(-0.45, 0.45,
                TimeSpan.FromMilliseconds(180))
            {
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever
            };
            Timeline.SetDesiredFrameRate(wobble, 30);
            rotate.BeginAnimation(RotateTransform.AngleProperty, wobble);
        }
        label.CaptureMouse();
        UpdateDrag(e.GetPosition(SessionScroll));
        e.Handled = true;
    }

    private void UpdateDrag(Point pointer)
    {
        if (liftedRow is null || draggedGrid is null) return;
        liftedRow.Offset = new Point(pointer.X - grabOffset.X, pointer.Y - grabOffset.Y);
        liftedRow.InvalidateVisual();
        var contentY = pointer.Y + SessionScroll.VerticalOffset;
        var target = dragRows.Where(item => item.Grid != draggedGrid)
            .OrderBy(item => Math.Abs(item.Top + item.Grid.ActualHeight / 2 - contentY)).FirstOrDefault();
        if (target.Grid is null) return;
        var after = contentY > target.Top + target.Grid.ActualHeight / 2;
        if (reorderTarget == target.Row && insertAfter == after) return;
        reorderTarget = target.Row;
        insertAfter = after;
        var sourceTop = dragRows.First(item => item.Grid == draggedGrid).Top;
        var insertionY = target.Top + (after ? target.Grid.ActualHeight : 0);
        var gap = draggedGrid.ActualHeight + draggedGrid.Margin.Bottom;
        foreach (var item in dragRows.Where(item => item.Grid != draggedGrid))
        {
            var shift = 0.0;
            if (insertionY < sourceTop && item.Top >= target.Top + (after ? 1 : 0) && item.Top < sourceTop)
                shift = gap;
            if (insertionY > sourceTop && item.Top > sourceTop && item.Top <= target.Top - (after ? 0 : 1))
                shift = -gap;
            var translate = (TranslateTransform)((TransformGroup)item.Grid.RenderTransform).Children[0];
            translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(shift, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        }
    }

    private void ScrollReorder(object sender, MouseWheelEventArgs e)
    {
        if (draggedLabel is null) return;
        SessionScroll.ScrollToVerticalOffset(SessionScroll.VerticalOffset - Math.Sign(e.Delta) * 80);
        SessionScroll.UpdateLayout();
        UpdateDrag(e.GetPosition(SessionScroll));
        e.Handled = true;
    }

    private void FinishReorder(object sender, MouseButtonEventArgs e)
    {
        if (draggedLabel is null) return;
        var source = draggedChannel;
        var target = reorderTarget;
        var after = insertAfter;
        EndDrag();
        if (source is not null && target is not null)
            ReorderApplication(source.Key, target, after);
        RefreshMixer();
        e.Handled = true;
    }

    private void CancelReorder(object sender, MouseEventArgs e)
    {
        if (draggedLabel is not null) EndDrag();
    }

    private void EndDrag()
    {
        var label = draggedLabel;
        draggedLabel = null;
        if (liftedRow is not null) dragLayer?.Remove(liftedRow);
        liftedRow = null;
        dragLayer = null;
        draggedGrid = null;
        draggedChannel = null;
        reorderTarget = null;
        foreach (var item in dragRows)
        {
            item.Grid.RenderTransform = Transform.Identity;
            item.Grid.Opacity = 1;
        }
        dragRows.Clear();
        label?.ReleaseMouseCapture();
        if (IsVisible && SettingsPanel.Visibility != Visibility.Visible && FolderPanel.Visibility != Visibility.Visible)
            timer.Start();
    }

    private static Grid? FindRow(DependencyObject element)
    {
        for (var parent = element; parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is Grid { Name: "ChannelRow" } grid) return grid;
        return null;
    }

    private static IEnumerable<T> VisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in VisualDescendants<T>(child)) yield return descendant;
        }
    }

    private sealed class LiftedRow(UIElement owner, ImageSource image, Size size) : Adorner(owner)
    {
        public Point Offset { get; set; }
        protected override void OnRender(DrawingContext context)
        {
            IsHitTestVisible = false;
            context.PushTransform(new TranslateTransform(Offset.X, Offset.Y));
            context.PushTransform(new ScaleTransform(1.025, 1.025, size.Width / 2, size.Height / 2));
            context.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(50, 0, 0, 0)), null,
                new Rect(0, 6, size.Width, size.Height), 40, 40);
            context.DrawImage(image, new Rect(new Point(), size));
            context.Pop();
            context.Pop();
        }
    }
    private void ReorderApplication(string key, MixerChannel target, bool after)
    {
        var source = rows.First(row => row.Key == key);
        var category = target.Key.StartsWith("group:")
            ? target.Key.Substring(6) : CategoryFor(target.Key, target.Name);
        if (CategoryFor(source.Key, source.Name) != category)
            preferences.Assignments![key] = category;

        var order = rows.Where(row => row.Key != "master")
            .OrderBy(row =>
            {
                var index = preferences.AppOrder!.IndexOf(row.Key);
                return index < 0 ? int.MaxValue : index;
            }).ThenBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(row => row.Key).ToList();
        order.Remove(key);
        var targetIndex = order.IndexOf(target.Key);
        var insertion = targetIndex < 0 ? order.Count : targetIndex + (after ? 1 : 0);
        order.Insert(insertion, key);
        // Keep saved positions of applications which are currently closed.
        preferences.AppOrder!.RemoveAll(order.Contains);
        preferences.AppOrder.InsertRange(0, order);
        if (category.Length > 0) expandedGroups.Add("group:" + category);
        SavePreferences();
    }
}



