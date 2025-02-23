using System;
using System.Windows;
using System.Windows.Threading;
using Serilog;
using UaaSolutionWpf.Services;

namespace UaaSolutionWpf
{
    public partial class TimerWindow : Window
    {
        private readonly PreciseTimer _timer;
        private readonly TimeSpan _duration;
        private readonly TaskCompletionSource<bool> _completionSource;
        private readonly ILogger _logger;
        private bool _isClosing = false;  // Add this field at class level
        private bool _isCancelled = false;
        public TimerWindow(TimeSpan duration, ILogger logger)
        {
            InitializeComponent();

            _duration = duration;
            _logger = logger?.ForContext<TimerWindow>() ?? throw new ArgumentNullException(nameof(logger));
            _timer = new PreciseTimer(_logger);
            _completionSource = new TaskCompletionSource<bool>();

            // Set up timer events
            _timer.TimerTick += Timer_Tick;
            _timer.TimerCompleted += Timer_Completed;
            _timer.TimerError += Timer_Error;

            // Set up initial UI state
            TimerProgress.Minimum = 0;
            TimerProgress.Maximum = 100;
            TimerProgress.Value = 100;

            // Start the timer when window is loaded
            Loaded += TimerWindow_Loaded;

            // Handle window closing
            Closing += TimerWindow_Closing;
        }

        private async void TimerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                UpdateTimeDisplay(_duration);
                await _timer.StartAsync(_duration);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error starting timer");
                MessageBox.Show("Error starting timer: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        private void Timer_Tick(object sender, TimeSpan remaining)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                UpdateTimeDisplay(remaining);
                UpdateProgressBar(remaining);
            }));
        }

        private void Timer_Completed(object sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                _completionSource.TrySetResult(true);
                Close();
            }));
        }

        private void Timer_Error(object sender, Exception ex)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                _logger.Error(ex, "Timer error occurred");
                MessageBox.Show("Timer error: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _completionSource.TrySetException(ex);
                Close();
            }));
        }

        private void UpdateTimeDisplay(TimeSpan time)
        {
            string format = time.Hours > 0 ? @"hh\:mm\:ss" : @"mm\:ss";
            TimeDisplay.Text = time.ToString(format);
        }

        private void UpdateProgressBar(TimeSpan remaining)
        {
            double progressPercent = (remaining.TotalMilliseconds / _duration.TotalMilliseconds) * 100;
            TimerProgress.Value = progressPercent;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Are you sure you want to cancel the timer?",
                              "Confirm Cancel",
                              MessageBoxButton.YesNo,
                              MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                HandleCancellation();
            }
        }

        private void TimerWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_isCancelled)
            {
                HandleCancellation();
            }
        }

        private void HandleCancellation()
        {
            if (_isCancelled) return;
            _isCancelled = true;

            _timer.Stop();
            _timer.Dispose();

            // Update UI to show cancelled state
            TimeDisplay.Text = "Cancelled";
            TimerProgress.Value = 0;

            // Set the task as canceled
            _completionSource.TrySetCanceled();

            // Change Cancel button to Close
            CancelButton.Content = "Close";
            CancelButton.Click -= CancelButton_Click;
            CancelButton.Click += (s, e) => Close();
        }

        public Task WaitForCompletion()
        {
            return _completionSource.Task;
        }
    }
}