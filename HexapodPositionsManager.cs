using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace UaaSolutionWpf
{
    public class HexapodPositionsManager
    {
        private readonly StackPanel targetPanel;
        private WorkingPositions workingPositions;
        private HashSet<string> allowedPositions;
        private Dictionary<string, string> buttonLabels;
        private int hexapodId;

        // List of position names that should be displayed by default
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

        // Default button label mappings with emojis
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

        public HexapodPositionsManager(StackPanel panel, int hexapodId, IEnumerable<string> positionsToShow = null, Dictionary<string, string> customButtonLabels = null)
        {
            targetPanel = panel;
            this.hexapodId = hexapodId;
            allowedPositions = positionsToShow != null
                ? new HashSet<string>(positionsToShow)
                : new HashSet<string>(DefaultAllowedPositions);
            buttonLabels = customButtonLabels != null
                ? new Dictionary<string, string>(customButtonLabels)
                : new Dictionary<string, string>(DefaultButtonLabels);
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
                MessageBox.Show($"Error loading positions: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CreateHexapodPositionButtons()
        {
            if (workingPositions?.Hexapods == null || workingPositions.Hexapods.Count == 0)
            {
                MessageBox.Show("No hexapod positions found in the file.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var hexapod = workingPositions.Hexapods.Find(h => h.HexapodId == hexapodId);
            if (hexapod == null)
            {
                MessageBox.Show($"No positions found for Hexapod ID {hexapodId}", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Create a WrapPanel to hold the buttons
            WrapPanel wrapPanel = new WrapPanel
            {
                Margin = new Thickness(5),
                HorizontalAlignment = HorizontalAlignment.Left
            };

            foreach (var position in hexapod.Positions.Where(p => allowedPositions.Contains(p.Key)))
            {
                Button positionButton = new Button
                {
                    Content = buttonLabels.TryGetValue(position.Key, out string label) ? label : position.Key,
                    Margin = new Thickness(5),
                    Padding = new Thickness(10, 5, 10, 5),
                    MinWidth = 100,
                    Background = new SolidColorBrush(Colors.LightGray)
                };

                // Add click handler
                positionButton.Click += (sender, e) => OnPositionButtonClick(hexapod.HexapodId, position.Key, position.Value);
                wrapPanel.Children.Add(positionButton);
            }

            targetPanel.Children.Add(wrapPanel);
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

        private void OnPositionButtonClick(int hexapodId, string positionName, Position position)
        {
            // TODO: Implement the actual movement logic here
            MessageBox.Show($"Moving {GetHexapodName(hexapodId)} Hexapod to position {positionName}\n" +
                          $"X: {position.X:F4}\n" +
                          $"Y: {position.Y:F4}\n" +
                          $"Z: {position.Z:F4}\n" +
                          $"U: {position.U:F4}\n" +
                          $"V: {position.V:F4}\n" +
                          $"W: {position.W:F4}");
        }
    }
}