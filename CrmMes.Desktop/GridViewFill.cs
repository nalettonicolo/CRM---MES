using System.Windows;
using System.Windows.Controls;

namespace CrmMes.Desktop;

/// <summary>Makes a ListView's GridView columns fill the available width. GridView only knows fixed
/// widths, so on a wide screen every table stopped halfway with an empty band on the right. Enabled for
/// every ListView by the implicit style in Styles.xaml.
///
/// Each column keeps the width declared in XAML as its minimum and proportion; the spare space is shared
/// out in proportion to those widths. Columns with no header text (action buttons like "Disattiva") keep
/// their declared width, since a stretched button looks broken.</summary>
public static class GridViewFill
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(GridViewFill), new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyProperty BaseWidthProperty = DependencyProperty.RegisterAttached(
        "BaseWidth", typeof(double), typeof(GridViewFill), new PropertyMetadata(double.NaN));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListView listView)
        {
            return;
        }

        if (e.NewValue is true)
        {
            listView.SizeChanged += ListView_SizeChanged;
            listView.Loaded += ListView_Loaded;
        }
        else
        {
            listView.SizeChanged -= ListView_SizeChanged;
            listView.Loaded -= ListView_Loaded;
        }
    }

    private static void ListView_Loaded(object sender, RoutedEventArgs e) => Resize((ListView)sender);

    private static void ListView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged)
        {
            Resize((ListView)sender);
        }
    }

    /// <summary>Space reserved for the vertical scrollbar, borders and the row padding, so the last
    /// column never pushes a horizontal scrollbar into view.</summary>
    private const double Reserved = 36;

    internal static void Resize(ListView listView)
    {
        if (listView.View is not GridView gridView || gridView.Columns.Count == 0 || listView.ActualWidth <= 0)
        {
            return;
        }

        var columns = gridView.Columns.ToList();
        foreach (var column in columns)
        {
            if (double.IsNaN((double)column.GetValue(BaseWidthProperty)))
            {
                column.SetValue(BaseWidthProperty, double.IsNaN(column.Width) ? Math.Max(column.ActualWidth, 60) : column.Width);
            }
        }

        var widths = Distribute(
            columns.Select(column => ((double)column.GetValue(BaseWidthProperty), IsStretchable(column))).ToList(),
            listView.ActualWidth - Reserved);
        for (var i = 0; i < columns.Count; i++)
        {
            columns[i].Width = widths[i];
        }
    }

    /// <summary>Pure layout rule (unit-tested): stretchable columns share the spare width in proportion to
    /// their base width; fixed ones, and every column when there's no spare width, keep their base.</summary>
    internal static double[] Distribute(IReadOnlyList<(double BaseWidth, bool Stretchable)> columns, double available)
    {
        var total = columns.Sum(column => column.BaseWidth);
        var stretchableTotal = columns.Where(column => column.Stretchable).Sum(column => column.BaseWidth);
        var spare = available - total;
        if (spare <= 0 || stretchableTotal <= 0)
        {
            return columns.Select(column => column.BaseWidth).ToArray();
        }

        return columns
            .Select(column => column.Stretchable
                ? Math.Floor(column.BaseWidth + spare * column.BaseWidth / stretchableTotal)
                : column.BaseWidth)
            .ToArray();
    }

    private static bool IsStretchable(GridViewColumn column) =>
        column.Header is string header ? !string.IsNullOrWhiteSpace(header) : column.Header is not null;
}
