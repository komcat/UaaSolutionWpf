using System;
using System.Threading;
using System.Threading.Tasks;
using NationalInstruments.Visa;
using Serilog;

namespace UaaSolutionWpf.Services
{
    public class CLD101xGpibService : IDisposable
    {
        private MessageBasedSession _session;
        private readonly string _resourceName;
        private bool _isConnected;
        private readonly ILogger _logger;
        private bool _disposed;
        private CancellationTokenSource _monitoringCts;
        private Task _monitoringTask;
        private readonly SemaphoreSlim _gpibLock = new SemaphoreSlim(1, 1);

        public bool IsConnected => _isConnected;

        public event EventHandler<double> CurrentMeasurementReceived;
        public event EventHandler<double> TemperatureMeasurementReceived;
        public event EventHandler<Exception> ErrorOccurred;

        public CLD101xGpibService(ILogger logger, string resourceName = "USB0::0x1313::0x804F::M00930341::INSTR")
        {
            _logger = logger.ForContext<CLD101xGpibService>();
            _resourceName = resourceName;
        }

        public async Task ConnectAsync()
        {
            try
            {
                await Task.Run(() =>
                {
                    var rmSession = new ResourceManager();
                    _session = (MessageBasedSession)rmSession.Open(_resourceName);

                });
                await Task.Delay(2000);
                _isConnected = true;

                // Query device ID to verify connection
                string idResponse = await QueryAsync("*IDN?");
                _logger.Information("Connected to device: {DeviceId}", idResponse);


                //Log.Information("Clear any pending errors in the queue");
                //// Clear any pending errors in the queue
                //await Task.Run(() => {
                //    _session.RawIO.Write("*CLS\n");  // Clear status command
                //    _session.RawIO.Write("errorqueue.clear()\n"); // Clear error queue
                //});
                //Log.Information("Clear any pending errors in the queue, completed");
                
                // Initialize with safe settings
                await WriteAsync("output1:state off"); // Laser off
                await WriteAsync("output2:state off"); // TEC off

                
                // Start monitoring if not already running
                StartMonitoring();
            }
            catch (Exception ex)
            {
                _isConnected = false;
                _logger.Error(ex, "Failed to connect to CLD101x");
                throw new Exception($"Failed to connect to CLD101x: {ex.Message}");
            }
        }

        private void StartMonitoring()
        {
            _monitoringCts?.Cancel();
            _monitoringCts = new CancellationTokenSource();

            _monitoringTask = Task.Run(async () =>
            {
                while (!_monitoringCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        if (_isConnected)
                        {
                            await ReadLaserCurrentAsync();
                            await Task.Delay(500, _monitoringCts.Token);
                            await ReadTecTemperatureAsync();
                            await Task.Delay(500, _monitoringCts.Token);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error in monitoring loop");
                        ErrorOccurred?.Invoke(this, ex);
                        await Task.Delay(1000, _monitoringCts.Token); // Wait before retrying
                    }
                }
            }, _monitoringCts.Token);
        }

        public async Task SetLaserCurrent(double current)
        {
            await WriteAsync($"source1:current:level:amplitude {current:F3}");
        }

        public async Task SetTecTemperature(double temperature)
        {
            await WriteAsync($"source2:temperature:spoint {temperature:F2}");
        }

        public async Task LaserOn()
        {
            await WriteAsync("output1:state on");
        }

        public async Task LaserOff()
        {
            await WriteAsync("output1:state off");
        }

        public async Task TecOn()
        {
            await WriteAsync("output2:state on");
        }

        public async Task TecOff()
        {
            await WriteAsync("output2:state off");
        }

        public async Task<double> ReadLaserCurrentAsync()
        {
            try
            {
                string response = await QueryAsync("sense3:current:dc:data?");
                if (double.TryParse(response, out double current))
                {
                    CurrentMeasurementReceived?.Invoke(this, current);
                    return current;
                }
                throw new FormatException($"Invalid current reading format: {response}");
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex);
                throw;
            }
        }

        public async Task<double> ReadTecTemperatureAsync()
        {
            try
            {
                await using var timeoutHandler = new TemporaryTimeoutHandler(_session, 2000);
                string response = await QueryAsync("sense2:temperature:data?");
                if (double.TryParse(response, out double temperature))
                {
                    TemperatureMeasurementReceived?.Invoke(this, temperature);
                    return temperature;
                }
                throw new FormatException($"Invalid temperature reading format: {response}");
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ex);
                throw;
            }
        }

        private async Task WriteAsync(string command)
        {
            if (!_isConnected || _session == null)
                throw new InvalidOperationException("Not connected to device");

            await _gpibLock.WaitAsync();
            try
            {
                await Task.Run(() => _session.RawIO.Write(command));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error writing command: {Command}", command);
                ErrorOccurred?.Invoke(this, ex);
                throw;
            }
            finally
            {
                _gpibLock.Release();
            }
        }

        private async Task<string> QueryAsync(string query, int maxRetries = 3)
        {
            if (!_isConnected || _session == null)
                throw new InvalidOperationException("Not connected to device");

            int retryCount = 0;
            Exception lastException = null;

            while (retryCount <= maxRetries)
            {
                await _gpibLock.WaitAsync();
                try
                {
                    // Calculate exponential backoff delay
                    int delayMs = retryCount == 0 ? 0 : (int)Math.Pow(2, retryCount - 1) * 1000;
                    if (retryCount > 0)
                    {
                        _logger.Information($"Retry attempt {retryCount} after {delayMs}ms delay for query: {query}");
                        await Task.Delay(delayMs);
                    }

                    return await Task.Run(() =>
                    {
                        // Set a longer timeout for subsequent retries
                        int timeoutMs = retryCount == 0 ? _session.TimeoutMilliseconds : 5000;
                        using (new TemporaryTimeout(_session, timeoutMs))
                        {
                            try
                            {
                                _session.RawIO.Write(query);
                                Task.Delay(20);
                                return _session.RawIO.ReadString().Trim();
                            }
                            catch (Exception ex)
                            {
                                // Clear any pending data in the buffer
                                try { _session.RawIO.ReadString(); } catch { }
                                throw;
                            }
                        }
                    });
                }
                catch (Ivi.Visa.IOTimeoutException ex)
                {
                    lastException = ex;
                    retryCount++;
                    _logger.Warning($"Timeout occurred during query (attempt {retryCount}/{maxRetries}): {ex.Message}");

                    if (retryCount > maxRetries)
                    {
                        _logger.Error(ex, "Max retries reached for query operation");
                        ErrorOccurred?.Invoke(this, ex);
                        throw new TimeoutException($"Query operation failed after {maxRetries} retries", ex);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error querying: {Query}", query);
                    ErrorOccurred?.Invoke(this, ex);
                    throw;
                }
                finally
                {
                    _gpibLock.Release();
                }
            }

            // This should never be reached due to the throw in the retry loop
            throw new TimeoutException($"Query operation failed after {maxRetries} retries", lastException);
        }

        // Helper class to temporarily modify timeout
        private class TemporaryTimeout : IDisposable
        {
            private readonly MessageBasedSession _session;
            private readonly int _originalTimeout;

            public TemporaryTimeout(MessageBasedSession session, int temporaryTimeoutMs)
            {
                _session = session;
                _originalTimeout = session.TimeoutMilliseconds;
                session.TimeoutMilliseconds = temporaryTimeoutMs;
            }

            public void Dispose()
            {
                _session.TimeoutMilliseconds = _originalTimeout;
            }
        }

        // Add this method to reset the connection if needed
        private async Task ResetConnectionAsync()
        {
            _logger.Information("Attempting to reset connection...");
            try
            {
                // Disconnect and wait briefly
                Disconnect();
                await Task.Delay(1000);

                // Reconnect
                var rmSession = new ResourceManager();
                _session = (MessageBasedSession)rmSession.Open(_resourceName);
                _isConnected = true;

                // Query device ID to verify connection
                string idResponse = await QueryAsync("*IDN?");
                _logger.Information("Connection reset successful. Device: {DeviceId}", idResponse);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to reset connection");
                _isConnected = false;
                throw;
            }
        }

        private class TemporaryTimeoutHandler : IAsyncDisposable
        {
            private readonly MessageBasedSession _session;
            private readonly int _originalTimeout;

            public TemporaryTimeoutHandler(MessageBasedSession session, int temporaryTimeoutMs)
            {
                _session = session;
                _originalTimeout = session.TimeoutMilliseconds;
                session.TimeoutMilliseconds = temporaryTimeoutMs;
            }

            public ValueTask DisposeAsync()
            {
                _session.TimeoutMilliseconds = _originalTimeout;
                return ValueTask.CompletedTask;
            }
        }

        public void Disconnect()
        {
            if (_isConnected)
            {
                try
                {
                    _monitoringCts?.Cancel();
                    if (_monitoringTask != null)
                    {
                        Task.WaitAll(new[] { _monitoringTask }, 1000);
                    }

                    // Try to safely shutdown the device
                    Task.Run(async () =>
                    {
                        try
                        {
                            await LaserOff();
                            await TecOff();
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "Error during disconnect sequence");
                        }
                    }).Wait(1000); // Wait max 1 second for shutdown

                    _session?.Dispose();
                    _session = null;
                    _isConnected = false;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error during disconnect");
                }
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _monitoringCts?.Cancel();
                Disconnect();
                _gpibLock.Dispose();
                _monitoringCts?.Dispose();
                _disposed = true;
            }
        }
    }
}