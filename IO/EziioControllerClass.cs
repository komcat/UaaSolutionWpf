using FASTECH;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;


namespace UaaSolutionWpf.IO
{
    public class EziioControllerClass
    {
        internal class IOConfig
        {
            public string name { get; set; }
            public int OutputQty { get; set; }
            public int InputQty { get; set; }
            public Dictionary<string, int> outputs { get; set; }
            public Dictionary<string, int> inputs { get; set; }
        }

        public const int TCP = 0;
        public const int UDP = 1;
        public const int OUTPUTPIN = 16;

        private IPAddress ip;
        private int nCommType;
        private int nBdID;
        private bool isConnected;

        // Input-related fields
     
        private Dictionary<string, int> inputMapping;
        private Thread scanningThread;
        private Thread inputStatusThread;
        private bool continueScanning;
        private bool continueGettingInputStatus;
        public bool[] inputStatus = new bool[16];
        private bool[] inputStatusPreviously = new bool[16];

        // Output-related fields
        public bool[] PinStatus = new bool[16];
        private Dictionary<string, int> outputPinMapping;
        public static readonly uint[] PinMasks = new uint[16]
        {
            0x00010000, 0x00020000, 0x00040000, 0x00080000,
            0x00100000, 0x00200000, 0x00400000, 0x00800000,
            0x01000000, 0x02000000, 0x04000000, 0x08000000,
            0x10000000, 0x20000000, 0x40000000, 0x80000000
        };

        // Events
        public event Action Connected;
        public event Action Disconnected;
        public event Action<string, bool> InputStatusChanged;
        public event Action<string> OutputSetSuccessfully;
        public event Action<string> OutputClearedSuccessfully;

        // Thread for continuously getting pin status
        private Thread statusThread;
        private bool continueGettingStatus;

        private uint lastLoggedOutputStatus = uint.MaxValue;
        private uint lastLoggedInputStatus = uint.MaxValue;

        private readonly ILogger _logger;

        public EziioControllerClass(int commType, int bdID, string ipAddress, string inputMappingFilePath, string outputMappingFilePath, ILogger logger)
        {

            _logger= logger;
            nCommType = commType;
            nBdID = bdID;

            if (!IPAddress.TryParse(ipAddress, out ip))
            {
                throw new ArgumentException("Invalid IP address format.");
            }

            LoadInputMapping(inputMappingFilePath);
            LoadOutputPinMapping(outputMappingFilePath);
            _logger = logger;
        }

        private void LoadInputMapping(string mappingFilePath)
        {
            if (File.Exists(mappingFilePath))
            {
                string json = File.ReadAllText(mappingFilePath);
                var config = JsonConvert.DeserializeObject<IOConfig>(json);
                inputMapping = config.inputs ?? new Dictionary<string, int>();
                Log.Information($"Loaded input mapping from {mappingFilePath} successfully");
            }
            else
            {
                Log.Error(new FileNotFoundException("Input mapping file not found."), "Input mapping file not found.");
            }
        }

        private void LoadOutputPinMapping(string filePath)
        {
            if (File.Exists(filePath))
            {
                string json = File.ReadAllText(filePath);
                var config = JsonConvert.DeserializeObject<IOConfig>(json);
                outputPinMapping = config.outputs;
                Log.Information($"Loaded output pin mapping from {filePath} successfully");
            }
            else
            {
                Log.Error(new FileNotFoundException("Output pin mapping file not found."), "Output pin mapping file not found.");
                outputPinMapping = new Dictionary<string, int>();
            }
        }

        public bool Connect()
        {
            bool bSuccess = true;

            switch (nCommType)
            {
                case TCP:
                    if (EziMOTIONPlusELib.FAS_ConnectTCP(ip, nBdID) == false)
                    {
                        Log.Error("Eziio TCP Connection Fail!");
                        bSuccess = false;
                    }
                    break;

                case UDP:
                    if (EziMOTIONPlusELib.FAS_Connect(ip, nBdID) == false)
                    {
                        Log.Error("Eziio UDP Connection Fail!");
                        bSuccess = false;
                    }
                    break;

                default:
                    Log.Error("Wrong communication type.");
                    bSuccess = false;
                    break;
            }

            if (bSuccess)
            {
                Log.Information("Connected successfully.");
                isConnected = true;
                Connected?.Invoke();
                StartScanning();
                StartGettingPinStatus();
            }

            return bSuccess;
        }

        public void CloseConnection()
        {
            StopScanning();
            StopGettingPinStatus();
            StopGettingInputStatus();
            EziMOTIONPlusELib.FAS_Close(nBdID);
            isConnected = false;
            Disconnected?.Invoke();
        }

        public bool IsConnected()
        {
            return isConnected;
        }

        // Input-related methods
        private void StartScanning()
        {
            continueScanning = true;
            scanningThread = new Thread(ScanInputs);
            scanningThread.IsBackground = true;
            scanningThread.Start();
        }

        private void StopScanning()
        {
            continueScanning = false;
            scanningThread?.Join();
        }

        private void ScanInputs()
        {
            uint previousInput = 0;

            while (continueScanning)
            {
                uint uInput = 0;
                uint uLatch = 0;

                if (EziMOTIONPlusELib.FAS_GetInput(nBdID, ref uInput, ref uLatch) == EziMOTIONPlusELib.FMM_OK)
                {
                    if (uInput != previousInput)
                    {
                        //WireInputStatus(uInput);
                        for (int i = 0; i < 16; i++)
                        {
                            bool isOn = ((uInput & (0x01 << i)) != 0);
                            inputStatus[i] = isOn;
                            if (inputStatusPreviously[i] != inputStatus[i])
                            {
                                string ikey = GetKeyByValue(i);
                                InputStatusChanged?.Invoke(ikey, isOn);
                            }
                        }
                        previousInput = uInput;
                        CopyPreviousIsNowArray();
                    }
                }

                Thread.Sleep(10);
            }
        }

        private void CopyPreviousIsNowArray()
        {
            Array.Copy(inputStatus, inputStatusPreviously, 16);
        }



        public string GetKeyByValue(int value)
        {
            var keyValue = inputMapping.FirstOrDefault(x => x.Value == value);
            return keyValue.Equals(default(KeyValuePair<string, int>)) ? null : keyValue.Key;
        }

        public bool GetInputStatus(string keyname)
        {
            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");

            if (string.IsNullOrWhiteSpace(keyname) || keyname == "not_used")
            {
                // Key name is empty, null, whitespace, or "not_used", ignore without logging an error
                return true;
            }

            if (inputMapping.TryGetValue(keyname, out int value))
            {
                bool status = inputStatus[value];
                //_logger.Information("[{Timestamp}] Input status for {Keyname} (pin {Value}): {Status}", timestamp, keyname, value, status);
                return status;
            }
            else
            {
                _logger.Error("[{Timestamp}] Key name {Keyname} not found.", timestamp, keyname);
                //throw new ArgumentException("Key name not found.");
                return true;
            }
        }


        // Output-related methods
        public bool GetOutput()
        {
            uint uOutput = 0;
            uint uStatus = 0;

            if (EziMOTIONPlusELib.FAS_GetOutput(nBdID, ref uOutput, ref uStatus) != EziMOTIONPlusELib.FMM_OK)
            {
                _logger.Error("Function(FAS_GetOutput) was failed.");
                return false;
            }
            else
            {
                _logger.Information("Output status: {OutputStatus}", ConvertToBinaryWithSpaces(uOutput));
                UpdatePinStatus(uOutput);
            }

            return true;
        }

        private void UpdatePinStatus(uint uOutput)
        {
            for (int i = 0; i < 16; i++)
            {
                PinStatus[i] = (uOutput & PinMasks[i]) != 0;
            }
        }

        public bool SetOutput(int pinNum)
        {
            uint uSetMask = PinMasks[pinNum];
            uint uClrMask = 0x00000000;

            if (EziMOTIONPlusELib.FAS_SetOutput(nBdID, uSetMask, uClrMask) != EziMOTIONPlusELib.FMM_OK)
            {
                _logger.Error("Function(FAS_SetOutput) was failed.");
                return false;
            }
            else
            {
                if (VerifyOutputSet(pinNum))
                {
                    _logger.Information("FAS_SetOutput Success!");
                    OutputSetSuccessfully?.Invoke($"Pin {pinNum}");
                    return true;
                }
                else
                {
                    _logger.Warning("FAS_SetOutput was not verified.");
                    return false;
                }
            }
        }

        public bool ClearOutput(int pinNum)
        {
            uint uSetMask = 0x00000000;
            uint uClrMask = PinMasks[pinNum];

            if (EziMOTIONPlusELib.FAS_SetOutput(nBdID, uSetMask, uClrMask) != EziMOTIONPlusELib.FMM_OK)
            {
                _logger.Error("Function(FAS_SetOutput) was failed.");
                return false;
            }
            else
            {
                if (VerifyOutputCleared(pinNum))
                {
                    _logger.Information("FAS_SetOutput Success!");
                    OutputClearedSuccessfully?.Invoke($"Pin {pinNum}");
                    return true;
                }
                else
                {
                    _logger.Warning("FAS_SetOutput was not verified.");
                    return false;
                }
            }
        }

        public bool SetOutputByName(string pinName)
        {
            if (outputPinMapping.TryGetValue(pinName, out int pinNum))
            {
                return SetOutput(pinNum);
            }
            else
            {
                _logger.Error("Pin name {PinName} not found in mapping.", pinName);
                return false;
            }
        }

        public bool ClearOutputByName(string pinName)
        {
            if (outputPinMapping.TryGetValue(pinName, out int pinNum))
            {
                return ClearOutput(pinNum);
            }
            else
            {
                _logger.Error("Pin name {PinName} not found in mapping.", pinName);
                return false;
            }
        }

        private bool VerifyOutputSet(int pinNum)
        {
            GetOutput();
            return PinStatus[pinNum];
        }

        private bool VerifyOutputCleared(int pinNum)
        {
            GetOutput();
            return !PinStatus[pinNum];
        }

        public static string ConvertToBinaryWithSpaces(uint value)
        {
            string binaryString = Convert.ToString(value, 2).PadLeft(32, '0');
            return string.Join(" ", Enumerable.Range(0, 8).Select(i => binaryString.Substring(i * 4, 4)));
        }

        // Continuously get pin status
        private void StartGettingPinStatus()
        {
            continueGettingStatus = true;
            statusThread = new Thread(GetPinStatusContinuously);
            statusThread.IsBackground = true;
            statusThread.Start();
        }

        private void StopGettingPinStatus()
        {
            continueGettingStatus = false;
            statusThread?.Join();
        }
        private void StopGettingInputStatus()
        {
            continueGettingInputStatus = false;
            inputStatusThread?.Join();
        }
        private void GetPinStatusContinuously()
        {
            while (continueGettingStatus)
            {
                uint currentOutputStatus = GetOutputStatus();
                if (currentOutputStatus != lastLoggedOutputStatus)
                {
                    lastLoggedOutputStatus = currentOutputStatus;
                    _logger.Information("Output status: {OutputStatus}", ConvertToBinaryWithSpaces(currentOutputStatus));
                }

                uint currentInputStatus = GetInputStatus();
                if (currentInputStatus != lastLoggedInputStatus)
                {
                    lastLoggedInputStatus = currentInputStatus;
                    _logger.Information("Input status: {InputStatus}", ConvertToBinaryWithSpaces(currentInputStatus));
                }

                Thread.Sleep(100); // Adjust the sleep time as needed
            }
        }

        private uint GetOutputStatus()
        {
            uint uOutput = 0;
            uint uStatus = 0;

            if (EziMOTIONPlusELib.FAS_GetOutput(nBdID, ref uOutput, ref uStatus) != EziMOTIONPlusELib.FMM_OK)
            {
                _logger.Error("Function(FAS_GetOutput) was failed.");
                return uint.MaxValue; // Return a value that indicates an error
            }
            else
            {
                UpdatePinStatus(uOutput);
                return uOutput;
            }
        }

        private uint GetInputStatus()
        {
            uint uInput = 0;
            uint uLatch = 0;

            if (EziMOTIONPlusELib.FAS_GetInput(nBdID, ref uInput, ref uLatch) != EziMOTIONPlusELib.FMM_OK)
            {
                _logger.Error("Function(FAS_GetInput) was failed.");
                return uint.MaxValue; // Return a value that indicates an error
            }
            else
            {
                //WireInputStatus(uInput);
                for (int i = 0; i < 16; i++)
                {
                    bool isOn = ((uInput & (0x01 << i)) != 0);
                    inputStatus[i] = isOn;
                    if (inputStatusPreviously[i] != inputStatus[i])
                    {
                        string ikey = GetKeyByValue(i);
                        InputStatusChanged?.Invoke(ikey, isOn);
                    }
                }
                CopyPreviousIsNowArray();
                return uInput;
            }
        }
    }
}
