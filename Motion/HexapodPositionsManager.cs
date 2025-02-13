using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Serilog;
using UaaSolutionWpf.Motion;
using UaaSolutionWpf.Services;
using UaaSolutionWpf.Hexapod;
using System.Text;

namespace UaaSolutionWpf.Motion
{
    public class HexapodPositionsManager
    {
        private readonly StackPanel targetPanel;
        private WorkingPositions workingPositions;
        private HashSet<string> allowedPositions;
        private Dictionary<string, string> buttonLabels;
        private readonly ILogger _logger;
        private readonly MotionGraphManager _motionGraphManager;
        private readonly PositionRegistry _positionRegistry;
        private readonly HexapodConnectionManager _hexapodConnectionManager;
        private readonly int hexapodId;
        private readonly string deviceId;

        public bool isSafetyMode=false;
        // Default allowed positions
        private static readonly HashSet<string> DefaultAllowedPositions = new HashSet<string>
        {
            "Home",
            "LensGrip",
            "ApproachLensGrip",
            "LensPlace",
            "ApproachLensPlace",
            "AvoidDispenser",
            "RejectLens",
            "ParkInside"
        };

        // Default button labels with emojis
        private static readonly Dictionary<string, string> DefaultButtonLabels = new Dictionary<string, string>
        {
            { "Home", "🏠 Home" },
            { "LensGrip", "🔍 Lens Grip" },
            { "ApproachLensGrip", "↗️ Approach Grip" },
            { "LensPlace", "📍 Lens Place" },
            { "LensPlaceCalculated", "🎯 Calculated Place" },
            { "ApproachLensPlace", "↗️ Approach Place" },
            { "LeftSeeProbe", "👁️ Left Probe" },
            { "RightSeeProbe", "👁️ Right Probe" },
            { "AvoidDispenser", "⚠️ Avoid Dispenser" },
            { "RejectLens", "❌ Reject Lens" },
            { "ParkInside", "🅿️ Park Inside" }
        };

        public HexapodPositionsManager(
            StackPanel panel,
            int hexapodId,
            HexapodConnectionManager hexapodConnectionManager,
            IEnumerable<string> positionsToShow = null,
            Dictionary<string, string> customButtonLabels = null,
            MotionGraphManager motionGraphManager = null,
            PositionRegistry positionRegistry = null,
            ILogger logger = null)
        {
            targetPanel = panel ?? throw new ArgumentNullException(nameof(panel));
            this.hexapodId = hexapodId;
            _hexapodConnectionManager = hexapodConnectionManager ?? throw new ArgumentNullException(nameof(hexapodConnectionManager));
            _motionGraphManager = motionGraphManager ?? throw new ArgumentNullException(nameof(motionGraphManager));
            _positionRegistry = positionRegistry ?? throw new ArgumentNullException(nameof(positionRegistry));
            _logger = logger?.ForContext<HexapodPositionsManager>() ?? Log.Logger;

            allowedPositions = positionsToShow != null
                ? new HashSet<string>(positionsToShow)
                : new HashSet<string>(DefaultAllowedPositions);

            buttonLabels = customButtonLabels ?? DefaultButtonLabels;

            // Set device ID based on hexapod ID
            deviceId = GetDeviceId(hexapodId);
        }

        private string GetDeviceId(int hexapodId)
        {
            return hexapodId switch
            {
                0 => "hex-left",
                1 => "hex-bottom",
                2 => "hex-right",
                _ => throw new ArgumentException($"Invalid hexapod ID: {hexapodId}")
            };
        }

        private string GetHexapodName(int id)
        {
            return id switch
            {
                0 => "Left",
                1 => "Bottom",
                2 => "Right",
                _ => $"Unknown ({id})"
            };
        }

        public void LoadPositionsAndCreateButtons(string jsonFilePath)
        {
            try
            {
                string jsonContent = File.ReadAllText(jsonFilePath);
                workingPositions = JsonSerializer.Deserialize<WorkingPositions>(jsonContent);
                CreateHexapodPositionButtons();
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

        private async void OnPositionButtonClick(string positionName)
        {
            try
            {
                _logger.Information("Analyzing movement to position {PositionName} for {HexapodName} Hexapod",
                    positionName, GetHexapodName(hexapodId));

                var pathAnalysis = await _motionGraphManager.AnalyzeMovementPath(deviceId, positionName);

                if (!pathAnalysis.IsValid)
                {
                    MessageBox.Show(
                        $"Cannot move to position {positionName}: {pathAnalysis.Error}",
                        "Movement Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                // Build message including initial move if needed
                var message = new StringBuilder();
                message.AppendLine($"Moving {GetHexapodName(hexapodId)} Hexapod to: {positionName}");

                if (pathAnalysis.RequiresInitialMove)
                {
                    message.AppendLine($"\nInitial move required:");
                    message.AppendLine($"Current Position: X={pathAnalysis.InitialPosition.X:F3}, Y={pathAnalysis.InitialPosition.Y:F3}, Z={pathAnalysis.InitialPosition.Z:F3}");
                    message.AppendLine($"Moving to nearest known position: {pathAnalysis.CurrentPosition}");
                    message.AppendLine($"Distance: {pathAnalysis.InitialMoveDistance:F3}mm");
                    message.AppendLine();
                }

                message.AppendLine($"Then following path:");
                message.AppendLine($"Path: {string.Join(" → ", pathAnalysis.Path)}");
                message.AppendLine($"Number of steps: {pathAnalysis.NumberOfSteps}");

                MessageBoxResult result;
                if (isSafetyMode)
                {
                    result = MessageBox.Show(
                    message.ToString(),
                    "Confirm Movement",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                    if (result == MessageBoxResult.No)
                    {
                        Log.Information("User confimr no movement.");
                        return;
                    }

                }
                else
                {
                    result = MessageBoxResult.Yes;
                }

                

                if (result == MessageBoxResult.Yes)
                {
                    // Execute the movement sequence
                    if (pathAnalysis.RequiresInitialMove)
                    {
                        _logger.Information("Executing initial move to nearest known position {Position}",
                            pathAnalysis.CurrentPosition);

                        // Get the position coordinates
                        Position targetPos;
                        if (hexapodId >= 0 && hexapodId <= 2)  // Hexapod
                        {
                            if (!_positionRegistry.TryGetHexapodPosition(hexapodId, pathAnalysis.CurrentPosition, out targetPos))
                            {
                                throw new InvalidOperationException($"Could not find coordinates for position {pathAnalysis.CurrentPosition}");
                            }
                        }
                        else
                        {
                            throw new InvalidOperationException($"Invalid hexapod ID: {hexapodId}");
                        }

                        // Execute the initial move
                        var controller = _hexapodConnectionManager.GetHexapodController(GetHexapodType(hexapodId));
                        if (controller != null)
                        {
                            await controller.MoveToAbsoluteTarget(new double[]
                            {
                        targetPos.X, targetPos.Y, targetPos.Z,
                        targetPos.U, targetPos.V, targetPos.W
                            });
                            _logger.Information("Completed initial move to {Position}", pathAnalysis.CurrentPosition);
                        }
                    }

                    // Execute the path moves
                    _logger.Information(
                        "Executing movement of {HexapodName} Hexapod to {PositionName} via path: {Path}",
                        GetHexapodName(hexapodId),
                        positionName,
                        string.Join(" → ", pathAnalysis.Path));

                    // TODO: Execute the remaining path movements
                    foreach (var position in pathAnalysis.Path.Skip(1)) // Skip first position as we're already there
                    {
                        // Similar movement code for each position in the path
                        _logger.Information("Moving to {Position} in path", position);

                        // Get position coordinates and execute move
                        if (_positionRegistry.TryGetHexapodPosition(hexapodId, position, out var pathPos))
                        {
                            var controller = _hexapodConnectionManager.GetHexapodController(GetHexapodType(hexapodId));
                            if (controller != null)
                            {
                                await controller.MoveToAbsoluteTarget(new double[]
                                {
                            pathPos.X, pathPos.Y, pathPos.Z,
                            pathPos.U, pathPos.V, pathPos.W
                                });
                                _logger.Information("Completed move to {Position}", position);
                            }
                        }
                    }

                    _logger.Information("Movement sequence completed successfully");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error processing movement to position {PositionName} for {HexapodName} Hexapod",
                    positionName, GetHexapodName(hexapodId));
                MessageBox.Show(
                    $"Error during movement: {ex.Message}",
                    "Movement Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        private void CreateHexapodPositionButtons()
        {
            if (workingPositions?.Hexapods == null || workingPositions.Hexapods.Count == 0)
            {
                _logger.Warning("No hexapod positions found in the configuration");
                MessageBox.Show(
                    "No hexapod positions found.",
                    "Information",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var hexapod = workingPositions.Hexapods.Find(h => h.HexapodId == hexapodId);
            if (hexapod == null)
            {
                _logger.Error("No positions found for Hexapod ID {HexapodId}", hexapodId);
                MessageBox.Show(
                    $"No positions found for {GetHexapodName(hexapodId)} Hexapod",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            var wrapPanel = new WrapPanel
            {
                Margin = new Thickness(5),
                HorizontalAlignment = HorizontalAlignment.Left
            };

            foreach (var position in hexapod.Positions.Where(p => allowedPositions.Contains(p.Key)))
            {
                CreatePositionButton(position.Key, wrapPanel);
            }

            targetPanel.Children.Add(wrapPanel);
        }

        private void CreatePositionButton(string positionName, WrapPanel panel)
        {
            var button = new Button
            {
                Content = buttonLabels.TryGetValue(positionName, out string label) ? label : positionName,
                Margin = new Thickness(5),
                Padding = new Thickness(10, 5, 10, 5),
                MinWidth = 100,
                Background = new SolidColorBrush(Colors.LightGray)
            };

            button.Click += (sender, e) => OnPositionButtonClick(positionName);
            panel.Children.Add(button);

            _logger.Debug("Created button for position {PositionName} on {HexapodName} Hexapod",
                positionName, GetHexapodName(hexapodId));
        }
        private HexapodConnectionManager.HexapodType GetHexapodType(int hexapodId)
        {
            return hexapodId switch
            {
                0 => HexapodConnectionManager.HexapodType.Left,
                1 => HexapodConnectionManager.HexapodType.Bottom,
                2 => HexapodConnectionManager.HexapodType.Right,
                _ => throw new ArgumentException($"Invalid hexapod ID: {hexapodId}")
            };
        }
        private async Task ExecuteMovement(PathAnalysis pathAnalysis)
        {
            try
            {
                var hexapodController = _hexapodConnectionManager.GetHexapodController(GetHexapodType(hexapodId));
                if (hexapodController == null)
                {
                    throw new InvalidOperationException($"No controller found for {GetHexapodName(hexapodId)} Hexapod");
                }

                // If we need an initial move to get to a known position
                if (pathAnalysis.RequiresInitialMove)
                {
                    _logger.Information(
                        "Executing initial move to {Position}, distance: {Distance:F3}mm",
                        pathAnalysis.CurrentPosition,
                        pathAnalysis.InitialMoveDistance);

                    // Get the target position coordinates
                    if (!_positionRegistry.TryGetHexapodPosition(hexapodId, pathAnalysis.CurrentPosition, out var initialPosition))
                    {
                        throw new InvalidOperationException($"Could not find coordinates for position {pathAnalysis.CurrentPosition}");
                    }

                    // Execute the initial move
                    await MoveToAbsolutePosition(hexapodController, initialPosition);

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
                    if (!_positionRegistry.TryGetHexapodPosition(hexapodId, nextNode, out var targetPosition))
                    {
                        throw new InvalidOperationException($"Could not find coordinates for position {nextNode}");
                    }

                    // Execute the move
                    await MoveToAbsolutePosition(hexapodController, targetPosition);

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

        private async Task MoveToAbsolutePosition(HexapodGCS controller, Position position)
        {
            try
            {
                if (!controller.IsConnected())
                {
                    throw new InvalidOperationException($"{GetHexapodName(hexapodId)} Hexapod is not connected");
                }

                // Convert position to array for hexapod
                double[] targetPos = new double[]
                {
                    position.X,
                    position.Y,
                    position.Z,
                    position.U,
                    position.V,
                    position.W
                };

                // Execute the move and wait for completion
                await controller.MoveToAbsoluteTarget(targetPos);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error moving {HexapodName} Hexapod to position", GetHexapodName(hexapodId));
                throw;
            }
        }
    }
}