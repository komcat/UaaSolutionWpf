using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace UaaSolutionWpf.Commands
{
    /// <summary>
    /// Generic base class for creating reusable commands with strong typing
    /// </summary>
    /// <typeparam name="TContext">The context or service used to execute the command</typeparam>
    public abstract class Command<TContext> : ICommand
    {
        protected readonly TContext _context;
        protected readonly ILogger _logger;

        public string Name { get; }
        public string Description { get; }
        public CommandStatus Status { get; protected set; } = CommandStatus.NotStarted;

        protected CancellationToken _cancellationToken;
        protected CancellationTokenSource _pauseTokenSource = new CancellationTokenSource();
        protected bool _isPaused = false;

        public Command(TContext context, string name, string description, ILogger logger = null)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Description = description ?? string.Empty;
            _logger = logger ?? Log.ForContext<Command<TContext>>();
        }

        public async Task<CommandResult> ExecuteAsync(CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
            Status = CommandStatus.Running;

            // Use stopwatch to track execution time
            var stopwatch = Stopwatch.StartNew();

            try
            {
                _logger.Information("Executing command: {CommandName}", Name);
                var result = await ExecuteInternalAsync();

                // Stop the stopwatch and set execution time
                stopwatch.Stop();
                result.ExecutionTime = stopwatch.Elapsed;

                Status = result.Success ? CommandStatus.Completed : CommandStatus.Failed;
                return result;
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();
                Status = CommandStatus.Aborted;
                _logger.Information("Command {CommandName} was canceled", Name);
                return new CommandResult
                {
                    Success = false,
                    Message = "Command was canceled",
                    ExecutionTime = stopwatch.Elapsed
                };
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                Status = CommandStatus.Failed;
                _logger.Error(ex, "Error executing command {CommandName}", Name);
                return new CommandResult
                {
                    Success = false,
                    Message = $"Command execution failed: {ex.Message}",
                    Error = ex,
                    ExecutionTime = stopwatch.Elapsed
                };
            }
        }

        protected abstract Task<CommandResult> ExecuteInternalAsync();

        public virtual async Task<CommandResult> AbortAsync()
        {
            if (Status != CommandStatus.Running && Status != CommandStatus.Paused)
            {
                return CommandResult.Failed("Cannot abort command that is not running or paused");
            }

            Status = CommandStatus.Aborted;
            _logger.Information("Command {CommandName} aborted", Name);
            return CommandResult.Successful("Command aborted");
        }

        public virtual async Task<CommandResult> PauseAsync()
        {
            if (Status != CommandStatus.Running)
            {
                return CommandResult.Failed("Cannot pause command that is not running");
            }

            _isPaused = true;
            Status = CommandStatus.Paused;
            _pauseTokenSource.Cancel();
            _logger.Information("Command {CommandName} paused", Name);
            return CommandResult.Successful("Command paused");
        }

        public virtual async Task<CommandResult> ResumeAsync()
        {
            if (Status != CommandStatus.Paused)
            {
                return CommandResult.Failed("Cannot resume command that is not paused");
            }

            _isPaused = false;
            _pauseTokenSource = new CancellationTokenSource();
            Status = CommandStatus.Running;
            _logger.Information("Command {CommandName} resumed", Name);
            return CommandResult.Successful("Command resumed");
        }

        protected async Task CheckPausedAsync()
        {
            if (_isPaused)
            {
                _logger.Debug("Command {CommandName} waiting for resume", Name);
                try
                {
                    while (_isPaused && !_cancellationToken.IsCancellationRequested)
                    {
                        await Task.Delay(100, _cancellationToken);
                    }

                    _cancellationToken.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException)
                {
                    if (_cancellationToken.IsCancellationRequested)
                    {
                        throw; // Propagate cancellation
                    }
                }
            }
        }
    }
}