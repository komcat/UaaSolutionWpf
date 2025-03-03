using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace UaaSolutionWpf.Commands
{
    /// <summary>
    /// A flexible command sequence that can handle multiple command types
    /// </summary>
    public class CommandSequence : Command<object>
    {
        private readonly List<ICommand> _commands = new List<ICommand>();
        private int _currentCommandIndex = -1;
        private ICommand _currentCommand = null;

        public IReadOnlyList<ICommand> Commands => _commands.AsReadOnly();
        public ICommand CurrentCommand => _currentCommand;
        public int CurrentCommandIndex => _currentCommandIndex;

        public CommandSequence(
        string name = null,
        string description = null,
        ILogger logger = null)
        : base(
            new object(),
            name ?? $"Sequence-{Guid.NewGuid():N}",
            description ?? "Command sequence",
            logger ?? Log.ForContext<CommandSequence>()) // Specify the context explicitly
        {
        }

        /// <summary>
        /// Add a single command to the sequence
        /// </summary>
        public void AddCommand(ICommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            _commands.Add(command);
        }

        /// <summary>
        /// Add multiple commands to the sequence
        /// </summary>
        public void AddCommands(IEnumerable<ICommand> commands)
        {
            if (commands == null) throw new ArgumentNullException(nameof(commands));
            foreach (var command in commands)
            {
                AddCommand(command);
            }
        }

        /// <summary>
        /// Execute the sequence of commands
        /// </summary>
        protected override async Task<CommandResult> ExecuteInternalAsync()
        {
            if (_commands.Count == 0)
            {
                return CommandResult.Successful("Command sequence is empty");
            }

            _logger.Information("Executing command sequence: {SequenceName} with {CommandCount} commands",
                Name, _commands.Count);

            var sequenceResults = new List<CommandResult>();

            for (_currentCommandIndex = 0; _currentCommandIndex < _commands.Count; _currentCommandIndex++)
            {
                await CheckPausedAsync();
                _cancellationToken.ThrowIfCancellationRequested();

                _currentCommand = _commands[_currentCommandIndex];
                _logger.Information("Executing command {CommandIndex}/{CommandCount}: {CommandName}",
                    _currentCommandIndex + 1, _commands.Count, _currentCommand.Name);

                var result = await _currentCommand.ExecuteAsync(_cancellationToken);
                sequenceResults.Add(result);

                if (!result.Success)
                {
                    _logger.Warning("Command {CommandName} failed: {ErrorMessage}",
                        _currentCommand.Name, result.Message);

                    // Return the first failed command's result
                    return CommandResult.Failed(
                        $"Command sequence failed at step {_currentCommandIndex + 1}: {result.Message}",
                        result.Error);
                }
            }

            _currentCommand = null;
            _currentCommandIndex = -1;

            // Create a comprehensive success message
            string successMessage = $"Command sequence completed successfully. " +
                $"Total commands: {sequenceResults.Count}, " +
                $"Total execution time: {TimeSpan.FromMilliseconds(sequenceResults.Sum(r => r.ExecutionTime.TotalMilliseconds)):c}";

            return CommandResult.Successful(successMessage);
        }

        /// <summary>
        /// Abort the current running command in the sequence
        /// </summary>
        public override async Task<CommandResult> AbortAsync()
        {
            var result = await base.AbortAsync();

            if (_currentCommand != null &&
                (_currentCommand.Status == CommandStatus.Running ||
                 _currentCommand.Status == CommandStatus.Paused))
            {
                await _currentCommand.AbortAsync();
            }

            return result;
        }

        /// <summary>
        /// Pause the current running command in the sequence
        /// </summary>
        public override async Task<CommandResult> PauseAsync()
        {
            var result = await base.PauseAsync();

            if (_currentCommand != null && _currentCommand.Status == CommandStatus.Running)
            {
                await _currentCommand.PauseAsync();
            }

            return result;
        }

        /// <summary>
        /// Resume the paused command in the sequence
        /// </summary>
        public override async Task<CommandResult> ResumeAsync()
        {
            var result = await base.ResumeAsync();

            if (_currentCommand != null && _currentCommand.Status == CommandStatus.Paused)
            {
                await _currentCommand.ResumeAsync();
            }

            return result;
        }
    }
}