using FASTECH;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Serilog;

namespace UaaSolutionWpf.IO
{
    /// <summary>
    /// Configuration class for EziIO device
    /// </summary>
    public class EziioConfiguration
    {
        public int IpA { get; set; } = 192;
        public int IpB { get; set; } = 168;
        public int IpC { get; set; } = 0;
        public int IpD { get; set; } = 3;
        public Dictionary<string, int> PinMapping { get; set; } = new Dictionary<string, int>();
    }

    /// <summary>
    /// Custom exception for EziIO operations
    /// </summary>
    public class EziioException : Exception
    {
        public int BoardId { get; }
        public string Operation { get; }

        public EziioException(string message, int boardId, string operation, Exception innerException = null)
            : base(message, innerException)
        {
            BoardId = boardId;
            Operation = operation;
        }
    }

    /// <summary>
    /// Connection type enumeration
    /// </summary>
    public enum ConnectionType
    {
        TCP = 0,
        UDP = 1
    }

    /// <summary>
    /// Pin state enumeration
    /// </summary>
    public enum PinState
    {
        Off = 0,
        On = 1
    }

    /// <summary>
    /// Class to track pin status information
    /// </summary>
    public class PinStatus
    {
        public bool State { get; set; }
        public DateTime LastChanged { get; set; }
        public int ChangeCount { get; set; }
    }

    /// <summary>
    /// Main class for controlling Fastech EziIO devices
    /// </summary>
    public class EziioClass : IDisposable
    {
        public const int OUTPUTPIN = 16;
        private readonly ILogger _logger;
        private readonly EziioConfiguration _config;
        private readonly object _pinStatusLock = new object();
        private readonly Dictionary<int, PinStatus> _pinStatuses = new Dictionary<int, PinStatus>();
        private bool _disposed;

        /// <summary>
        /// Pin mask array for output control
        /// </summary>
        public static readonly uint[] PinMasks = new uint[16]
        {
            0x00010000, // Pin 0
            0x00020000, // Pin 1
            0x00040000, // Pin 2
            0x00080000, // Pin 3
            0x00100000, // Pin 4
            0x00200000, // Pin 5
            0x00400000, // Pin 6
            0x00800000, // Pin 7
            0x01000000, // Pin 8
            0x02000000, // Pin 9
            0x04000000, // Pin 10
            0x08000000, // Pin 11
            0x10000000, // Pin 12
            0x20000000, // Pin 13
            0x40000000, // Pin 14
            0x80000000  // Pin 15
        };

        /// <summary>
        /// Dictionary for pin name to number mapping
        /// </summary>
        public static Dictionary<string, int> PinMapping { get; private set; } = new Dictionary<string, int>();

        /// <summary>
        /// Constructor
        /// </summary>
        public EziioClass(ILogger logger, EziioConfiguration config = null)
        {
            _logger = logger.ForContext<EziioClass>();
            _config = config ?? new EziioConfiguration();
        }

        /// <summary>
        /// Connect to EziIO device
        /// </summary>
        public async Task<bool> ConnectAsync(ConnectionType connType, int boardId, int ipA = 192, int ipB = 168, int ipC = 0, int ipD = 3)
        {
            return await Task.Run(() => Connect(connType, boardId, ipA, ipB, ipC, ipD));
        }

        /// <summary>
        /// Synchronous connection method
        /// </summary>
        public bool Connect(ConnectionType connType, int boardId, int ipA = 192, int ipB = 168, int ipC = 0, int ipD = 3)
        {
            try
            {
                ValidateIPAddress(ipA, ipB, ipC, ipD);

                IPAddress ip = new IPAddress(new byte[] { (byte)ipA, (byte)ipB, (byte)ipC, (byte)ipD });
                bool success = true;

                switch (connType)
                {
                    case ConnectionType.TCP:
                        if (!EziMOTIONPlusELib.FAS_ConnectTCP(ip, boardId))
                        {
                            _logger.Error("TCP Connection Failed for IP: {IP}, BoardID: {BoardID}", ip, boardId);
                            success = false;
                        }
                        break;

                    case ConnectionType.UDP:
                        if (!EziMOTIONPlusELib.FAS_Connect(ip, boardId))
                        {
                            _logger.Error("UDP Connection Failed for IP: {IP}, BoardID: {BoardID}", ip, boardId);
                            success = false;
                        }
                        break;

                    default:
                        _logger.Error("Invalid communication type: {CommType}", connType);
                        success = false;
                        break;
                }

                if (success)
                {
                    _logger.Information("Connected successfully to IP: {IP}, BoardID: {BoardID}", ip, boardId);
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Connection failed for IP: {IP}.{IP}.{IP}.{IP}, BoardID: {BoardID}",
                    ipA, ipB, ipC, ipD, boardId);
                return false;
            }
        }

        /// <summary>
        /// Close connection to EziIO device
        /// </summary>
        public void CloseConnection(int boardId)
        {
            try
            {
                EziMOTIONPlusELib.FAS_Close(boardId);
                _logger.Information("Connection closed for BoardID: {BoardID}", boardId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error closing connection for BoardID: {BoardID}", boardId);
            }
        }

        /// <summary>
        /// Get output status
        /// </summary>
        public bool GetOutput(int boardId)
        {
            lock (_pinStatusLock)
            {
                try
                {
                    uint uOutput = 0;
                    uint uStatus = 0;

                    if (EziMOTIONPlusELib.FAS_GetOutput(boardId, ref uOutput, ref uStatus) != EziMOTIONPlusELib.FMM_OK)
                    {
                        throw new EziioException("Failed to get output", boardId, "GetOutput");
                    }

                    UpdatePinStatus(uOutput);
                    _logger.Information("Output status: {OutputStatus}", ConvertToBinaryWithSpaces(uOutput));
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error getting output for BoardID: {BoardID}", boardId);
                    return false;
                }
            }
        }

        /// <summary>
        /// Set output pin state
        /// </summary>
        public bool SetOutput(int boardId, int pinNum)
        {
            try
            {
                ValidatePin(pinNum);

                uint uSetMask = PinMasks[pinNum];
                uint uClrMask = 0x00000000;

                if (EziMOTIONPlusELib.FAS_SetOutput(boardId, uSetMask, uClrMask) != EziMOTIONPlusELib.FMM_OK)
                {
                    throw new EziioException($"Failed to set output for pin {pinNum}", boardId, "SetOutput");
                }

                _logger.Information("Successfully set output for BoardID: {BoardID}, Pin: {PinNumber}", boardId, pinNum);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error setting output for BoardID: {BoardID}, Pin: {PinNumber}", boardId, pinNum);
                return false;
            }
        }

        /// <summary>
        /// Clear output pin state
        /// </summary>
        public bool ClearOutput(int boardId, int pinNum)
        {
            try
            {
                ValidatePin(pinNum);

                uint uSetMask = 0x00000000;
                uint uClrMask = PinMasks[pinNum];

                if (EziMOTIONPlusELib.FAS_SetOutput(boardId, uSetMask, uClrMask) != EziMOTIONPlusELib.FMM_OK)
                {
                    throw new EziioException($"Failed to clear output for pin {pinNum}", boardId, "ClearOutput");
                }

                _logger.Information("Successfully cleared output for BoardID: {BoardID}, Pin: {PinNumber}", boardId, pinNum);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error clearing output for BoardID: {BoardID}, Pin: {PinNumber}", boardId, pinNum);
                return false;
            }
        }

        /// <summary>
        /// Load pin mapping from JSON file
        /// </summary>
        public void LoadPinMapping(string filePath)
        {
            try
            {
                if (System.IO.File.Exists(filePath))
                {
                    string json = System.IO.File.ReadAllText(filePath);
                    PinMapping = JsonConvert.DeserializeObject<Dictionary<string, int>>(json);
                    _logger.Information("Successfully loaded pin mapping from {FilePath}", filePath);
                }
                else
                {
                    _logger.Warning("Pin mapping file not found at {FilePath}", filePath);
                    PinMapping = new Dictionary<string, int>();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error loading pin mapping from {FilePath}", filePath);
                PinMapping = new Dictionary<string, int>();
            }
        }

        /// <summary>
        /// Convert uint to binary string with spaces
        /// </summary>
        public static string ConvertToBinaryWithSpaces(uint value)
        {
            string binaryString = Convert.ToString(value, 2).PadLeft(32, '0');
            for (int i = 4; i < binaryString.Length; i += 5)
            {
                binaryString = binaryString.Insert(i, " ");
            }
            return binaryString;
        }

        /// <summary>
        /// Get pin status
        /// </summary>
        public PinStatus GetPinStatus(int pinNum)
        {
            lock (_pinStatusLock)
            {
                ValidatePin(pinNum);
                return _pinStatuses.ContainsKey(pinNum) ? _pinStatuses[pinNum] : null;
            }
        }

        private void UpdatePinStatus(uint uOutput)
        {
            lock (_pinStatusLock)
            {
                for (int i = 0; i < OUTPUTPIN; i++)
                {
                    bool newState = (uOutput & PinMasks[i]) != 0;
                    if (!_pinStatuses.ContainsKey(i))
                    {
                        _pinStatuses[i] = new PinStatus { State = newState, LastChanged = DateTime.Now };
                    }
                    else if (_pinStatuses[i].State != newState)
                    {
                        _pinStatuses[i].State = newState;
                        _pinStatuses[i].LastChanged = DateTime.Now;
                        _pinStatuses[i].ChangeCount++;
                    }
                }
            }
        }

        private void ValidatePin(int pinNum)
        {
            if (pinNum < 0 || pinNum >= OUTPUTPIN)
            {
                throw new ArgumentOutOfRangeException(nameof(pinNum),
                    $"Pin number must be between 0 and {OUTPUTPIN - 1}");
            }
        }

        private void ValidateIPAddress(int ipA, int ipB, int ipC, int ipD)
        {
            if (ipA < 0 || ipA > 255 || ipB < 0 || ipB > 255 ||
                ipC < 0 || ipC > 255 || ipD < 0 || ipD > 255)
            {
                throw new ArgumentException("Invalid IP address octets");
            }
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
                    // Clean up any managed resources here
                    try
                    {
                        // Note: You might want to store the boardId as a class member
                        // to properly close the connection here
                        // CloseConnection(boardId);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error during disposal");
                    }
                }

                _disposed = true;
            }
        }

        ~EziioClass()
        {
            Dispose(false);
        }
    }
}