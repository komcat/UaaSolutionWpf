using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using UaaSolutionWpf.Gantry;
using UaaSolutionWpf.Services;

namespace UaaSolutionWpf.Motion
{
    public class Position
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double U { get; set; }
        public double V { get; set; }
        public double W { get; set; }
    }

    public class GantryData
    {
        public int GantryId { get; set; }
        public Dictionary<string, Position> Positions { get; set; }
    }

    public class HexapodData
    {
        public int HexapodId { get; set; }
        public Dictionary<string, Position> Positions { get; set; }
    }

    public class WorkingPositions
    {
        public List<HexapodData> Hexapods { get; set; }
        public List<GantryData> Gantries { get; set; }
    }

    public class GantryPositionsManager
    {
        private readonly StackPanel targetPanel;
        private WorkingPositions workingPositions;
        private HashSet<string> allowedPositions;
        private Dictionary<string, string> buttonLabels;
        private readonly ILogger _logger;
        private readonly MotionGraphManager _motionGraphManager;
        private readonly PositionRegistry _positionRegistry;
        private readonly AcsGantryConnectionManager _gantryConnectionManager;
        private const string DEVICE_ID = "gantry-main";

        public bool isSafetyMode=false;

        // Default positions to show
        private static readonly HashSet<string> DefaultAllowedPositions = new HashSet<string>
        {
            "Home", "Dispense1", "Dispense2", "PreDispense", "UV",
            "SeeCollimateLens", "SeeFocusLens", "SeeSLED", "SeePIC", "CamSeeNumber"
        };

        // Default button labels with emojis
        private static readonly Dictionary<string, string> DefaultButtonLabels = new Dictionary<string, string>
        {
            { "Home", "🏠 Home" },
            { "UV", "💡 UV" },
            { "Dispense1", "💧 Dispense 1" },
            { "Dispense2", "💧 Dispense 2" },
            { "PreDispense", "⏳ Pre-Dispense" },
            { "SeeSLED", "👁️ SLED" },
            { "SeePIC", "👁️ PIC" },
            { "SeeFocusLens", "🔍 Focus Lens" },
            { "SeeCollimateLens", "🔍 Collimate Lens" },
            {"SeeGripCollLens","👁️ Grip Coll Lens" },
            {"SeeGripFocusLens","👁️ Grip Focus Lens" },
            { "CamSeeNumber", "🔢 See Number" }
        };

        public GantryPositionsManager(
            StackPanel panel,
            AcsGantryConnectionManager gantryConnectionManager,
            IEnumerable<string> positionsToShow = null,
            Dictionary<string, string> customButtonLabels = null,
            MotionGraphManager motionGraphManager = null,
            PositionRegistry positionRegistry = null,
            ILogger logger = null)
        {
            targetPanel = panel ?? throw new ArgumentNullException(nameof(panel));
            _gantryConnectionManager= gantryConnectionManager ?? throw new ArgumentNullException(nameof(gantryConnectionManager));
            _motionGraphManager = motionGraphManager ?? throw new ArgumentNullException(nameof(motionGraphManager));
            _positionRegistry = positionRegistry ?? throw new ArgumentNullException(nameof(positionRegistry));
            _logger = logger?.ForContext<GantryPositionsManager>() ?? Log.Logger;

            allowedPositions = positionsToShow != null
                ? new HashSet<string>(positionsToShow)
                : new HashSet<string>(DefaultAllowedPositions);

            buttonLabels = customButtonLabels ?? DefaultButtonLabels;
        }

        public void LoadPositionsAndCreateButtons(string jsonFilePath)
        {
            try
            {
                string jsonContent = File.ReadAllText(jsonFilePath);
                workingPositions = JsonSerializer.Deserialize<WorkingPositions>(jsonContent);
                CreateGantryPositionButtons();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load positions from {FilePath}", jsonFilePath);
                MessageBox.Show(
                    $"Error loading positions: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        // Modify the existing OnPositionButtonClick method
        private async void OnPositionButtonClick(int gantryId, string positionName)
        {
            try
            {
                _logger.Information("Analyzing movement to position {PositionName}", positionName);

                var pathAnalysis = await _motionGraphManager.AnalyzeMovementPath(DEVICE_ID, positionName);

                if (!pathAnalysis.IsValid)
                {
                    MessageBox.Show(
                        $"Cannot move to position {positionName}: {pathAnalysis.Error}",
                        "Movement Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                // Build message showing movement details
                var message = $"Moving to: {positionName}\n" +
                             $"Current Position: {pathAnalysis.CurrentPosition}\n" +
                             $"Path: {string.Join(" → ", pathAnalysis.Path)}\n" +
                             $"Number of steps: {pathAnalysis.NumberOfSteps}";

                if (pathAnalysis.RequiresInitialMove)
                {
                    message += $"\n\nInitial move required: {pathAnalysis.InitialMoveDistance:F3}mm";
                }


                if (isSafetyMode)
                {
                    // Ask for confirmation
                    var result = MessageBox.Show(
                        message,
                        "Confirm Movement",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        await ExecuteMovement(pathAnalysis);
                    }
                }
                else
                {
                    await ExecuteMovement(pathAnalysis);

                }

                
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error processing movement to position {PositionName}", positionName);
                MessageBox.Show(
                    $"Error during movement: {ex.Message}",
                    "Movement Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        private void CreateGantryPositionButtons()
        {
            if (workingPositions?.Gantries == null || workingPositions.Gantries.Count == 0)
            {
                _logger.Warning("No gantry positions found in the configuration");
                MessageBox.Show(
                    "No gantry positions found.",
                    "Information",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var wrapPanel = new WrapPanel
            {
                Margin = new Thickness(5),
                HorizontalAlignment = HorizontalAlignment.Left
            };

            foreach (var gantry in workingPositions.Gantries)
            {
                foreach (var position in gantry.Positions.Where(p => allowedPositions.Contains(p.Key)))
                {
                    CreatePositionButton(position.Key, gantry.GantryId, wrapPanel);
                }
            }

            targetPanel.Children.Add(wrapPanel);
        }

        private void CreatePositionButton(string positionName, int gantryId, WrapPanel panel)
        {
            var button = new Button
            {
                Content = buttonLabels.TryGetValue(positionName, out string label) ? label : positionName,
                Margin = new Thickness(5),
                Padding = new Thickness(10, 5, 10, 5),
                MinWidth = 100,
                Background = new SolidColorBrush(Colors.LightGray)
            };

            button.Click += (sender, e) => OnPositionButtonClick(gantryId, positionName);
            panel.Children.Add(button);

            _logger.Debug("Created button for position {PositionName}", positionName);
        }

        // Add these new methods to GantryPositionsManager class

        private async Task ExecuteMovement(PathAnalysis pathAnalysis)
        {
            try
            {
                // If we need an initial move to get to a known position
                if (pathAnalysis.RequiresInitialMove)
                {
                    _logger.Information(
                        "Executing initial move to {Position}, distance: {Distance:F3}mm",
                        pathAnalysis.CurrentPosition,
                        pathAnalysis.InitialMoveDistance);

                    // Get the target position coordinates
                    if (!_positionRegistry.TryGetGantryPosition(4, pathAnalysis.CurrentPosition, out var initialPosition))
                    {
                        throw new InvalidOperationException($"Could not find coordinates for position {pathAnalysis.CurrentPosition}");
                    }

                    // Execute the initial move
                    await MoveToAbsolutePosition(initialPosition);

                    // Wait briefly to ensure position is stable
                    await Task.Delay(500);
                }

                // Now execute the path movements
                for (int i = 0; i < pathAnalysis.Path.Count - 1; i++)
                {
                    string currentNode = pathAnalysis.Path[i];
                    string nextNode = pathAnalysis.Path[i + 1];

                    _logger.Information("Moving from {From} to {To}", currentNode, nextNode);

                    // Get the target position coordinates
                    if (!_positionRegistry.TryGetGantryPosition(4, nextNode, out var targetPosition))
                    {
                        throw new InvalidOperationException($"Could not find coordinates for position {nextNode}");
                    }

                    // Execute the move
                    await MoveToAbsolutePosition(targetPosition);

                    // Wait briefly between moves
                    if (i < pathAnalysis.Path.Count - 2)
                    {
                        await Task.Delay(500);
                    }
                }

                _logger.Information("Movement sequence completed successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error executing movement sequence");
                throw;
            }
        }

        private async Task MoveToAbsolutePosition(Position position)
        {
            // Convert the position to axis values
            double[] axisValues = new double[]
            {
        position.X,
        position.Y,
        position.Z,
        position.U,
        position.V,
        position.W
            };

            try
            {
                // Start all axis movements simultaneously
                var moveOperations = new List<Task>();
                for (int axis = 0; axis < 3; axis++)
                {
                    moveOperations.Add(_gantryConnectionManager.MoveToAbsolutePositionAsync(axis, axisValues[axis]));
                }

                // Wait for all move commands to be initiated
                await Task.WhenAll(moveOperations);

                // Now wait for all axes to complete their motion
                await _gantryConnectionManager.WaitForAllAxesIdleAsync();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during multi-axis move to position X:{X}, Y:{Y}, Z:{Z}",
                    position.X, position.Y, position.Z);
                throw;
            }
        }
        private async Task WaitForAxisMotionComplete(int axis)
        {
            try
            {
                const int timeout = 30000; // 30 second timeout
                const int pollInterval = 100; // Check every 100ms
                int elapsed = 0;

                var controller = _gantryConnectionManager.GetController();

                while (elapsed < timeout)
                {
                    // Get the current status of the axis
                    var (_, _, isMoving) = controller.GetAxisStatus(axis);

                    if (!isMoving)
                    {
                        _logger.Debug("Axis {Axis} motion completed", axis);
                        return;
                    }

                    await Task.Delay(pollInterval);
                    elapsed += pollInterval;
                }

                throw new TimeoutException($"Timeout waiting for axis {axis} motion to complete");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error waiting for axis {Axis} motion to complete", axis);
                throw;
            }
        }
    }
}