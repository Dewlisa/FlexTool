using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace FlexTool;

/// <summary>
/// Helper class for drag-and-drop reordering of mod cards in the mod manager.
/// Enables users to reorder mods by dragging them to new positions.
/// The drag only starts after the mouse moves past the system drag threshold,
/// so normal clicks (e.g. the ON/OFF toggle inside a card) keep working.
/// </summary>
public static class ModDragDropHelper
{
    private static RimWorldSaveReader.ModInfo? _draggedMod;
    private static RimWorldSaveReader.ModInfo? _pressedMod;
    private static Point _dragStartPoint;
    private static Border? _dropIndicator;
    private static FrameworkElement? _indicatorTarget;
    private static bool _indicatorBelow;

    /// <summary>
    /// Initializes drag handlers for a mod card.
    /// The callback receives (draggedMod, targetMod, insertBelowTarget) when a drop completes.
    /// </summary>
    public static void EnableDragForCard(Border card, RimWorldSaveReader.ModInfo mod,
        Action<RimWorldSaveReader.ModInfo, RimWorldSaveReader.ModInfo, bool> onReorder)
    {
        card.AllowDrop = true;
        card.Tag = mod;

        // Record where the mouse went down; the drag starts later in PreviewMouseMove
        // once the pointer travels past the system drag threshold. Starting the drag
        // directly in PreviewMouseLeftButtonDown would swallow the mouse-up event and
        // break click handlers (like the enable/disable toggle) inside the card.
        card.PreviewMouseLeftButtonDown += (_, e) =>
        {
            _pressedMod = mod;
            _dragStartPoint = e.GetPosition(null);
        };

        card.PreviewMouseLeftButtonUp += (_, _) => _pressedMod = null;

        card.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                _pressedMod = null;
                return;
            }

            if (_draggedMod != null || !ReferenceEquals(_pressedMod, mod))
                return;

            var pos = e.GetPosition(null);
            if (Math.Abs(pos.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pos.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            _pressedMod = null;
            _draggedMod = mod;
            card.Opacity = 0.55;
            try
            {
                DragDrop.DoDragDrop(card, new DataObject(DataFormats.Serializable, mod.PackageId), DragDropEffects.Move);
            }
            finally
            {
                card.Opacity = 1.0;
                _draggedMod = null;
                RemoveDropIndicator();
            }
        };

        // Drag over: show the drop indicator above/below the hovered card
        card.DragOver += (_, e) =>
        {
            e.Handled = true;

            if (_draggedMod == null || ReferenceEquals(_draggedMod, mod))
            {
                e.Effects = DragDropEffects.None;
                if (_draggedMod != null)
                    RemoveDropIndicator();
                return;
            }

            e.Effects = DragDropEffects.Move;
            bool isBelow = e.GetPosition(card).Y > card.ActualHeight / 2;
            ShowDropIndicator(card, isBelow);
        };

        // Drop: report the move and let the caller persist it against the real load order
        card.Drop += (_, e) =>
        {
            e.Handled = true;
            RemoveDropIndicator();

            var dragged = _draggedMod;
            if (dragged == null || ReferenceEquals(dragged, mod))
                return;

            e.Effects = DragDropEffects.Move;
            bool insertBelow = e.GetPosition(card).Y > card.ActualHeight / 2;

            // Defer until DoDragDrop unwinds so the caller can safely rebuild the UI
            card.Dispatcher.BeginInvoke(DispatcherPriority.Background,
                new Action(() => onReorder(dragged, mod, insertBelow)));
        };
    }

    private static void ShowDropIndicator(FrameworkElement targetCard, bool isBelow)
    {
        // Avoid remove/insert churn while hovering the same position
        if (_dropIndicator != null && ReferenceEquals(_indicatorTarget, targetCard) && _indicatorBelow == isBelow)
            return;

        RemoveDropIndicator();

        if (targetCard.Parent is not Panel panel)
            return;

        var index = panel.Children.IndexOf(targetCard);
        if (index < 0)
            return;

        _dropIndicator = new Border
        {
            Height = 3,
            Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x7B, 0xD5)),
            CornerRadius = new CornerRadius(1.5),
            Margin = new Thickness(0, 0, 0, 4),
            IsHitTestVisible = false
        };
        _indicatorTarget = targetCard;
        _indicatorBelow = isBelow;

        panel.Children.Insert(isBelow ? index + 1 : index, _dropIndicator);
    }

    private static void RemoveDropIndicator()
    {
        if (_dropIndicator?.Parent is Panel panel)
            panel.Children.Remove(_dropIndicator);
        _dropIndicator = null;
        _indicatorTarget = null;
    }
}
