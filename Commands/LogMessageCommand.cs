using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UaaSolutionWpf.Commands
{
    /// <summary>
    /// Command to log a message
    /// </summary>
    public class LogMessageCommand : Command<object>
    {
        private readonly string _message;
        private readonly bool _isWarning;

        public LogMessageCommand(
            string message,
            bool isWarning = false,
            ILogger logger = null)
            : base(
                null,
                "LogMessage",
                $"Log message: {message}",
                logger)
        {
            _message = message ?? throw new ArgumentNullException(nameof(message));
            _isWarning = isWarning;
        }

        protected override Task<CommandResult> ExecuteInternalAsync()
        {
            if (_isWarning)
            {
                _logger.Warning(_message);
            }
            else
            {
                _logger.Information(_message);
            }

            return Task.FromResult(CommandResult.Successful());
        }
    }
}
