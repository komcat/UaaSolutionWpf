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
            const int TargetAccuracyMs = 1; // Target accuracy in milliseconds
            TimeSpan totalElapsed = TimeSpan.Zero;
            _stopwatch.Restart();

            while (totalElapsed < duration)
            {
                if (_cancellationTokenSource.Token.IsCancellationRequested)
                    break;

                // Calculate next tick interval
                TimeSpan nextTick = TimeSpan.FromMilliseconds(TargetAccuracyMs);
                TimeSpan remaining = duration - totalElapsed;

                if (remaining < nextTick)
                    nextTick = remaining;

                // High-precision wait
                long targetTicks = _stopwatch.ElapsedTicks + (long)(nextTick.TotalSeconds * Stopwatch.Frequency);
                while (_stopwatch.ElapsedTicks < targetTicks)
                {
                    if (_cancellationTokenSource.Token.IsCancellationRequested)
                        break;

                    // Efficient spinning for high precision
                    if (targetTicks - _stopwatch.ElapsedTicks > Stopwatch.Frequency / 1000)
                        await Task.Delay(0); // Yield to other threads if we're far from target
                }

                totalElapsed = TimeSpan.FromSeconds(_stopwatch.ElapsedTicks / (double)Stopwatch.Frequency);
                RemainingTime = duration - totalElapsed;

                // Raise tick event
                TimerTick?.Invoke(this, RemainingTime);

                // Log timing accuracy periodically
                if (totalElapsed.TotalSeconds % 1 < TargetAccuracyMs / 1000.0)
                {
                    double accuracyPercent = Math.Abs(1 - (totalElapsed.TotalMilliseconds % 1000) / 1000) * 100;
                    _logger.Debug("Timer accuracy at {Elapsed}: {Accuracy:F2}% deviation",
                        totalElapsed, accuracyPercent);
                }
            }

            _stopwatch.Stop();
            TimerCompleted?.Invoke(this, EventArgs.Empty);
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