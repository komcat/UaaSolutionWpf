using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace UaaSolutionWpf.Services
{
    public class PreciseTimer : IDisposable
    {
        private readonly ILogger _logger;
        private readonly object _lockObject = new object();
        private readonly Stopwatch _stopwatch;
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isRunning;
        private bool _disposed;

        public event EventHandler<TimeSpan> TimerTick;
        public event EventHandler TimerCompleted;
        public event EventHandler<Exception> TimerError;

        public TimeSpan Interval { get; private set; }
        public TimeSpan RemainingTime { get; private set; }
        public bool IsRunning => _isRunning;

        public PreciseTimer(ILogger logger)
        {
            _logger = logger?.ForContext<PreciseTimer>() ?? throw new ArgumentNullException(nameof(logger));
            _stopwatch = new Stopwatch();
        }

        public async Task StartAsync(TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero)
                throw new ArgumentException("Duration must be positive", nameof(duration));

            lock (_lockObject)
            {
                if (_isRunning)
                    throw new InvalidOperationException("Timer is already running");

                _isRunning = true;
                RemainingTime = duration;
                _cancellationTokenSource = new CancellationTokenSource();
            }

            try
            {
                await RunTimerAsync(duration);
            }
            catch (OperationCanceledException)
            {
                _logger.Information("Timer was cancelled");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in timer execution");
                TimerError?.Invoke(this, ex);
            }
            finally
            {
                lock (_lockObject)
                {
                    _isRunning = false;
                }
            }
        }

        private async Task RunTimerAsync(TimeSpan duration)
        {
            const int UPDATE_INTERVAL_MS = 100; // Update UI every 100ms
            const int SPIN_THRESHOLD_MS = 5; // Switch to spinning for last 5ms

            TimeSpan totalElapsed = TimeSpan.Zero;
            _stopwatch.Restart();

            try
            {
                while (totalElapsed < duration)
                {
                    if (_cancellationTokenSource.Token.IsCancellationRequested)
                        break;

                    TimeSpan remaining = duration - totalElapsed;
                    TimeSpan nextInterval = TimeSpan.FromMilliseconds(
                        Math.Min(UPDATE_INTERVAL_MS, remaining.TotalMilliseconds)
                    );

                    if (remaining.TotalMilliseconds > SPIN_THRESHOLD_MS)
                    {
                        // Use Task.Delay for longer intervals
                        await Task.Delay(nextInterval, _cancellationTokenSource.Token);
                    }
                    else
                    {
                        // Use spinning only for final precision
                        long targetTicks = _stopwatch.ElapsedTicks +
                            (long)(remaining.TotalSeconds * Stopwatch.Frequency);

                        while (_stopwatch.ElapsedTicks < targetTicks)
                        {
                            if (_cancellationTokenSource.Token.IsCancellationRequested)
                                break;

                            if (targetTicks - _stopwatch.ElapsedTicks > Stopwatch.Frequency / 1000)
                                await Task.Yield(); // Yield to other threads periodically
                        }
                        break; // Exit after final precision timing
                    }

                    totalElapsed = TimeSpan.FromSeconds(_stopwatch.ElapsedTicks / (double)Stopwatch.Frequency);
                    RemainingTime = duration - totalElapsed;

                    // Raise tick event on UI thread
                    await Task.Run(() => TimerTick?.Invoke(this, RemainingTime));

                    // Log timing accuracy periodically
                    if (totalElapsed.TotalSeconds % 1 < 0.1) // Log every second
                    {
                        _logger.Debug("Timer progress: {Elapsed}/{Duration} seconds",
                            totalElapsed.TotalSeconds.ToString("F1"),
                            duration.TotalSeconds.ToString("F1"));
                    }
                }

                _stopwatch.Stop();
                await Task.Run(() => TimerCompleted?.Invoke(this, EventArgs.Empty));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.Error(ex, "Error during timer execution");
                TimerError?.Invoke(this, ex);
                throw;
            }
        }

        public void Stop()
        {
            lock (_lockObject)
            {
                if (!_isRunning)
                    return;

                _cancellationTokenSource?.Cancel();
                _isRunning = false;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                Stop();
                _cancellationTokenSource?.Dispose();
                _stopwatch.Stop();
            }

            _disposed = true;
        }
    }
}