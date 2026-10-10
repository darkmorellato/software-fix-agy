using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace SoftwareFix.UI.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        try
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string iconPath = Path.Combine(home, ".local", "share", "icons", "software-fix.png");
            if (File.Exists(iconPath))
            {
                Icon = new WindowIcon(iconPath);
            }
        }
        catch { }
    }
}