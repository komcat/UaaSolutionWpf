using FASTECH;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Globalization;
using Serilog;

namespace UaaSolutionWpf.IO
{
    public class EziioClass
    {
        public const int TCP = 0;
        public const int UDP = 1;
        public const int OUTPUTPIN = 16;
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
        /// PinStatus 0=OFF, 1=ON
        /// </summary>
        public static bool[] PinStatus = new bool[16];
        public static Dictionary<string, int> PinMapping;

        public int ipA = 192;
        public int ipB = 168;
        public int ipC = 0;
        public int ipD = 3;

        private readonly ILogger _logger;

        public EziioClass(ILogger logger)
        {
            _logger = logger.ForContext<EziioClass>();
        }

        /// <summary>
        /// Eziio connect function
        /// </summary>
        /// <param name="nCommType">TCP=0, UDP=1</param>
        /// <param name="nBdID">internal ID</param>
        /// <param name="_ipa">ipaddress 192</param>
        /// <param name="_ipb">ipaddress 168</param>
        /// <param name="_ipc">ipaddress 0</param>
        /// <param name="_ipd">ipaddress 3</param>
        /// <returns></returns>
        public bool Connect(int nCommType, int nBdID, int _ipa = 192, int _ipb = 168, int _ipc = 0, int _ipd = 3)
        {
            IPAddress ip = new IPAddress(new byte[] { (byte)_ipa, (byte)_ipb, (byte)_ipc, (byte)_ipd });
            bool bSuccess = true;

            // Connection
            switch (nCommType)
            {
                case TCP:
                    // TCP Connection
                    if (EziMOTIONPlusELib.FAS_ConnectTCP(ip, nBdID) == false)
                    {
                        _logger.Error("TCP Connection Failed for IP: {IP}, BoardID: {BoardID}", ip, nBdID);
                        bSuccess = false;
                    }
                    break;

                case UDP:
                    // UDP Connection
                    if (EziMOTIONPlusELib.FAS_Connect(ip, nBdID) == false)
                    {
                        _logger.Error("UDP Connection Failed for IP: {IP}, BoardID: {BoardID}", ip, nBdID);
                        bSuccess = false;
                    }
                    break;

                default:
                    _logger.Error("Wrong communication type: {CommType}", nCommType);
                    bSuccess = false;
                    break;
            }

            if (bSuccess)
                _logger.Information("Connected successfully to IP: {IP}, BoardID: {BoardID}", ip, nBdID);

            return bSuccess;
        }

        public void CloseConnection(int nBdID)
        {
            EziMOTIONPlusELib.FAS_Close(nBdID);
            _logger.Information("Connection closed for BoardID: {BoardID}", nBdID);
        }

        public bool GetOutput(int nBdID)
        {
            uint uOutput = 0;
            uint uStatus = 0;

            if (EziMOTIONPlusELib.FAS_GetOutput(nBdID, ref uOutput, ref uStatus) != EziMOTIONPlusELib.FMM_OK)
            {
                _logger.Error("Function(FAS_GetOutput) failed for BoardID: {BoardID}", nBdID);
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

        public void CheckPinStatus(uint uOutput)
        {
            for (int i = 0; i < 16; i++)
            {
                bool isSet = (uOutput & PinMasks[i]) != 0;
                _logger.Information("Pin {PinNumber}: {Status}", i, isSet ? "ON" : "OFF");
            }
        }

        public bool SetOutput(int nBdID, int pinNum)
        {
            uint uSetMask = PinMasks[pinNum];
            uint uClrMask = 0x00000000;

            if (EziMOTIONPlusELib.FAS_SetOutput(nBdID, uSetMask, uClrMask) != EziMOTIONPlusELib.FMM_OK)
            {
                _logger.Error("Function(FAS_SetOutput) failed for BoardID: {BoardID}, Pin: {PinNumber}", nBdID, pinNum);
                return false;
            }
            else
            {
                _logger.Information("Successfully set output for BoardID: {BoardID}, Pin: {PinNumber}", nBdID, pinNum);
                return true;
            }
        }

        public bool ClearOutput(int nBdID, int pinNum)
        {
            uint uSetMask = 0x00000000;
            uint uClrMask = PinMasks[pinNum];

            if (EziMOTIONPlusELib.FAS_SetOutput(nBdID, uSetMask, uClrMask) != EziMOTIONPlusELib.FMM_OK)
            {
                _logger.Error("Function(FAS_SetOutput) failed for BoardID: {BoardID}, Pin: {PinNumber}", nBdID, pinNum);
                return false;
            }
            else
            {
                _logger.Information("Successfully cleared output for BoardID: {BoardID}, Pin: {PinNumber}", nBdID, pinNum);
                return true;
            }
        }

        public static string ConvertToBinaryWithSpaces(uint value)
        {
            string binaryString = Convert.ToString(value, 2).PadLeft(32, '0');
            for (int i = 4; i < binaryString.Length; i += 5)
            {
                binaryString = binaryString.Insert(i, " ");
            }
            return binaryString;
        }

        public void LoadPinMapping(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath);
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
    }
}