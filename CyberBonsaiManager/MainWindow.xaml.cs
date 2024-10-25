using System.Windows;
using Serilog;

namespace CyberBonsaiManager;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        WindowState = WindowState.Minimized;
    }

    public void ScrollViewer1_ScrollToBottom()
    {
        if (DataContext is MainWindowViewModel { AutoScrollToBottom: true })
            Dispatcher.Invoke(ScrollViewer1.ScrollToBottom);
    }
}