using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Newtonsoft.Json;
using PI;
using System.IO;
using Serilog;


namespace UaaSolutionWpf.Hexapod
{

    public class PositionHexapod
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double U { get; set; }
        public double V { get; set; }
        public double W { get; set; }

        public override string ToString()
        {
            return $"X: {X}, Y: {Y}, Z: {Z}, U: {U}, V: {V}, W: {W}";
        }
    }
    public class HexapodGCS: IDisposable
    {
        private bool _disposed = false;

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
                    // Stop the timer and any other ongoing operations
                    StopRealTimePositionUpdates();
                    StopContinuousAnalogInputReading();

                    // Dispose of the timer
                    if (positionUpdateTimer != null)
                    {
                        positionUpdateTimer.Dispose();
                        positionUpdateTimer = null;
                    }

                    // Disconnect from the controller
                    Disconnect();
                }

                _disposed = true;
            }
        }

        private static SemaphoreSlim _moveSemaphore = new SemaphoreSlim(1, 1);

        private System.Timers.Timer positionUpdateTimer;
        public delegate void PositionUpdateHandler(double[] positions);
        public event PositionUpdateHandler PositionUpdated;

        public delegate void LogHandler(string message);
        public event LogHandler LogMessage;
        // Define the event
        public event EventHandler<string> MovedSuccessEvent;

        public event EventHandler<double[]> PositionRequestedAcquired;
        public event EventHandler<string> FailedToGetPivotCoordinate;
        public event EventHandler<string> FailedToSetCoordinate;
        public event EventHandler<string> SuccessfulGetPivotCoordinate;
        public event EventHandler<string> SuccessfulSetCoordinate;
        public event EventHandler<(double ch5val, double ch6val, TimeSpan elapsed)> AnalogInputValuesUpdated;


        private const int PI_RESULT_FAILURE = 0;
        private const int PI_NUMBER_OF_AXIS = 6;

        private Stopwatch analogInputStopwatch;

        public string Name;

        private readonly ILogger _logger;
        public HexapodGCS(string name, ILogger logger)
        {
            analogInputStopwatch = new Stopwatch();
            Name = name;
            _logger = logger;  
            _logger=_logger.ForContext<HexapodGCS>();
        }



        private bool _continueReadingAnalogInput;

        public enum ConnectionType
        {
            Dialog,
            Rs232,
            Tcpip,
            Usb
        }
        public static Dictionary<string, string> AxisIdentifier { get; }
            = new Dictionary<string, string>
            {
                {"", "X Y Z U V W"},
                {"C-887", "X Y Z U V W"},
                {"PivotPoint","X Y Z" }
            };
        public static readonly string Axis = AxisIdentifier["C-887"];
        public static readonly string AxisPivotPoint = AxisIdentifier["PivotPoint"];
        public int ControllerId;



        public int Connect(string ipAddress, int port)
        {
            _logger.Information("Attempting to connect to {IpAddress}:{Port}", ipAddress, port);

            ControllerId = GCS2.ConnectTCPIP(ipAddress, port);

            if (ControllerId >= 0)
            {
                _logger.Information("Connect successful to {IpAddress}:{Port}, ControllerID={ControllerId}", ipAddress, port, ControllerId);
            }
            else
            {
                _logger.Error("Failed to connect to {IpAddress}:{Port}. No hexapod available.", ipAddress, port);
            }

            return ControllerId;
        }


        /// <summary>
        /// Gets the pivot point coordinates in the volatile memory.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="z"></param>
        /// <exception cref="InvalidOperationException"></exception>
        public bool GetPivotCoordinates(out double x, out double y, out double z)
        {
            
            double[] values = new double[3];
            int success = GCS2.qSPI(ControllerId, AxisPivotPoint, values);

            if (success > 0)
            {
                x = values[0];
                y = values[1];
                z = values[2];
                string successMessage = $"Successfully got pivot coordinates: X={x}, Y={y}, Z={z}";
                _logger.Information("GetPivotCoordinates: {SuccessMessage} for ControllerId {ControllerId}", successMessage, ControllerId);
                
                SuccessfulGetPivotCoordinate?.Invoke(this, successMessage);
                return true;
            }
            else
            {
                x = y = z = 0;
                string errorMessage = $"Failed to get pivot coordinates for ControllerId {ControllerId}";
                _logger.Error("GetPivotCoordinates: {ErrorMessage}", errorMessage);
                
                FailedToGetPivotCoordinate?.Invoke(this, errorMessage);
                // throw new InvalidOperationException(errorMessage);
                return false;
            }
        }

        public void SetPivotCoordinates(double x, double y, double z)
        {
            double[] values = new double[] { x, y, z };
            int success = GCS2.SPI(ControllerId, AxisPivotPoint, values);
            if (success > 0)
            {
                string successMessage = $"Successfully set pivot coordinates to: X={x}, Y={y}, Z={z}";
                _logger.Information("SetPivotCoordinates: {SuccessMessage} for ControllerId {ControllerId}", successMessage, ControllerId);
                LogMessage?.Invoke(successMessage);
                SuccessfulSetCoordinate?.Invoke(this, successMessage);
            }
            else
            {
                string errorMessage = $"Failed to set pivot coordinates to: X={x}, Y={y}, Z={z} for ControllerId {ControllerId}";
                _logger.Error("SetPivotCoordinates: {ErrorMessage}", errorMessage);
                LogMessage?.Invoke(errorMessage);
                FailedToSetCoordinate?.Invoke(this, errorMessage);
                throw new InvalidOperationException(errorMessage);
            }
        }
        public bool IsConnected()
        {
            if (ControllerId >= 0)
            {
                if (GCS2.IsConnected(ControllerId) > 0)
                    return true;
                else return false;

            }
            else { return false; }
        }

        public void Disconnect()
        {
            if (ControllerId >= 0)
            {
                GCS2.CloseConnection(ControllerId);
                ControllerId = -1;
                LogMessage?.Invoke("Disconnected from the hexapod.");
            }
        }




        public async Task MoveToAbsoluteTarget(double[] targetPos)
        {
            await _moveSemaphore.WaitAsync();
            try
            {
                if (GCS2.MOV(ControllerId, Axis, targetPos) == PI_RESULT_FAILURE)
                {
                    //LogMessage?.Invoke("Failed to move to target position.");
                    throw new GcsCommandError("Unable to move to target position.");
                }

                string formattedPosition = $"X: {targetPos[0]:F4}, Y: {targetPos[1]:F4}, Z: {targetPos[2]:F4}, U: {targetPos[3]:F4}, V: {targetPos[4]:F4}, W: {targetPos[5]:F4}";
                //LogMessage?.Invoke($"Moved to absolute target position: {formattedPosition}");

                await WaitForMotionDone();

                // Raise the event
                MovedSuccessEvent?.Invoke(this, formattedPosition);
            }
            finally
            {
                _moveSemaphore.Release();
            }
        }


        public async Task MoveToAbsoluteTarget(PositionHexapod targetPosHex)
        {
            double[] targetPos = targetPosHex.ToDoubleArray();
            await _moveSemaphore.WaitAsync();
            try
            {
                if (GCS2.MOV(ControllerId, Axis, targetPos) == PI_RESULT_FAILURE)
                {
                    //LogMessage?.Invoke("Failed to move to target position.");
                    throw new GcsCommandError("Unable to move to target position.");
                }

                string formattedPosition = $"X: {targetPos[0]:F4}, Y: {targetPos[1]:F4}, Z: {targetPos[2]:F4}, U: {targetPos[3]:F4}, V: {targetPos[4]:F4}, W: {targetPos[5]:F4}";
                //LogMessage?.Invoke($"Moved to absolute target position: {formattedPosition}");

                await WaitForMotionDone();

                // Raise the event
                MovedSuccessEvent?.Invoke(this, formattedPosition);
            }
            finally
            {
                _moveSemaphore.Release();
            }
        }




        public async Task MoveToRelativeTarget(double[] targetPos)
        {
            await _moveSemaphore.WaitAsync();
            try
            {
                if (GCS2.MVR(ControllerId, Axis, targetPos) == PI_RESULT_FAILURE)
                {
                    //LogMessage?.Invoke("Failed to move to relative target position.");
                    //throw new GcsCommandError("Unable to move to target position.");
                }
                string formattedPosition = $"X: {targetPos[0]:F4}, Y: {targetPos[1]:F4}, Z: {targetPos[2]:F4}, U: {targetPos[3]:F4}, V: {targetPos[4]:F4}, W: {targetPos[5]:F4}";
                //LogMessage?.Invoke($"Moved to relative target position: {formattedPosition}");
                await WaitForMotionDone();
                // Raise the event
                //MovedSuccessEvent?.Invoke(this, formattedPosition);
            }
            finally
            {
                _moveSemaphore.Release();
            }
        }

        public async Task WaitForMotionDone()
        {
            var isMoving = Enumerable.Repeat(1, PI_NUMBER_OF_AXIS).ToArray();

            //LogMessage?.Invoke("Waiting for movement to end.");

            while (Array.Exists(isMoving, element => element != 0))
            {
                if (GCS2.IsMoving(ControllerId, Axis, isMoving) == PI_RESULT_FAILURE)
                {
                    
                    _logger.Error("Failed to query movement status for ControllerId {ControllerId} and Axis {Axis}", ControllerId, Axis);
                    throw new GcsCommandError("Unable to query movement status.");

                }

                await Task.Delay(50);
            }

            //LogMessage?.Invoke("Movement is finished.");

            MovedSuccessEvent?.Invoke(this, "Movement is finished.");


        }


        public double[] GetMinPositionLimit()
        {
            var minPosLimits = new double[PI_NUMBER_OF_AXIS];

            try
            {
                if (GCS2.qTMN(ControllerId, Axis, minPosLimits) == PI_RESULT_FAILURE)
                {
                    // Log the failure before throwing the exception
                    _logger.Error("Failed to get minimum position limit for ControllerId {ControllerId} and Axis {Axis}", ControllerId, Axis);
                    throw new GcsCommandError("Unable to get minimum position limit.");
                }

                // Log the successful retrieval of the minimum position limits
                _logger.Information("Successfully retrieved minimum position limits for ControllerId {ControllerId} and Axis {Axis}", ControllerId, Axis);
            }
            catch (GcsCommandError ex)
            {
                // Log the exception
                _logger.Error(ex, "An error occurred while getting minimum position limits.");
                throw; // Re-throw the exception if necessary
            }
            catch (Exception ex)
            {
                // Log any other unexpected exceptions
                _logger.Error(ex, "An unexpected error occurred while getting minimum position limits.");
                throw;
            }

            return minPosLimits;
        }

        public double[] GetMaxPositionLimit()
        {
            var maxPosLimits = new double[PI_NUMBER_OF_AXIS];

            try
            {
                if (GCS2.qTMX(ControllerId, Axis, maxPosLimits) == PI_RESULT_FAILURE)
                {
                    // Log the failure before throwing the exception
                    _logger.Error("Failed to get maximum position limit for ControllerId {ControllerId} and Axis {Axis}", ControllerId, Axis);
                    throw new GcsCommandError("Unable to get maximum position limit.");
                }

                // Log the successful retrieval of the maximum position limits
                _logger.Information("Successfully retrieved maximum position limits for ControllerId {ControllerId} and Axis {Axis}", ControllerId, Axis);
            }
            catch (GcsCommandError ex)
            {
                // Log the exception
                _logger.Error(ex, "An error occurred while getting maximum position limits.");
                throw; // Re-throw the exception if necessary
            }
            catch (Exception ex)
            {
                // Log any other unexpected exceptions
                _logger.Error(ex, "An unexpected error occurred while getting maximum position limits.");
                throw;
            }

            return maxPosLimits;
        }

        public double[] GetPosition()
        {
            var position = new double[PI_NUMBER_OF_AXIS];



            try
            {
                if (GCS2.qPOS(ControllerId, Axis, position) == PI_RESULT_FAILURE)
                {
                    // Log the failure before returning
                    _logger.Error("Failed to get the position of the hexapod for ControllerId {ControllerId} and Axis {Axis}", ControllerId, Axis);
                    // Optionally throw an exception if needed
                    // throw new GcsCommandError("Failed to get the position of the hexapod.");
                }
                else
                {
                    // Log the successful retrieval of the position
                    //_logger.Information("Successfully obtained the position of the hexapod for ControllerId {ControllerId} and Axis {Axis}", ControllerId, Axis);
                }

                // Raise the event
                PositionRequestedAcquired?.Invoke(this, position);
                //_logger.Information("PositionRequestedAcquired event invoked.");
            }
            catch (Exception ex)
            {
                // Log any unexpected exceptions
                _logger.Error(ex, "An unexpected error occurred while getting the position of the hexapod.");
                throw;
            }

            return position;
        }


        public PositionHexapod GetPositionToPositionHexapod()
        {
            var position = new double[PI_NUMBER_OF_AXIS];
            PositionHexapod returnPosition = new PositionHexapod();

            try
            {
                if (GCS2.qPOS(ControllerId, Axis, position) == PI_RESULT_FAILURE)
                {
                    // Log the failure before returning
                    _logger.Error("Failed to get the position of the hexapod for ControllerId {ControllerId} and Axis {Axis}", ControllerId, Axis);
                    // Optionally throw an exception if needed
                    // throw new GcsCommandError("Failed to get the position of the hexapod.");
                }
                else
                {
                    // Log the successful retrieval of the position
                    //_logger.Information("Successfully obtained the position of the hexapod for ControllerId {ControllerId} and Axis {Axis}", ControllerId, Axis);
                }

                // Raise the event
                PositionRequestedAcquired?.Invoke(this, position);
                //_logger.Information("PositionRequestedAcquired event invoked.");

                // Log the transformation of the position
                returnPosition = position.ToPositionHexapod();
                //_logger.Information("Position transformed to PositionHexapod.");

                // Log the final position returned
                //_logger.Information("Returning position: {@PositionHexapod}", returnPosition);
            }
            catch (Exception ex)
            {
                // Log any unexpected exceptions
                _logger.Error(ex, "An unexpected error occurred while getting the position of the hexapod.");
                throw;
            }

            return returnPosition;
        }


        public string GetDeviceIdentification()
        {
            var idnBuffer = new StringBuilder(256);

            try
            {
                if (GCS2.qIDN(ControllerId, idnBuffer, idnBuffer.Capacity) == PI_RESULT_FAILURE)
                {
                    // Log the failure case
                    _logger.Error("Failed to get device identification for ControllerId {ControllerId}", ControllerId);
                    return "Failed to get device identification.";
                }

                // Log the successful retrieval
                var deviceId = idnBuffer.ToString();
                _logger.Information("Successfully obtained device identification for ControllerId {ControllerId}: {DeviceId}", ControllerId, deviceId);

                return deviceId;
            }
            catch (Exception ex)
            {
                // Log any unexpected exceptions
                _logger.Error(ex, "An unexpected error occurred while getting the device identification for ControllerId {ControllerId}", ControllerId);
                throw;
            }
        }

        public void PrintControllerIdentification()
        {
            var ControllerIdentification = new StringBuilder(1024);

            if (GCS2.qIDN(ControllerId, ControllerIdentification, ControllerIdentification.Capacity) ==
                PI_RESULT_FAILURE)
            {
                LogMessage?.Invoke("qIDN failed.");
                //throw new GcsCommandError("qIDN failed. Exiting.");
            }

            LogMessage?.Invoke($"qIDN returned: {ControllerIdentification}");
        }


        public void GetAnalogInputValue(out double ch5val, out double ch6val)
        {
            int[] channels = new int[2];
            channels[0] = 5;
            channels[1] = 6;
            double[] values = new double[2];
            int result = GCS2.qTAV(ControllerId, channels, values, 2);
            if (result == PI_RESULT_FAILURE)
            {
                ch5val = 0;
                ch6val = 0;
            }
            else
            {
                ch5val = values[0];
                ch6val = values[1];
            }


            //return (int)result;
        }


        public Dictionary<string, PositionHexapod> predefinedHexapodPositions;

        public void LoadPredefinedPositionsHexapod()
        {
            string filePath = "PositionsHexapod.json"; // Path to your JSON file
            predefinedHexapodPositions = LoadHexapodPositions(filePath);
        }

        public Dictionary<string, PositionHexapod> LoadHexapodPositions(string filePath)
        {
            try
            {
                _logger.Information("Loading hexapod positions from {FilePath}", filePath);
                string json = File.ReadAllText(filePath);
                var positions = JsonConvert.DeserializeObject<Dictionary<string, PositionHexapod>>(json);
                _logger.Information("Successfully loaded hexapod positions from {FilePath}", filePath);
                return positions;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error loading hexapod positions from {FilePath}", filePath);
                System.Windows.MessageBox.Show(
                    $"Error loading hexapod positions: {ex.Message}",
                    "Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return new Dictionary<string, PositionHexapod>();
            }
        }

        /// <summary>
        /// Comapre current position with prededine location and return positioname
        /// </summary>
        /// <param name="tolerance">default = 0.002 mm, or 2 microns</param>
        /// <returns>return position name</returns>
        public string WhereAmI(double tolerance=0.002)
        {
            LoadPredefinedPositionsHexapod();
            var currentPosition = GetPositionToPositionHexapod();
           



            foreach (var predefinedPosition in predefinedHexapodPositions)
            {
                if (IsPositionClose(currentPosition, predefinedPosition.Value, tolerance))
                {
                    return predefinedPosition.Key;
                }
            }

            return "Unknown Position";
        }

        private bool IsPositionClose(PositionHexapod pos1, PositionHexapod pos2, double tolerance)
        {
            return Math.Abs(pos1.X - pos2.X) < tolerance &&
                   Math.Abs(pos1.Y - pos2.Y) < tolerance &&
                   Math.Abs(pos1.Z - pos2.Z) < tolerance &&
                   Math.Abs(pos1.U - pos2.U) < tolerance &&
                   Math.Abs(pos1.V - pos2.V) < tolerance &&
                   Math.Abs(pos1.W - pos2.W) < tolerance;
        }

        public async void StartContinuousAnalogInputReading()
        {
            _continueReadingAnalogInput = true;
            analogInputStopwatch.Start();

            while (_continueReadingAnalogInput)
            {
                GetAnalogInputValue(out double ch5val, out double ch6val);
                TimeSpan elapsed = analogInputStopwatch.Elapsed;
                analogInputStopwatch.Restart();

                AnalogInputValuesUpdated?.Invoke(this, (ch5val, ch6val, elapsed));

                await Task.Delay(100); // Delay for 0.1 seconds between readings
            }

            analogInputStopwatch.Stop();
        }

        public void StopContinuousAnalogInputReading()
        {
            _continueReadingAnalogInput = false;
        }

        public void StartRealTimePositionUpdates(int intervalMilliseconds)
        {
            if (positionUpdateTimer == null)
            {
                positionUpdateTimer = new System.Timers.Timer(intervalMilliseconds);
                positionUpdateTimer.Elapsed += OnPositionUpdate;
            }
            positionUpdateTimer.Start();
        }


        public void StopRealTimePositionUpdates()
        {
            if (positionUpdateTimer != null)
            {
                positionUpdateTimer.Stop();
            }
        }

        private void OnPositionUpdate(object sender, ElapsedEventArgs e)
        {
            try
            {
                double[] positions = GetPosition();
                PositionUpdated?.Invoke(positions);
            }
            catch (ObjectDisposedException)
            {
                // The timer might fire one last time during disposal, we can ignore this
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in OnPositionUpdate");
            }
        }
        public double GetSystemVelocity()
        {
            double systemVelocity = 0;

            try
            {
                if (GCS2.qVLS(ControllerId, ref systemVelocity) == PI_RESULT_FAILURE)
                {
                    // Log the failure before returning
                    _logger.Error("Failed to get the system velocity of the hexapod for ControllerId {ControllerId}", ControllerId);
                    // Optionally throw an exception if needed
                    throw new GcsCommandError("Failed to get the system velocity of the hexapod.");
                }
                else
                {
                    // Log the successful retrieval of the system velocity
                    _logger.Information("Successfully obtained the system velocity of the hexapod for ControllerId {ControllerId}: {SystemVelocity}", ControllerId, systemVelocity);
                }
            }
            catch (Exception ex)
            {
                // Log any unexpected exceptions
                _logger.Error(ex, "An unexpected error occurred while getting the system velocity of the hexapod.");
                throw;
            }

            return systemVelocity;
        }
        public double[] GetVelocity()
        {
            var velocity = new double[PI_NUMBER_OF_AXIS];

            try
            {
                if (GCS2.qVEL(ControllerId, Axis, velocity) == PI_RESULT_FAILURE)
                {
                    // Log the failure before returning
                    _logger.Error("Failed to get the velocity of the hexapod for ControllerId {ControllerId} and Axis {Axis}", ControllerId, Axis);
                    // Optionally throw an exception if needed
                    throw new GcsCommandError("Failed to get the velocity of the hexapod.");
                }
                else
                {
                    // Log the successful retrieval of the velocity
                    _logger.Information("Successfully obtained the velocity of the hexapod for ControllerId {ControllerId} and Axis {Axis}", ControllerId, Axis);
                }
            }
            catch (Exception ex)
            {
                // Log any unexpected exceptions
                _logger.Error(ex, "An unexpected error occurred while getting the velocity of the hexapod.");
                throw;
            }

            return velocity;
        }
        public string GetConfiguredAxes()
        {
            const int bufferSize = 256;
            var szAxes = new StringBuilder(bufferSize);

            try
            {
                if (GCS2.qSAI(ControllerId, szAxes, bufferSize) == PI_RESULT_FAILURE)
                {
                    // Log the failure before returning
                    _logger.Error("Failed to get the configured axes for ControllerId {ControllerId}", ControllerId);
                    // Optionally throw an exception if needed
                    throw new GcsCommandError("Failed to get the configured axes.");
                }
                else
                {
                    // Log the successful retrieval of the configured axes
                    var axes = szAxes.ToString();
                    _logger.Information("Successfully obtained the configured axes for ControllerId {ControllerId}: {Axes}", ControllerId, axes);
                    return axes;
                }
            }
            catch (Exception ex)
            {
                // Log any unexpected exceptions
                _logger.Error(ex, "An unexpected error occurred while getting the configured axes.");
                throw;
            }
        }
        public void StopMotion()
        {
            try
            {
                // Stop all axes immediately
                if (GCS2.STP(ControllerId) == PI_RESULT_FAILURE)
                {
                    _logger.Error("Failed to stop motion for ControllerId {ControllerId}", ControllerId);
                    throw new GcsCommandError("Failed to stop motion.");
                }

                _logger.Information("Successfully stopped motion for ControllerId {ControllerId}", ControllerId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error occurred while stopping motion for ControllerId {ControllerId}", ControllerId);
                throw;
            }
        }

        // Additional method for a softer stop if needed
        public void HaltMotion()
        {
            try
            {
                // Halt motion (softer stop than STP)
                if (GCS2.HLT(ControllerId, Axis) == PI_RESULT_FAILURE)
                {
                    _logger.Error("Failed to halt motion for ControllerId {ControllerId}", ControllerId);
                    throw new GcsCommandError("Failed to halt motion.");
                }

                _logger.Information("Successfully halted motion for ControllerId {ControllerId}", ControllerId);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error occurred while halting motion for ControllerId {ControllerId}", ControllerId);
                throw;
            }
        }


        // Add these methods to your HexapodGCS class

        public async Task MoveRelative(int axis, double distance)
        {
            await _moveSemaphore.WaitAsync();
            try
            {
                // Create array for relative movement
                double[] relativeMove = new double[PI_NUMBER_OF_AXIS];
                relativeMove[axis] = distance;  // Set the distance for the specified axis

                _logger.Debug("Initiating relative move - Axis: {Axis}, Distance: {Distance}", axis, distance);

                if (GCS2.MVR(ControllerId, Axis, relativeMove) == PI_RESULT_FAILURE)
                {
                    string errorMessage = $"Failed to execute relative move on axis {axis} by {distance}";
                    _logger.Error(errorMessage);
                    throw new GcsCommandError(errorMessage);
                }

                await WaitForMotionDone();

                string successMessage = $"Completed relative move - Axis: {axis}, Distance: {distance}";
                _logger.Information(successMessage);
                MovedSuccessEvent?.Invoke(this, successMessage);
            }
            finally
            {
                _moveSemaphore.Release();
            }
        }

        // Overload for moving multiple axes at once
        public async Task MoveRelative(double[] relativeDistances)
        {
            if (relativeDistances == null || relativeDistances.Length != PI_NUMBER_OF_AXIS)
            {
                throw new ArgumentException($"Relative distances array must have length {PI_NUMBER_OF_AXIS}");
            }

            await _moveSemaphore.WaitAsync();
            try
            {
                _logger.Debug("Initiating multi-axis relative move: X:{0}, Y:{1}, Z:{2}, U:{3}, V:{4}, W:{5}",
                    relativeDistances[0], relativeDistances[1], relativeDistances[2],
                    relativeDistances[3], relativeDistances[4], relativeDistances[5]);

                if (GCS2.MVR(ControllerId, Axis, relativeDistances) == PI_RESULT_FAILURE)
                {
                    string errorMessage = "Failed to execute multi-axis relative move";
                    _logger.Error(errorMessage);
                    throw new GcsCommandError(errorMessage);
                }

                await WaitForMotionDone();

                string successMessage = $"Completed multi-axis relative move";
                _logger.Information(successMessage);
                MovedSuccessEvent?.Invoke(this, successMessage);
            }
            finally
            {
                _moveSemaphore.Release();
            }
        }

        // Helper method for single axis relative movement using PositionHexapod
        public async Task MoveRelativeAxis(string axis, double distance)
        {
            int axisIndex = GetAxisIndex(axis);
            if (axisIndex == -1)
            {
                throw new ArgumentException($"Invalid axis name: {axis}");
            }

            await MoveRelative(axisIndex, distance);
        }

        private int GetAxisIndex(string axis)
        {
            return axis.ToUpper() switch
            {
                "X" => 0,
                "Y" => 1,
                "Z" => 2,
                "U" => 3,
                "V" => 4,
                "W" => 5,
                _ => -1
            };
        }

        internal class GcsCommandError : Exception
        {
            public GcsCommandError(string message)
                : base(message)
            {
            }
        }
    }

    public static class PositionHexapodExtensions
    {
        public static double[] ToDoubleArray(this PositionHexapod targetPosition)
        {
            return new double[]
            {
            targetPosition.X,
            targetPosition.Y,
            targetPosition.Z,
            targetPosition.U,
            targetPosition.V,
            targetPosition.W
            };
        }
        public static PositionHexapod ToPositionHexapod(this double[] array)
        {
            if (array == null || array.Length != 6)
            {
                throw new ArgumentException("Array must be of length 6.", nameof(array));
            }

            return new PositionHexapod
            {
                X = array[0],
                Y = array[1],
                Z = array[2],
                U = array[3],
                V = array[4],
                W = array[5]
            };
        }



    }
    public class PivotPoint
    {
        public string Name { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }
}
