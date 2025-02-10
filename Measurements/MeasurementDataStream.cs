using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace UaaSolutionWpf.Measurements
{
    public class MeasurementPoint
    {
        public DateTime Timestamp { get; set; }
        public double Value { get; set; }
        public int ChannelNumber { get; set; }
        public string Unit { get; set; }
        public string ChannelName { get; set; }
    }

    public class DataStreamConfig
    {
        public int MaxBufferSize { get; set; } = 10000;  // Default buffer size
        public int BatchSize { get; set; } = 100;        // Number of points to trigger batch processing
        public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(1);
        public bool EnableDataLogging { get; set; } = true;
    }

    public class MeasurementDataStream : IDisposable
    {
        private readonly ConcurrentQueue<MeasurementPoint> _dataBuffer;
        private readonly DataStreamConfig _config;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly object _lockObject = new object();
        private bool _disposed;

        // Events for data handling
        public event EventHandler<List<MeasurementPoint>> BatchProcessed;
        public event EventHandler<MeasurementPoint> DataPointAdded;
        public event EventHandler<Exception> ErrorOccurred;
        public event EventHandler BufferOverflow;

        private Task _processingTask;
        public int BufferSize
        {
            get { return _dataBuffer.Count; }
        }
        public MeasurementDataStream(DataStreamConfig config = null, ILogger logger = null)
        {
            _config = config ?? new DataStreamConfig();
            _logger = logger?.ForContext<MeasurementDataStream>() ?? Log.Logger;
            _dataBuffer = new ConcurrentQueue<MeasurementPoint>();
            _cancellationTokenSource = new CancellationTokenSource();
            _config.EnableDataLogging = false;
            StartProcessing();
        }

        private void StartProcessing()
        {
            _processingTask = Task.Run(async () =>
            {
                try
                {
                    while (!_cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        await ProcessDataBatchAsync();
                        await Task.Delay(_config.FlushInterval, _cancellationTokenSource.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Normal cancellation, log at debug level
                    _logger.Debug("Data processing task cancelled");
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error in data processing task");
                    ErrorOccurred?.Invoke(this, ex);
                }
            }, _cancellationTokenSource.Token);
        }

        public void AddDataPoint(double value, int channelNumber, string unit = "", string channelName = "")
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MeasurementDataStream));
            }

            try
            {
                var point = new MeasurementPoint
                {
                    Timestamp = DateTime.UtcNow,
                    Value = value,
                    ChannelNumber = channelNumber,
                    Unit = unit,
                    ChannelName = channelName
                };

                if (_dataBuffer.Count >= _config.MaxBufferSize)
                {
                    //_logger.Warning("Buffer overflow detected. Buffer size: {Count}", _dataBuffer.Count);
                    //BufferOverflow?.Invoke(this, EventArgs.Empty);

                    // Remove oldest items if buffer is full
                    while (_dataBuffer.Count >= _config.MaxBufferSize && _dataBuffer.TryDequeue(out _)) { }
                }

                _dataBuffer.Enqueue(point);
                DataPointAdded?.Invoke(this, point);

                if (_config.EnableDataLogging)
                {
                    _logger.Debug("Added data point: Channel {Channel}, Value {Value} {Unit}, Time {Timestamp}",
                        channelNumber, value, unit, point.Timestamp);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error adding data point");
                ErrorOccurred?.Invoke(this, ex);
            }
        }

        private async Task ProcessDataBatchAsync()
        {
            // Do nothing - this allows data to keep accumulating in the buffer
            await Task.CompletedTask;
        }
        private readonly object _snapshotLock = new object();

        public async Task<List<MeasurementPoint>> GetLatestDataAsync(int count)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MeasurementDataStream));
            }

            try
            {
                // Use a lock to ensure a consistent snapshot
                lock (_snapshotLock)
                {
                    // Convert to array using a more controlled method
                    var snapshotArray = _dataBuffer.ToArray();

                    // Calculate the starting index
                    int startIndex = Math.Max(0, snapshotArray.Length - count);

                    // Return the last 'count' elements
                    return snapshotArray
                        .Skip(startIndex)
                        .Take(count)
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting latest data");
                ErrorOccurred?.Invoke(this, ex);
                throw;
            }
        }
        public void ClearBuffer()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MeasurementDataStream));
            }

            while (_dataBuffer.TryDequeue(out _)) { }
            _logger.Information("Data buffer cleared");
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _cancellationTokenSource.Cancel();
                    try
                    {
                        _processingTask?.Wait(TimeSpan.FromSeconds(5));
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error waiting for processing task to complete during disposal");
                    }

                    _cancellationTokenSource.Dispose();
                    ClearBuffer();
                }

                _disposed = true;
            }
        }
    }
}