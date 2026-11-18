using System.Windows;
using System.Windows.Controls;

namespace SmartWater.App;

/// <summary>نافذة إدخال نص بسيطة (أو عرض نص للنسخ فقط).</summary>
public static class InputDialog
{
    public static string? Ask(string title, string label, string initial = "", bool readOnly = false, Window? owner = null)
    {
        var w = new Window
        {
            Title = title, Width = 520, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterScreen,
            FlowDirection = FlowDirection.RightToLeft, ResizeMode = ResizeMode.NoResize, Owner = owner,
        };
        var box = new TextBox { Text = initial, IsReadOnly = readOnly, Margin = new Thickness(0, 6, 0, 10), TextWrapping = TextWrapping.Wrap, MinHeight = 28 };
        string? result = null;
        var ok = new Button { Content = readOnly ? "إغلاق" : "موافق", Padding = new Thickness(18, 5, 18, 5), IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "إلغاء", Padding = new Thickness(18, 5, 18, 5), IsCancel = true, Visibility = readOnly ? Visibility.Collapsed : Visibility.Visible };
        ok.Click += (_, _) => { result = box.Text; w.DialogResult = true; };
        var btns = new StackPanel { Orientation = Orientation.Horizontal };
        btns.Children.Add(ok); btns.Children.Add(cancel);
        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(box); panel.Children.Add(btns);
        w.Content = panel;
        box.Focus();
        return w.ShowDialog() == true ? result : null;
    }
}
