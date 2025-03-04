using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Serilog;
using UaaSolutionWpf.Commands;
using UaaSolutionWpf.Data;

namespace UaaSolutionWpf.Commands
{
    /// <summary>
    /// DUT (Device Under Test) record model for serialization
    /// </summary>
    public class DUTRecord
    {
        public string SerialNumber { get; set; }
        public DateTime BirthTime { get; set; }
        public string RecipeId { get; set; }
        public string OperatorId { get; set; }
        public List<DUTValueEntry> Entries { get; set; } = new List<DUTValueEntry>();

        [JsonIgnore]
        public string FilePath { get; set; }

        public DUTRecord()
        {
            // Default constructor for deserialization
        }

        public DUTRecord(string serialNumber, string recipeId = null, string operatorId = null)
        {
            SerialNumber = serialNumber;
            BirthTime = DateTime.Now;
            RecipeId = recipeId;
            OperatorId = operatorId;
        }

        public void AddEntry(string state, double value, string unit, DateTime? timestamp = null)
        {
            Entries.Add(new DUTValueEntry
            {
                State = state,
                Value = value,
                Unit = unit,
                Timestamp = timestamp ?? DateTime.Now
            });
        }
    }

    /// <summary>
    /// Entry for a specific measurement or state change of the DUT
    /// </summary>
    public class DUTValueEntry
    {
        public string State { get; set; }
        public double Value { get; set; }
        public string Unit { get; set; }
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Command to register birth of a DUT with a serial number
    /// </summary>
    public class DUTBirthCommand : Command<RealTimeDataManager>
    {
        private readonly string _serialNumber;
        private readonly string _recipeId;
        private readonly string _operatorId;

        public DUTBirthCommand(
            RealTimeDataManager dataManager,
            string serialNumber,
            string recipeId = null,
            string operatorId = null,
            ILogger logger = null)
            : base(
                dataManager,
                $"DUTBirth-{serialNumber}",
                $"Register birth of DUT with serial number {serialNumber}",
                logger)
        {
            _serialNumber = !string.IsNullOrEmpty(serialNumber) ? serialNumber : throw new ArgumentNullException(nameof(serialNumber));
            _recipeId = recipeId;
            _operatorId = operatorId;
        }

        protected override async Task<CommandResult> ExecuteInternalAsync()
        {
            try
            {
                _logger.Information("Registering birth of DUT with serial number {SerialNumber}", _serialNumber);

                // Create a new DUT record
                var dutRecord = new DUTRecord(_serialNumber, _recipeId, _operatorId);

                // Create the directory if it doesn't exist
                string dirPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DUT_Logs");
                Directory.CreateDirectory(dirPath);

                // Generate a unique filename with serial number and timestamp
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string filename = $"{_serialNumber}_{timestamp}.json";
                string filePath = Path.Combine(dirPath, filename);

                // Save the file path in the record for later use
                dutRecord.FilePath = filePath;

                // Save the initial record as JSON
                await SaveDUTRecordAsync(dutRecord, filePath);

                return CommandResult.Successful($"DUT with serial number {_serialNumber} registered successfully. Log file: {filePath}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error registering DUT with serial number {SerialNumber}", _serialNumber);
                return CommandResult.Failed($"Error registering DUT: {ex.Message}", ex);
            }
        }

        private static async Task SaveDUTRecordAsync(DUTRecord record, string filePath)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
            {
                await JsonSerializer.SerializeAsync(fileStream, record, options);
            }
        }
    }

    /// <summary>
    /// Command to add a value entry to an existing DUT record
    /// </summary>
    public partial class DUTAddValueCommand : Command<RealTimeDataManager>
    {
        private readonly string _filePath;
        private readonly Func<string> _filePathProvider;
        private readonly string _state;
        private readonly string _channelName;
        private readonly double? _manualValue;
        private readonly string _manualUnit;

        // Constructor for direct string path (existing code)
        public DUTAddValueCommand(
            RealTimeDataManager dataManager,
            string filePath,
            string state,
            string channelName,
            ILogger logger = null)
            : base(
                dataManager,
                $"DUTAddValue-{Path.GetFileNameWithoutExtension(filePath)}-{state}-{channelName}",
                $"Add {channelName} value for state {state} to DUT record",
                logger)
        {
            _filePath = !string.IsNullOrEmpty(filePath) ? filePath : throw new ArgumentNullException(nameof(filePath));
            _filePathProvider = null; // Not using a provider
            _state = !string.IsNullOrEmpty(state) ? state : throw new ArgumentNullException(nameof(state));
            _channelName = !string.IsNullOrEmpty(channelName) ? channelName : throw new ArgumentNullException(nameof(channelName));
            _manualValue = null;
            _manualUnit = null;
        }

        // New constructor for lambda expression
        public DUTAddValueCommand(
            RealTimeDataManager dataManager,
            Func<string> filePathProvider,
            string state,
            string channelName,
            ILogger logger = null)
            : base(
                dataManager,
                $"DUTAddValue-{state}-{channelName}",
                $"Add {channelName} value for state {state} to DUT record",
                logger)
        {
            _filePath = null; // Will be resolved at execution time
            _filePathProvider = filePathProvider ?? throw new ArgumentNullException(nameof(filePathProvider));
            _state = !string.IsNullOrEmpty(state) ? state : throw new ArgumentNullException(nameof(state));
            _channelName = !string.IsNullOrEmpty(channelName) ? channelName : throw new ArgumentNullException(nameof(channelName));
            _manualValue = null;
            _manualUnit = null;
        }

        // Same for manual values - first the direct string version
        public DUTAddValueCommand(
            RealTimeDataManager dataManager,
            string filePath,
            string state,
            double value,
            string unit,
            ILogger logger = null)
            : base(
                dataManager,
                $"DUTAddValue-{Path.GetFileNameWithoutExtension(filePath)}-{state}",
                $"Add manual value for state {state} to DUT record",
                logger)
        {
            _filePath = !string.IsNullOrEmpty(filePath) ? filePath : throw new ArgumentNullException(nameof(filePath));
            _filePathProvider = null; // Not using a provider
            _state = !string.IsNullOrEmpty(state) ? state : throw new ArgumentNullException(nameof(state));
            _channelName = null;
            _manualValue = value;
            _manualUnit = unit;
        }

        // New constructor for lambda expression with manual values
        public DUTAddValueCommand(
            RealTimeDataManager dataManager,
            Func<string> filePathProvider,
            string state,
            double value,
            string unit,
            ILogger logger = null)
            : base(
                dataManager,
                $"DUTAddValue-{state}",
                $"Add manual value for state {state} to DUT record",
                logger)
        {
            _filePath = null; // Will be resolved at execution time
            _filePathProvider = filePathProvider ?? throw new ArgumentNullException(nameof(filePathProvider));
            _state = !string.IsNullOrEmpty(state) ? state : throw new ArgumentNullException(nameof(state));
            _channelName = null;
            _manualValue = value;
            _manualUnit = unit;
        }

        protected override async Task<CommandResult> ExecuteInternalAsync()
        {
            try
            {
                // Resolve the file path
            string resolvedFilePath = _filePath;
            
            // If using a provider, resolve it now
            if (resolvedFilePath == null && _filePathProvider != null)
            {
                resolvedFilePath = _filePathProvider();
                if (string.IsNullOrEmpty(resolvedFilePath))
                {
                    return CommandResult.Failed("Failed to resolve DUT file path");
                }
            }
                // Check if the file exists
                if (!File.Exists(resolvedFilePath))
                {
                    return CommandResult.Failed($"DUT record file not found: {resolvedFilePath}");
                }

                // Get the value from the specified channel or use manual value
                double value;
                string unit;

                if (_channelName != null)
                {
                    // Get value from the channel
                    if (!_context.TryGetChannelValue(_channelName, out var measurement))
                    {
                        return CommandResult.Failed($"Channel {_channelName} not found or has no value");
                    }

                    if (!measurement.IsValid)
                    {
                        return CommandResult.Failed($"Channel {_channelName} has an invalid value");
                    }

                    value = measurement.Value;
                    unit = measurement.Unit;
                    _logger.Information("Adding value from channel {ChannelName} for DUT state {State}: {Value} {Unit}",
                        _channelName, _state, value, unit);
                }
                else
                {
                    // Use manual value
                    value = _manualValue ?? 0;
                    unit = _manualUnit ?? "";
                    _logger.Information("Adding manual value for DUT state {State}: {Value} {Unit}",
                        _state, value, unit);
                }

                // Load the existing DUT record
                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                DUTRecord dutRecord;
                using (var fileStream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    dutRecord = await JsonSerializer.DeserializeAsync<DUTRecord>(fileStream, options);
                }

                // Add the entry
                dutRecord.AddEntry(_state, value, unit);
                dutRecord.FilePath = _filePath;

                // Save the updated record
                await SaveDUTRecordAsync(dutRecord, _filePath);

                return CommandResult.Successful($"Added value {value} {unit} for state {_state} to DUT record");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error adding value to DUT record for state {State}", _state);
                return CommandResult.Failed($"Error adding value to DUT record: {ex.Message}", ex);
            }
        }

        private static async Task SaveDUTRecordAsync(DUTRecord record, string filePath)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
            {
                await JsonSerializer.SerializeAsync(fileStream, record, options);
            }
        }
    }

    /// <summary>
    /// Helper class for DUT (Device Under Test) related commands and operations
    /// </summary>
    public class DUTManager
    {
        // Keep track of the current DUT file path
        private string _currentDUTFilePath;
        private readonly ILogger _logger;
        private readonly RealTimeDataManager _dataManager;

        public string CurrentDUTFilePath => _currentDUTFilePath;
        public string CurrentSerialNumber => Path.GetFileNameWithoutExtension(_currentDUTFilePath)?.Split('_')[0];

        public DUTManager(RealTimeDataManager dataManager, ILogger logger)
        {
            _dataManager = dataManager ?? throw new ArgumentNullException(nameof(dataManager));
            _logger = logger?.ForContext<DUTManager>() ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Set the current DUT file path
        /// </summary>
        public void SetCurrentDUT(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"DUT record file not found: {filePath}");
            }

            _currentDUTFilePath = filePath;
            _logger.Information("Current DUT set to {FilePath}", filePath);
        }

        /// <summary>
        /// Create a new DUT with the specified serial number
        /// </summary>
        public async Task<string> CreateDUTAsync(string serialNumber, string recipeId = null, string operatorId = null)
        {
            var command = new DUTBirthCommand(_dataManager, serialNumber, recipeId, operatorId, _logger);
            var result = await command.ExecuteAsync(System.Threading.CancellationToken.None);

            if (result.Success)
            {
                // Extract the file path from the success message
                // The message format is: "DUT with serial number {_serialNumber} registered successfully. Log file: {filePath}"
                string message = result.Message;
                int filePathIndex = message.LastIndexOf("Log file: ");
                if (filePathIndex != -1)
                {
                    _currentDUTFilePath = message.Substring(filePathIndex + 10); // "Log file: " is 10 characters
                }

                return _currentDUTFilePath;
            }
            else
            {
                throw new Exception($"Failed to create DUT: {result.Message}");
            }
        }

        /// <summary>
        /// Add a value to the current DUT record from a specified channel
        /// </summary>
        public async Task AddValueFromChannelAsync(string state, string channelName)
        {
            if (string.IsNullOrEmpty(_currentDUTFilePath))
            {
                throw new InvalidOperationException("No current DUT is set. Create or load a DUT first.");
            }

            // Use the string overload of DUTAddValueCommand constructor
            var command = new DUTAddValueCommand(_dataManager, _currentDUTFilePath, state, channelName, _logger);
            var result = await command.ExecuteAsync(System.Threading.CancellationToken.None);

            if (!result.Success)
            {
                throw new Exception($"Failed to add value: {result.Message}");
            }
        }

        /// <summary>
        /// Add a manual value to the current DUT record
        /// </summary>
        public async Task AddManualValueAsync(string state, double value, string unit)
        {
            if (string.IsNullOrEmpty(_currentDUTFilePath))
            {
                throw new InvalidOperationException("No current DUT is set. Create or load a DUT first.");
            }

            // Use the string overload of DUTAddValueCommand constructor
            var command = new DUTAddValueCommand(_dataManager, _currentDUTFilePath, state, value, unit, _logger);
            var result = await command.ExecuteAsync(System.Threading.CancellationToken.None);

            if (!result.Success)
            {
                throw new Exception($"Failed to add value: {result.Message}");
            }
        }

        /// <summary>
        /// Load an existing DUT file
        /// </summary>
        public async Task<DUTRecord> LoadDUTAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    throw new FileNotFoundException($"DUT record file not found: {filePath}");
                }

                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var dutRecord = await JsonSerializer.DeserializeAsync<DUTRecord>(fileStream, options);
                    dutRecord.FilePath = filePath;
                    _currentDUTFilePath = filePath;
                    return dutRecord;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error loading DUT file {FilePath}", filePath);
                throw;
            }
        }
    }
}