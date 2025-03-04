using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Serilog;
using UaaSolutionWpf.Data;

namespace UaaSolutionWpf.Commands
{
    /// <summary>
    /// Extension methods for DUT commands to simplify their usage
    /// </summary>
    public static class DUTCommandExtensions
    {
        /// <summary>
        /// Creates a DUTAddValueCommand with a file path provider function
        /// </summary>
        public static DUTAddValueCommand CreateDUTAddValueCommand(
            this RealTimeDataManager dataManager,
            Func<string> filePathProvider,
            string state,
            string channelName,
            ILogger logger = null)
        {
            return new DUTAddValueCommand(dataManager, filePathProvider, state, channelName, logger);
        }

        /// <summary>
        /// Creates a DUTAddValueCommand with a manual value and a file path provider function
        /// </summary>
        public static DUTAddValueCommand CreateDUTAddValueCommand(
            this RealTimeDataManager dataManager,
            Func<string> filePathProvider,
            string state,
            double value,
            string unit,
            ILogger logger = null)
        {
            return new DUTAddValueCommand(dataManager, filePathProvider, state, value, unit, logger);
        }
    }

        
}