using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FlexTool;

/// <summary>
/// Provides efficient mod list rendering using virtualization.
/// Handles large mod collections (100+) without performance degradation.
/// </summary>
public class VirtualizedModList
{
    private readonly List<RimWorldSaveReader.ModInfo> _allMods;
    private readonly List<RimWorldSaveReader.ModInfo> _visibleMods;
    private readonly int _batchSize;

    public VirtualizedModList(List<RimWorldSaveReader.ModInfo> mods, int batchSize = ModManagerConstants.Thresholds.LAZY_LOAD_BATCH_SIZE)
    {
        _allMods = mods;
        _visibleMods = new List<RimWorldSaveReader.ModInfo>();
        _batchSize = batchSize;
    }

    /// <summary>
    /// Gets the recommended panel type based on mod count.
    /// </summary>
    public static string GetRecommendedPanelType(int modCount)
    {
        return modCount > ModManagerConstants.Thresholds.VIRTUALIZATION_THRESHOLD
            ? "VirtualizingStackPanel"
            : "StackPanel";
    }

    /// <summary>
    /// Loads the initial batch of mods.
    /// </summary>
    public void LoadInitialBatch()
    {
        _visibleMods.Clear();
        var count = Math.Min(_batchSize, _allMods.Count);
        _visibleMods.AddRange(_allMods.Take(count));
    }

    /// <summary>
    /// Loads the next batch of mods.
    /// </summary>
    public bool LoadNextBatch()
    {
        if (_visibleMods.Count >= _allMods.Count)
            return false;

        var startIdx = _visibleMods.Count;
        var endIdx = Math.Min(startIdx + _batchSize, _allMods.Count);
        var toAdd = _allMods.Skip(startIdx).Take(endIdx - startIdx);

        _visibleMods.AddRange(toAdd);
        return _visibleMods.Count < _allMods.Count;
    }

    /// <summary>
    /// Gets the currently visible mods.
    /// </summary>
    public List<RimWorldSaveReader.ModInfo> GetVisibleMods() => new(_visibleMods);

    /// <summary>
    /// Gets the total mod count.
    /// </summary>
    public int GetTotalCount() => _allMods.Count;

    /// <summary>
    /// Gets the number of loaded mods.
    /// </summary>
    public int GetLoadedCount() => _visibleMods.Count;

    /// <summary>
    /// Gets the number of remaining mods to load.
    /// </summary>
    public int GetRemainingCount() => _allMods.Count - _visibleMods.Count;

    /// <summary>
    /// Clears the visible list and reloads.
    /// </summary>
    public void Refresh()
    {
        LoadInitialBatch();
    }

    /// <summary>
    /// Creates a virtualized panel for efficient rendering.
    /// </summary>
    public Panel CreateVirtualizedPanel(List<UIElement> items)
    {
        if (items.Count > ModManagerConstants.Thresholds.VIRTUALIZATION_THRESHOLD)
        {
            var virtualizingPanel = new VirtualizingStackPanel();
            VirtualizingStackPanel.SetIsVirtualizing(virtualizingPanel, true);
            VirtualizingStackPanel.SetCacheLengthUnit(virtualizingPanel, VirtualizationCacheLengthUnit.Item);
            VirtualizingStackPanel.SetCacheLength(virtualizingPanel, new VirtualizationCacheLength(10));

            foreach (var item in items)
            {
                virtualizingPanel.Children.Add(item);
            }

            return virtualizingPanel;
        }
        else
        {
            var stackPanel = new StackPanel();
            foreach (var item in items)
            {
                stackPanel.Children.Add(item);
            }
            return stackPanel;
        }
    }

    /// <summary>
    /// Creates a scrollable viewport with virtualization for large lists.
    /// </summary>
    public ScrollViewer CreateVirtualizedScrollViewer(List<UIElement> items, double maxHeight = 500)
    {
        var panel = CreateVirtualizedPanel(items);

        var scrollViewer = new ScrollViewer
        {
            MaxHeight = maxHeight,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = panel
        };

        // Enable virtualization in the scroll viewer
        if (panel is VirtualizingStackPanel)
        {
            scrollViewer.PreviewMouseWheel += (s, e) =>
            {
                if (!e.Handled)
                {
                    e.Handled = true;
                    var eventArg = new System.Windows.Input.MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                    {
                        RoutedEvent = UIElement.MouseWheelEvent,
                        Source = s
                    };
                    ((UIElement)s).RaiseEvent(eventArg);
                }
            };
        }

        return scrollViewer;
    }
}

/// <summary>
/// Lazy-load helper for paginated mod list loading.
/// </summary>
public class LazyModListLoader
{
    private readonly List<RimWorldSaveReader.ModInfo> _allMods;
    private int _currentIndex = 0;
    private readonly int _pageSize;
    private readonly Action<List<RimWorldSaveReader.ModInfo>> _onBatchLoaded;

    public LazyModListLoader(
        List<RimWorldSaveReader.ModInfo> mods,
        int pageSize,
        Action<List<RimWorldSaveReader.ModInfo>> onBatchLoaded)
    {
        _allMods = mods;
        _pageSize = pageSize;
        _onBatchLoaded = onBatchLoaded;
    }

    /// <summary>
    /// Loads the next batch of mods asynchronously.
    /// </summary>
    public void LoadNextBatchAsync()
    {
        System.Threading.Tasks.Task.Run(() => LoadNextBatch());
    }

    /// <summary>
    /// Loads the next batch of mods.
    /// </summary>
    public bool LoadNextBatch()
    {
        if (_currentIndex >= _allMods.Count)
            return false;

        var endIndex = Math.Min(_currentIndex + _pageSize, _allMods.Count);
        var batch = _allMods.Skip(_currentIndex).Take(endIndex - _currentIndex).ToList();

        _currentIndex = endIndex;
        _onBatchLoaded?.Invoke(batch);

        return _currentIndex < _allMods.Count;
    }

    /// <summary>
    /// Resets the loader to the beginning.
    /// </summary>
    public void Reset()
    {
        _currentIndex = 0;
    }

    /// <summary>
    /// Gets the loading progress percentage.
    /// </summary>
    public double GetProgressPercentage() => _allMods.Count > 0 ? (_currentIndex * 100.0) / _allMods.Count : 0;

    /// <summary>
    /// Checks if there are more items to load.
    /// </summary>
    public bool HasMore() => _currentIndex < _allMods.Count;
}
