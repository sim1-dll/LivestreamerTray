using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Timers;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media.Imaging;
using LivestreamerTray.Properties;
using MahApps.Metro.Controls;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using Timer = System.Timers.Timer;

namespace LivestreamerTray
{
    public enum StreamQuality
    {
        Source,
        High,
        Medium,
        Low,
        Audio,
        Best
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : MetroWindow
    {
        private NotifyIcon _notifyIcon;
        private bool _close4Real;
        private static Process _livestreamerProcess;
        public static string LivestreamerExe = "livestreamer.exe";

        public static readonly Timer t = new Timer(1000);

        public MainWindow()
        {
            InitializeComponent();

            _notifyIcon = new NotifyIcon();
            _notifyIcon.Icon = Properties.Resources.Icon;
            _notifyIcon.Visible = true;

            _notifyIcon.ContextMenu = new ContextMenu();
            MenuItem itemExit = new MenuItem("Exit", HandleExitTray);
            itemExit.Text = "Exit";

            Uri iconUri = new Uri("pack://application:,,,/Icon.ico", UriKind.RelativeOrAbsolute);
            this.Icon = BitmapFrame.Create(iconUri);

            SetSelectedQuality(SelectedQuality);

            _notifyIcon.ContextMenu.MenuItems.Add(itemExit);
            _notifyIcon.Click +=
                delegate
                {
                    Show();
                    WindowState = WindowState.Normal;
                };

            t.AutoReset = true;
            t.Elapsed += HandleElapsed;
            MainGrid.DataContext = this;
        }

        private void HandleElapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                lock (launchLock)
                {
                    if (_livestreamerProcess != null &&
                        !_livestreamerProcess.HasExited &&
                        !_livestreamerProcess.StandardOutput.EndOfStream)
                    {
                        string output = _livestreamerProcess.StandardOutput.ReadLine();
                        Dispatcher.Invoke(() =>
                        {
                            OutputTextBlock.Text = OutputTextBlock.Text + output + Environment.NewLine;
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }

        private void HandleExitTray(object sender, EventArgs e)
        {
            _close4Real = true;
            Close();
        }

        private void HandleClosing(object sender, CancelEventArgs e)
        {
            if (true)
            {
                if (!_close4Real)
                    Hide();

                //send to tray
                e.Cancel = !_close4Real;
            }

            if (!e.Cancel)
            {
                Settings.Default.Quality = SelectedQuality;
                Settings.Default.Save();

                DisposeResources();
            }
        }

        private void DisposeResources()
        {
            DisposeLivestreamerProcess();

            _notifyIcon.Dispose();
        }

        private object launchLock = new object();

        internal bool LaunchLivestreamer(string url, string quality)
        {
            bool res = true;

            lock (launchLock)
            {
                try
                {
                    if (_livestreamerProcess != null && !_livestreamerProcess.HasExited)
                        DisposeLivestreamerProcess();
                }
                catch (Exception) //process not started
                {
                    DisposeLivestreamerProcess();
                }

                //stream quality
                quality = string.IsNullOrEmpty(quality)
                    ? DefaultQuality
                    : quality.ToLower();

                //stream path

                string path = Path.Combine(
                    Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location),
                    "livestreamer",
                    LivestreamerExe);

                if (!File.Exists(path))
                {
                    path = LivestreamerExe;
                }


                path = "\"" + path + "\"";
                string args = string.Join(" ", "/c", path, url, quality);

                
                Console.WriteLine("Launching Livestreamer: cmd.exe" + " " + args);

                OutputTextBlock.Text = string.Empty;
                OutputTextBlock.Text = "Launching Livestreamer: cmd.exe" + " " + args;

                ProcessStartInfo startInfo = new ProcessStartInfo("cmd.exe");
                startInfo.Arguments = args;
                startInfo.UseShellExecute = false;
                startInfo.RedirectStandardOutput = true;
                startInfo.CreateNoWindow = true;

                _livestreamerProcess = new Process();
                _livestreamerProcess.OutputDataReceived += HandleOutput;
                _livestreamerProcess.StartInfo = startInfo;
                _livestreamerProcess.EnableRaisingEvents = true;
                _livestreamerProcess.Exited += HandleLivestreamerExited;

                t.Start();

                try
                {
                    _livestreamerProcess.Start();

                    ProcessRunningText.Text = "Livestreamer running";

                    //ProcessRunning = true;
                }
                catch (Exception e)
                {
                    Console.WriteLine("Cannot start Livestreamer:" + e.Message);
                    try
                    {
                        DisposeLivestreamerProcess();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Cannot dispose Livestreamer process:" + ex.Message);
                    }

                    res = false;
                }
            }
            return res;

        }

        private void DisposeLivestreamerProcess()
        {
            lock (launchLock)
            {
                try
                {
                    if (_livestreamerProcess != null)
                    {
                        _livestreamerProcess.Exited -= HandleLivestreamerExited;
                        _livestreamerProcess.OutputDataReceived -= HandleOutput;
                        _livestreamerProcess.Close();
                        _livestreamerProcess = null;

                        Dispatcher.Invoke(() => { ProcessRunningText.Text = "Livestreamer off"; });

                        //not used anymore. Process can be re-started with a new url 
                        //Dispatcher.Invoke(() => { ProcessRunning = false; });
                    }
                }
                finally
                {
                    t.Stop();
                }
            }
        }

        private void HandleLivestreamerExited(object sender, EventArgs e)
        {
            DisposeLivestreamerProcess();
        }

        private void HandleOutput(object sender, DataReceivedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                OutputTextBlock.Text = OutputTextBlock.Text + e.Data;
            });
        }


        public static string DefaultQuality 
        {
            get { return "source"; }
        }

        private void HandleDragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.Link;
        }

        private void HandleDrop(object sender, DragEventArgs e)
        {
            // If the DataObject contains string data, extract it.
            if (e.Data.GetDataPresent(System.Windows.DataFormats.StringFormat))
            {
                string dataString = (string)e.Data.GetData(System.Windows.DataFormats.StringFormat);
                UrlBox.Text = dataString;
                if (Uri.IsWellFormedUriString(dataString, UriKind.Absolute))
                {
                    LaunchLivestreamer(dataString, SelectedQuality.ToString());
                }
            }
        }

        private void HandleClick(object sender, RoutedEventArgs e)
        {
            LaunchLivestreamer(UrlBox.Text, SelectedQuality.ToString());
        }

        public void SetSelectedQuality(StreamQuality quality)
        {
            switch (quality)
            {
                case StreamQuality.Source:
                    SourceQuality = true;
                    break;
                case StreamQuality.High:
                    HighQuality = true;
                    break;
                case StreamQuality.Medium:
                    MediumQuality = true;
                    break;
                case StreamQuality.Low:
                    LowQuality = true;
                    break;
                case StreamQuality.Audio:
                    AudioQuality = true;
                    break;
                case StreamQuality.Best:
                    HighQuality = true;
                    break;
                default:
                    throw new ArgumentOutOfRangeException("quality");
            }
        }

        public static readonly DependencyProperty ProcessRunningProperty = DependencyProperty.Register(
            "ProcessRunning", typeof (bool), typeof (MainWindow), new PropertyMetadata(default(bool)));

        public bool ProcessRunning
        {
            get { return (bool) GetValue(ProcessRunningProperty); }
            set { SetValue(ProcessRunningProperty, value); }
        }

        public static readonly DependencyProperty QualityProperty = DependencyProperty.Register(
            "Quality", typeof (StreamQuality), typeof (MainWindow), new PropertyMetadata(default(StreamQuality)));

        public StreamQuality Quality
        {
            get { return (StreamQuality) GetValue(QualityProperty); }
            set { SetValue(QualityProperty, value); }
        }

        public StreamQuality SelectedQuality
        {
            get
            {
                if (SourceQuality) return StreamQuality.Source;
                else if (HighQuality) return StreamQuality.High;
                else if (MediumQuality) return StreamQuality.Medium;
                else if (LowQuality) return StreamQuality.Low;
                else if (AudioQuality) return StreamQuality.Audio;
                else return StreamQuality.Best;
            }
        }

        public static readonly DependencyProperty SourceQualityProperty = DependencyProperty.Register(
            "SourceQuality", typeof (bool), typeof (MainWindow), new PropertyMetadata(default(bool)));

        public bool SourceQuality
        {
            get { return (bool) GetValue(SourceQualityProperty); }
            set { SetValue(SourceQualityProperty, value); }
        }

        public static readonly DependencyProperty HighQualityProperty = DependencyProperty.Register(
            "HighQuality", typeof (bool), typeof (MainWindow), new PropertyMetadata(default(bool)));

        public bool HighQuality
        {
            get { return (bool) GetValue(HighQualityProperty); }
            set { SetValue(HighQualityProperty, value); }
        }

        public static readonly DependencyProperty MediumQualityProperty = DependencyProperty.Register(
            "MediumQuality", typeof (bool), typeof (MainWindow), new PropertyMetadata(default(bool)));

        public bool MediumQuality
        {
            get { return (bool) GetValue(MediumQualityProperty); }
            set { SetValue(MediumQualityProperty, value); }
        }

        public static readonly DependencyProperty LowQualityProperty = DependencyProperty.Register(
            "LowQuality", typeof(bool), typeof(MainWindow), new PropertyMetadata(default(bool)));

        public bool LowQuality
        {
            get { return (bool)GetValue(LowQualityProperty); }
            set { SetValue(LowQualityProperty, value); }
        }

        public static readonly DependencyProperty AudioQualityProperty = DependencyProperty.Register(
            "AudioQuality", typeof (bool), typeof (MainWindow), new PropertyMetadata(default(bool)));

        public bool AudioQuality
        {
            get { return (bool) GetValue(AudioQualityProperty); }
            set { SetValue(AudioQualityProperty, value); }
        }



    }
}
