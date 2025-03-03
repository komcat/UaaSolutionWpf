using Serilog;
using System.Threading;
using UaaSolutionWpf.Commands;

public class DelayCommand : Command<object>
{
    private readonly TimeSpan _duration;

    public DelayCommand(
        TimeSpan duration,
        ILogger logger = null)
        : base(
            new object(), // Pass a dummy context object
            $"Delay-{duration.TotalMilliseconds}ms",
            $"Delay for {duration.TotalMilliseconds} milliseconds",
            logger)
    {
        _duration = duration;
    }

    protected override async Task<CommandResult> ExecuteInternalAsync()
    {
        try
        {
            _logger.Information("Starting delay of {Duration}ms", _duration.TotalMilliseconds);

            // Break up the delay into smaller chunks to allow for cancellation
            var remaining = _duration;
            var chunkSize = TimeSpan.FromMilliseconds(100);

            while (remaining > TimeSpan.Zero)
            {
                await CheckPausedAsync();
                _cancellationToken.ThrowIfCancellationRequested();

                var waitTime = remaining < chunkSize ? remaining : chunkSize;
                await Task.Delay(waitTime, _cancellationToken);
                remaining -= waitTime;
            }

            _logger.Information("Delay of {Duration}ms completed", _duration.TotalMilliseconds);
            return CommandResult.Successful($"Delay of {_duration.TotalMilliseconds}ms completed");
        }
        catch (OperationCanceledException)
        {
            _logger.Information("Delay of {Duration}ms was canceled", _duration.TotalMilliseconds);
            return CommandResult.Failed($"Delay of {_duration.TotalMilliseconds}ms was canceled");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during delay operation");
            return CommandResult.Failed($"Error during delay operation: {ex.Message}", ex);
        }
    }
}