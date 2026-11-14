using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace iLand;

public partial class MainWindow : Window
{
    private const double SmallW = 190;
    private const double SmallH = 48;
    private const double LargeW = 410;
    private const double LargeH = 290;

    private readonly DispatcherTimer clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer hardwareTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer demoToastTimer = new() { Interval = TimeSpan.FromSeconds(6) };
    private readonly DispatcherTimer collapseTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };

    private readonly List<(string App, string Title, string Body, Brush Color, string Icon)> demoMessages = new()
    {
        ("Discord", "New message", "You have a new DM from @snoopy", Brushes.SlateBlue, "D"),
        ("Spotify", "Now playing", "Midnight City · M83", new SolidColorBrush(Color.FromRgb(29, 185, 84)), "♫"),
        ("Teams", "Meeting reminder", "Design review starts in 15 minutes", new SolidColorBrush(Color.FromRgb(86,130,255)), "T"),
        ("Telegram", "New channel post", "Your group has a new update", new SolidColorBrush(Color.FromRgb(94,167,255)), "TG"),
        ("WhatsApp", "Incoming message", "Alex: Can you review the mockup?", new SolidColorBrush(Color.FromRgb(71,201,107)), "W"),
        ("Outlook", "Email received", "New message from design@company", new SolidColorBrush(Color.FromRgb(92,134,255)), "O")
    };

    private PerformanceCounter? cpuCounter;
    private bool expanded;
    private bool mouseDown;
    private double swipeStart;
    private int currentPage;
    private int demoIndex;

    public MainWindow()
    {
        InitializeComponent();

        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - SmallW) / 2;
        Top = area.Top + 12;

        clockTimer.Tick += (_, _) => UpdateClock();
        hardwareTimer.Tick += (_, _) => UpdateHardware();
        demoToastTimer.Tick += (_, _) => ShowDemoNotification();
        collapseTimer.Tick += (_, _) =>
        {
            collapseTimer.Stop();
            if (!Island.IsMouseOver && !ToastCard.IsVisible)
                Collapse();
        };

        Loaded += (_, _) =>
        {
            clockTimer.Start();

            try
            {
                cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _ = cpuCounter.NextValue();
                hardwareTimer.Start();
                UpdateHardware();
            }
            catch (Exception ex)
            {
                CpuText.Text = "CPU  unavailable";
                Debug.WriteLine($"PerformanceCounter failed: {ex.Message}");
            }

            demoToastTimer.Start();
            NotificationStatusText.Text = "Demo notifications are active";
        };

        UpdateClock();
    }

    private void UpdateClock()
    {
        ClockText.Text = DateTime.Now.ToString("HH:mm");
        DateText.Text = DateTime.Now.ToString("dddd, dd MMMM");
    }

    private void UpdateHardware()
    {
        try
        {
            if (cpuCounter is not null)
            {
                double cpu = Math.Clamp(cpuCounter.NextValue(), 0, 100);
                CpuText.Text = $"CPU  {cpu:F0}%";
                CpuBar.Value = cpu;
            }

            var memory = new MemoryStatusEx
            {
                Length = (uint)Marshal.SizeOf<MemoryStatusEx>()
            };

            if (GlobalMemoryStatusEx(ref memory))
            {
                RamText.Text = $"RAM  {memory.MemoryLoad}%";
                RamBar.Value = memory.MemoryLoad;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"System monitor update failed: {ex.Message}");
        }
    }

    private void AnimateIsland(double width, double height)
    {
        double center = Left + ActualWidth / 2;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(420);

        BeginAnimation(WidthProperty, new DoubleAnimation(width, duration)
        {
            EasingFunction = ease
        });

        BeginAnimation(HeightProperty, new DoubleAnimation(height, duration)
        {
            EasingFunction = ease
        });

        BeginAnimation(LeftProperty, new DoubleAnimation(center - width / 2, duration)
        {
            EasingFunction = ease
        });
    }

    private void Expand()
    {
        collapseTimer.Stop();

        if (expanded)
            return;

        expanded = true;
        ExpandedContent.Visibility = Visibility.Visible;
        AnimateIsland(LargeW, LargeH);
    }

    private void Collapse()
    {
        if (!expanded)
            return;

        expanded = false;
        AnimateIsland(SmallW, SmallH);

        var finish = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(440)
        };

        finish.Tick += (_, _) =>
        {
            finish.Stop();
            if (!expanded)
                ExpandedContent.Visibility = Visibility.Collapsed;
        };

        finish.Start();
    }

    private void Island_MouseEnter(object sender, MouseEventArgs e)
    {
        collapseTimer.Stop();
        Expand();
    }

    private void Island_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!ToastCard.IsVisible)
            collapseTimer.Start();
    }

    private void ChangePage(int direction)
    {
        currentPage = (currentPage + direction + 3) % 3;

        HomePage.Visibility = currentPage == 0 ? Visibility.Visible : Visibility.Collapsed;
        SystemPage.Visibility = currentPage == 1 ? Visibility.Visible : Visibility.Collapsed;
        NotificationsPage.Visibility = currentPage == 2 ? Visibility.Visible : Visibility.Collapsed;

        IslandTitle.Text = currentPage switch
        {
            0 => "iLand",
            1 => "Система",
            _ => "Уведомления"
        };

        PageIndicator.Text = $"{currentPage + 1} / 3";
    }

    private void PreviousPage(object sender, RoutedEventArgs e) => ChangePage(-1);
    private void NextPage(object sender, RoutedEventArgs e) => ChangePage(1);

    private void Island_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        ChangePage(e.Delta > 0 ? -1 : 1);
        e.Handled = true;
    }

    private void Island_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        swipeStart = e.GetPosition(this).X;
        mouseDown = true;
        CaptureMouse();
    }

    private void Island_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!mouseDown)
            return;

        double delta = e.GetPosition(this).X - swipeStart;

        if (Math.Abs(delta) > 65 && expanded)
            ChangePage(delta > 0 ? -1 : 1);

        mouseDown = false;
        ReleaseMouseCapture();
    }

    private void ShowDemoNotification()
    {
        var item = demoMessages[demoIndex % demoMessages.Count];
        demoIndex++;

        ToastApp.Text = item.App;
        ToastTitle.Text = item.Title;
        ToastBody.Text = item.Body;
        ToastIcon.Text = item.Icon;
        ToastIconBackground.Background = item.Color;

        Expand();
        ToastCard.Visibility = Visibility.Visible;

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220));
        var moveIn = new DoubleAnimation(-18, 0, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        ToastCard.BeginAnimation(OpacityProperty, fadeIn);
        ToastShift.BeginAnimation(TranslateTransform.YProperty, moveIn);

        demoToastTimer.Stop();
        demoToastTimer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        clockTimer.Stop();
        hardwareTimer.Stop();
        demoToastTimer.Stop();
        collapseTimer.Stop();
        cpuCounter?.Dispose();
        base.OnClosed(e);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
