using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace iLand;

public partial class MainWindow : Window
{
    private const double CompactWidth = 190;
    private const double CompactHeight = 48;
    private const double ExpandedWidth = 410;
    private const double ExpandedHeight = 290;

    private readonly DispatcherTimer clockTimer = new()
    {
        Interval = TimeSpan.FromSeconds(1)
    };

    private readonly DispatcherTimer hardwareTimer = new()
    {
        Interval = TimeSpan.FromSeconds(1)
    };

    private readonly DispatcherTimer notificationTimer = new()
    {
        Interval = TimeSpan.FromSeconds(2)
    };

    private readonly DispatcherTimer toastTimer = new()
    {
        Interval = TimeSpan.FromSeconds(6)
    };

    private readonly DispatcherTimer collapseTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(650)
    };

    private readonly HashSet<uint> seenNotificationIds = new();

    private PerformanceCounter? cpuCounter;
    private bool expanded;
    private bool mouseDown;
    private bool notificationPollRunning;
    private double swipeStart;
    private int currentPage;

    public MainWindow()
    {
        InitializeComponent();

        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + (workArea.Width - CompactWidth) / 2;
        Top = workArea.Top + 12;

        clockTimer.Tick += (_, _) => UpdateClock();
        hardwareTimer.Tick += (_, _) => UpdateHardware();
        notificationTimer.Tick += async (_, _) => await PollNotificationsAsync();
        toastTimer.Tick += (_, _) => HideToast();
        collapseTimer.Tick += (_, _) =>
        {
            collapseTimer.Stop();
            if (!Island.IsMouseOver && !ToastCard.IsVisible)
                Collapse();
        };

        Loaded += MainWindow_Loaded;
        UpdateClock();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
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
            CpuText.Text = "CPU  недоступно";
            Debug.WriteLine($"CPU counter could not start: {ex.Message}");
        }

        await InitializeNotificationsAsync();
        notificationTimer.Start();
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
        AnimateIsland(ExpandedWidth, ExpandedHeight);
    }

    private void Collapse()
    {
        if (!expanded)
            return;

        expanded = false;
        AnimateIsland(CompactWidth, CompactHeight);

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
        collapseTimer.Stop();

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

    private async Task InitializeNotificationsAsync()
    {
        try
        {
            var listener = UserNotificationListener.Current;
            var access = await listener.RequestAccessAsync();

            if (access != UserNotificationListenerAccessStatus.Allowed)
            {
                NotificationStatusText.Text =
                    "Windows не разрешил доступ к уведомлениям. Для чтения уведомлений других приложений может потребоваться MSIX-установка и разрешение Windows.";
                return;
            }

            // Не показываем старые уведомления при первом запуске.
            var existing = await listener.GetNotificationsAsync(NotificationKinds.Toast);
            foreach (var notification in existing)
                seenNotificationIds.Add(notification.Id);

            NotificationStatusText.Text =
                "Доступ разрешён. Уведомления появятся, если Windows передаёт их этому приложению.";
        }
        catch (Exception ex)
        {
            NotificationStatusText.Text =
                "Доступ к уведомлениям недоступен в этой установке. Смотрите примечание в README.";
            Debug.WriteLine($"Notification listener unavailable: {ex}");
        }
    }

    private async Task PollNotificationsAsync()
    {
        if (notificationPollRunning)
            return;

        notificationPollRunning = true;

        try
        {
            var items = await UserNotificationListener.Current
                .GetNotificationsAsync(NotificationKinds.Toast);

            foreach (var item in items.OrderBy(x => x.CreationTime))
            {
                if (!seenNotificationIds.Add(item.Id))
                    continue;

                string appName = item.AppInfo?.DisplayInfo?.DisplayName ?? "Приложение";

                if (!IsSupportedApp(appName))
                    continue;

                var binding = item.Notification?.Visual?
                    .GetBinding(KnownNotificationBindings.ToastGeneric);

                string[] lines = binding?
                    .GetTextElements()
                    .Select(element => element.Text)
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .ToArray() ?? Array.Empty<string>();

                if (lines.Length == 0)
                    continue;

                string title = lines[0];
                string body = string.Join(Environment.NewLine, lines.Skip(1));

                ShowNotification(appName, title, body);
            }

            if (seenNotificationIds.Count > 1000)
                seenNotificationIds.Clear();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Notification polling failed: {ex.Message}");
        }
        finally
        {
            notificationPollRunning = false;
        }
    }

    private static bool IsSupportedApp(string appName)
    {
        string[] names =
        {
            "Discord", "Spotify", "Telegram", "WhatsApp",
            "Teams", "Outlook"
        };

        return names.Any(name =>
            appName.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    private void ShowNotification(string appName, string title, string body)
    {
        ToastApp.Text = appName;
        ToastTitle.Text = title;
        ToastBody.Text = body;

        bool isDiscord = appName.Contains("Discord", StringComparison.OrdinalIgnoreCase);
        bool isSpotify = appName.Contains("Spotify", StringComparison.OrdinalIgnoreCase);

        ToastIcon.Text = isDiscord
            ? "D"
            : isSpotify
                ? "♫"
                : appName.Length > 0
                    ? appName[..1].ToUpperInvariant()
                    : "•";

        ToastIconBackground.Background = isDiscord
            ? new SolidColorBrush(Color.FromRgb(88, 101, 242))
            : isSpotify
                ? new SolidColorBrush(Color.FromRgb(29, 185, 84))
                : new SolidColorBrush(Color.FromRgb(76, 110, 150));

        Expand();
        collapseTimer.Stop();

        ToastCard.Visibility = Visibility.Visible;
        ToastCard.Opacity = 0;
        ToastTransform.Y = -14;

        var duration = TimeSpan.FromMilliseconds(260);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        ToastCard.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration));
        ToastTransform.BeginAnimation(
            System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(-14, 0, duration) { EasingFunction = ease });

        toastTimer.Stop();
        toastTimer.Start();
    }

    private void HideToast()
    {
        toastTimer.Stop();

        var fadeOut = new DoubleAnimation(ToastCard.Opacity, 0, TimeSpan.FromMilliseconds(200));
        fadeOut.Completed += (_, _) =>
        {
            ToastCard.Visibility = Visibility.Collapsed;

            if (!Island.IsMouseOver)
                collapseTimer.Start();
        };

        ToastCard.BeginAnimation(OpacityProperty, fadeOut);
        ToastTransform.BeginAnimation(
            System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(ToastTransform.Y, -12, TimeSpan.FromMilliseconds(200)));
    }

    protected override void OnClosed(EventArgs e)
    {
        clockTimer.Stop();
        hardwareTimer.Stop();
        notificationTimer.Stop();
        toastTimer.Stop();
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
