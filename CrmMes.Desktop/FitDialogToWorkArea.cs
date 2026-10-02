using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CrmMes.Desktop;

/// <summary>Dialogs sized for a full desktop (or SizeToContent forms with many fields) open taller than
/// the work area once the Windows taskbar and DPI scaling are accounted for: the footer with Salva /
/// Annulla ends up off-screen. Caps every window to the monitor work area, keeps footers visible by
/// scrolling the body when needed, and wraps crowded button rows so they don't clip horizontally.</summary>
public static class FitDialogToWorkArea
{
    private static readonly DependencyProperty AppliedProperty =
        DependencyProperty.RegisterAttached("Applied", typeof(bool), typeof(FitDialogToWorkArea));

    private const double ScreenMargin = 16;

    public static void Ensure(Window window)
    {
        if (Equals(window.GetValue(AppliedProperty), true))
        {
            return;
        }

        window.SetValue(AppliedProperty, true);

        void Apply()
        {
            CapToWorkArea(window);
            EnsureScrollableFooter(window);
            WrapCrowdedFooters(window);
            CapToWorkArea(window);
            ClampPosition(window);
        }

        if (window.IsLoaded)
        {
            Apply();
        }
        else
        {
            window.Loaded += (_, _) => Apply();
        }

        window.LocationChanged += (_, _) => ClampPosition(window);
        window.SizeChanged += (_, _) =>
        {
            CapToWorkArea(window);
            ClampPosition(window);
        };
    }

    private static void CapToWorkArea(Window window)
    {
        if (!TryGetWorkAreaDip(window, out var work))
        {
            return;
        }

        var maxH = Math.Max(240, work.Height - ScreenMargin);
        var maxW = Math.Max(320, work.Width - ScreenMargin);
        if (window.MaxHeight > maxH || double.IsInfinity(window.MaxHeight))
        {
            window.MaxHeight = maxH;
        }

        if (window.MaxWidth > maxW || double.IsInfinity(window.MaxWidth))
        {
            window.MaxWidth = maxW;
        }

        if (window.MinHeight > maxH)
        {
            window.MinHeight = maxH;
        }

        if (window.MinWidth > maxW)
        {
            window.MinWidth = maxW;
        }

        if (window.SizeToContent == SizeToContent.Manual
            && !double.IsNaN(window.Height)
            && window.Height > maxH)
        {
            window.Height = maxH;
        }

        if (window.SizeToContent == SizeToContent.Manual
            && !double.IsNaN(window.Width)
            && window.Width > maxW)
        {
            window.Width = maxW;
        }

        // SizeToContent windows that still measure taller than the work area must become fixed+scrollable.
        if (window.SizeToContent != SizeToContent.Manual
            && window.ActualHeight > maxH + 0.5)
        {
            window.SizeToContent = SizeToContent.Manual;
            window.Height = maxH;
        }
    }

    private static void ClampPosition(Window window)
    {
        if (window.WindowState != WindowState.Normal || !TryGetWorkAreaDip(window, out var work))
        {
            return;
        }

        var width = double.IsNaN(window.ActualWidth) || window.ActualWidth <= 0
            ? (double.IsNaN(window.Width) ? 0 : window.Width)
            : window.ActualWidth;
        var height = double.IsNaN(window.ActualHeight) || window.ActualHeight <= 0
            ? (double.IsNaN(window.Height) ? 0 : window.Height)
            : window.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var left = window.Left;
        var top = window.Top;
        if (double.IsNaN(left) || double.IsNaN(top))
        {
            return;
        }

        if (left + width > work.Right)
        {
            left = work.Right - width;
        }

        if (top + height > work.Bottom)
        {
            top = work.Bottom - height;
        }

        if (left < work.Left)
        {
            left = work.Left;
        }

        if (top < work.Top)
        {
            top = work.Top;
        }

        window.Left = left;
        window.Top = top;
    }

    /// <summary>SizeToContent forms put Annulla/Salva as the last row of a StackPanel. When the form
    /// is taller than the screen those buttons are clipped with no scrollbar — split them into a
    /// docked footer and scroll the fields above.</summary>
    private static void EnsureScrollableFooter(Window window)
    {
        if (window.Content is not StackPanel stack || stack.Children.Count < 2)
        {
            return;
        }

        if (!TryGetWorkAreaDip(window, out var work))
        {
            return;
        }

        var maxH = Math.Max(240, work.Height - ScreenMargin);
        // Only restructure when the natural height would overflow (or already does).
        stack.Measure(new Size(window.ActualWidth > 0 ? window.ActualWidth : window.Width, double.PositiveInfinity));
        if (stack.DesiredSize.Height + 40 < maxH && window.ActualHeight <= maxH + 0.5)
        {
            return;
        }

        FrameworkElement? footer = null;
        if (stack.Children[^1] is FrameworkElement last && LooksLikeFooter(last))
        {
            footer = last;
            stack.Children.RemoveAt(stack.Children.Count - 1);
        }

        var margin = stack.Margin;
        stack.Margin = new Thickness(0);
        window.Content = null;

        var dock = new DockPanel { Margin = margin };
        if (footer is not null)
        {
            footer.Margin = new Thickness(
                footer.Margin.Left,
                Math.Max(footer.Margin.Top, 12),
                footer.Margin.Right,
                footer.Margin.Bottom);
            DockPanel.SetDock(footer, Dock.Bottom);
            dock.Children.Add(footer);
        }

        dock.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = stack
        });

        window.Content = dock;
        window.SizeToContent = SizeToContent.Manual;
        window.Height = Math.Min(maxH, Math.Max(window.MinHeight, stack.DesiredSize.Height + (footer?.DesiredSize.Height ?? 48) + 48));
        if (window.Height > maxH)
        {
            window.Height = maxH;
        }

        if (window.ResizeMode == ResizeMode.NoResize)
        {
            window.ResizeMode = ResizeMode.CanResize;
        }
    }

    private static bool LooksLikeFooter(FrameworkElement element)
    {
        if (element is not Panel panel)
        {
            return false;
        }

        var interactive = panel.Children.OfType<FrameworkElement>()
            .Where(c => c.Visibility != Visibility.Collapsed)
            .ToList();
        if (interactive.Count == 0)
        {
            return false;
        }

        return interactive.All(c => c is Button)
               || (panel is StackPanel { Orientation: Orientation.Horizontal }
                   && interactive.OfType<Button>().Count() >= 1
                   && interactive.Count <= 6);
    }

    private static void WrapCrowdedFooters(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            WrapCrowdedFooters(VisualTreeHelper.GetChild(root, i));
        }

        if (root is not StackPanel { Orientation: Orientation.Horizontal } stack
            || stack.Parent is not Panel parent
            || stack.Children.OfType<Button>().Count() < 3)
        {
            return;
        }

        var dock = parent is DockPanel ? DockPanel.GetDock(stack) : Dock.Left;
        var isDockedFooter = parent is DockPanel && (dock is Dock.Bottom or Dock.Right);
        var isGridFooter = parent is Grid grid
                           && Grid.GetRow(stack) == grid.RowDefinitions.Count - 1
                           && grid.RowDefinitions.Count > 0;
        if (!isDockedFooter && !isGridFooter)
        {
            return;
        }

        var index = parent.Children.IndexOf(stack);
        if (index < 0)
        {
            return;
        }

        var wrap = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = stack.HorizontalAlignment == HorizontalAlignment.Stretch
                ? HorizontalAlignment.Right
                : stack.HorizontalAlignment,
            VerticalAlignment = stack.VerticalAlignment,
            Margin = stack.Margin
        };
        while (stack.Children.Count > 0)
        {
            var child = stack.Children[0];
            stack.Children.RemoveAt(0);
            wrap.Children.Add(child);
        }

        if (parent is DockPanel)
        {
            DockPanel.SetDock(wrap, dock);
        }
        else if (parent is Grid)
        {
            Grid.SetRow(wrap, Grid.GetRow(stack));
            Grid.SetColumn(wrap, Grid.GetColumn(stack));
            Grid.SetColumnSpan(wrap, Grid.GetColumnSpan(stack));
        }

        parent.Children.RemoveAt(index);
        parent.Children.Insert(index, wrap);
    }

    internal static bool TryGetWorkAreaDip(Window window, out Rect work)
    {
        work = default;
        var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            handle = new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
        }

        if (!MaximizeToWorkArea.TryGetWorkArea(handle, out var pxWork, out _))
        {
            return false;
        }

        var source = PresentationSource.FromVisual(window)
                     ?? System.Windows.Interop.HwndSource.FromHwnd(handle);
        if (source?.CompositionTarget is null)
        {
            // Fallback: assume 96 DPI.
            work = new Rect(pxWork.Left, pxWork.Top, pxWork.Right - pxWork.Left, pxWork.Bottom - pxWork.Top);
            return work.Height > 0 && work.Width > 0;
        }

        var fromDevice = source.CompositionTarget.TransformFromDevice;
        var topLeft = fromDevice.Transform(new Point(pxWork.Left, pxWork.Top));
        var bottomRight = fromDevice.Transform(new Point(pxWork.Right, pxWork.Bottom));
        work = new Rect(topLeft, bottomRight);
        return work.Height > 0 && work.Width > 0;
    }
}
