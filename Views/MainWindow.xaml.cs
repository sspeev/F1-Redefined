using System.Windows;
using System.Windows.Media;
using F1_Redefined.Services;

namespace F1_Redefined;

public partial class MainWindow : Window
{
    private readonly SmartHomeService _smartHome;

    public MainWindow()
    {
        InitializeComponent();
        _smartHome = new SmartHomeService(OnLog, OnFlagChanged);
        _smartHome.Start();
    }

    private void OnLog(string message)
    {
        Dispatcher.Invoke(() =>
        {
            LogBox.AppendText(message + Environment.NewLine);
            LogBox.ScrollToEnd();
        });
    }

    private void OnFlagChanged(string flag)
    {
        Dispatcher.Invoke(() =>
        {
            FlagText.Text = flag;
            FlagIndicator.Background = flag.ToUpperInvariant() switch
            {
                "YELLOW" or "DOUBLE YELLOW" => Brushes.Yellow,
                "RED" => Brushes.Red,
                "GREEN" => Brushes.Green,
                "CLEAR" => Brushes.White,
                "CHEQUERED" => Brushes.White,
                _ => Brushes.Gray
            };
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        _smartHome.Dispose();
        base.OnClosed(e);
    }
}
