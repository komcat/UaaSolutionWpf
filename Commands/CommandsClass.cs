using System;
using System.Threading;
using System.Threading.Tasks;

namespace UaaSolutionWpf.Commands
{
    /// <summary>
    /// Represents the current status of a command
    /// </summary>
    public enum CommandStatus
    {
        NotStarted,
        Running,
        Paused,
        Completed,
        Failed,
        Aborted
    }

    /// <summary>
    /// Represents the result of a command execution
    /// </summary>
    public class CommandResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public Exception Error { get; set; }

        /// <summary>
        /// Total execution time of the command
        /// </summary>
        public TimeSpan ExecutionTime { get; set; }

        public static CommandResult Successful(string message = null) =>
            new CommandResult
            {
                Success = true,
                Message = message ?? "Command completed successfully",
                ExecutionTime = TimeSpan.Zero
            };

        public static CommandResult Failed(string message, Exception error = null) =>
            new CommandResult
            {
                Success = false,
                Message = message,
                Error = error,
                ExecutionTime = TimeSpan.Zero
            };

        public override string ToString()
        {
            return $"Success: {Success}, " +
                   $"Message: {Message}, " +
                   $"Execution Time: {ExecutionTime.TotalMilliseconds:F2}ms";
        }
    }

    /// <summary>
    /// Base interface for all commands
    /// </summary>
    public interface ICommand
    {
        string Name { get; }
        string Description { get; }
        CommandStatus Status { get; }

        Task<CommandResult> ExecuteAsync(CancellationToken cancellationToken);
        Task<CommandResult> AbortAsync();
        Task<CommandResult> PauseAsync();
        Task<CommandResult> ResumeAsync();
    }
}