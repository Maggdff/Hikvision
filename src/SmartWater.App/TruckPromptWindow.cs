using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using SmartWater.Engine;

namespace SmartWater.App;

/// <summary>نافذة السؤال «أي وايت هذا؟»: صورة اللقطة + الوايتات الأشبه أولًا. الجواب الأول هو المعتمد.</summary>
public sealed class TruckPromptWindow : Window
{
    public long EpisodeId { get; }

    public TruckPromptWindow(TruckPrompt p, TruckCoordinator flow)
    {
        EpisodeId = p.EpisodeId;
        Title = "أي وايت هذا؟";
        FlowDirection = FlowDirection.RightToLeft;
        Width = 560; Height = 660; Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new DockPanel { Margin = 12 };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var choose = new Button { Content = "اختيار هذا الوايت", Padding = new Thickness(16, 6), Margin = new Thickness(0, 0, 8, 0), IsEnabled = false };
        var later = new Button { Content = "لاحقًا (أربطه من السجل)", Padding = new Thickness(16, 6) };
        buttons.Children.Add(choose); buttons.Children.Add(later);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        if (p.PhotoPath != null && File.Exists(p.PhotoPath))
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.UriSource = new Uri(p.PhotoPath); bi.EndInit(); bi.Freeze();
                var img = new Image { Source = bi, MaxHeight = 300, Stretch = System.Windows.Media.Stretch.Uniform, Margin = new Thickness(0, 0, 0, 8) };
                DockPanel.SetDock(img, Dock.Top); root.Children.Add(img);
            }
            catch { /* نكمل بلا صورة */ }
        }
        if (p.Note.Length > 0)
        {
            var note = new TextBlock { Text = p.Note, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8), Foreground = System.Windows.Media.Brushes.DarkRed };
            DockPanel.SetDock(note, Dock.Top); root.Children.Add(note);
        }
        var list = new ListBox { DisplayMemberPath = "Label" };
        foreach (var t in p.Trucks) list.Items.Add(new TruckChoice(t.Id, $"{t.Name} — {Ui.N(t.Capacity)} برميل"));
        list.SelectionChanged += (_, _) => choose.IsEnabled = list.SelectedItem != null;
        root.Children.Add(list);

        choose.Click += (_, _) =>
        {
            if (list.SelectedItem is TruckChoice c) { flow.Choose(p.EpisodeId, c.Id); Close(); }
        };
        later.Click += (_, _) => { flow.Dismiss(p.EpisodeId); Close(); };
        Content = root;
    }
}
