using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace UaaSolutionWpf
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

        // List of position names that should be displayed
        private static readonly HashSet<string> DefaultAllowedPositions = new HashSet<string>
        {
            "Home",
            "Dispense1",
            "Dispense2",
            "PreDispense",
            "MidFront",
            "MidCenter",
            "MidBack",
            "SeeSLED",
            "SeePIC",
            "CamSeeNumber"
        };

        // Default button label mappings
        private static readonly Dictionary<string, string> DefaultButtonLabels = new Dictionary<string, string>
        {
            // Common Positions
            { "Home", "🏠 Home" },
            { "UV", "💡 UV" },
            
            // Dispense Related
            { "Dispense1", "💧 Dispense 1" },
            { "Dispense1Safe", "🛡️ Dispense 1 Safe" },
            { "Dispense2", "💧 Dispense 2" },
            { "Dispense2Safe", "🛡️ Dispense 2 Safe" },
            { "PreDispense", "⏳ Pre-Dispense" },
            
            // Movement Positions
            { "MidFront", "⬆️ Front" },
            { "MidCenter", "⚡ Center" },
            { "MidBack", "⬇️ Back" },
            
            // Vision Positions
            { "SeeSLED", "👁️ SLED" },
            { "SeePIC", "👁️ PIC" },
            { "SeeFocusLens", "🔍 Focus Lens" },
            { "SeeCollimateLens", "🔍 Collimate Lens" },
            
            // Grip Positions
            { "GripLeftLens", "👆 Grip Left" },
            { "GripRightLens", "👆 Grip Right" },
            
            // Fiducial Positions
            { "Fiducial1", "🎯 Fiducial 1" },
            { "Fiducial2", "🎯 Fiducial 2" },
            { "Fiducial3", "🎯 Fiducial 3" },
            
            // Pattern Related
            { "SledPattern", "📋 SLED Pattern" },
            { "PicPattern", "📋 PIC Pattern" },
            
            // Dispensing Fiducials
            { "DispFid1", "📍 Disp Fid 1" },
            { "DispFid2", "📍 Disp Fid 2" },
            { "DispFid3", "📍 Disp Fid 3" },
            
            // Dispensing Locations
            { "DispLoc1", "📌 Disp Loc 1" },
            { "DispLoc2", "📌 Disp Loc 2" },
            
            // Calibration and Camera Positions
            { "CalDot", "⚪ Cal Dot" },
            { "CamSeeDot", "📸 Cam See Dot" },
            { "CamDispense1", "📸 Cam Dispense 1" },
            { "CamDispense2", "📸 Cam Dispense 2" },
            { "CamSeeNumber", "🔢 Cam See Number" },
            { "SNViewing", "👀 SN Viewing" }
        };

        public GantryPositionsManager(StackPanel panel, IEnumerable<string> positionsToShow = null, Dictionary<string, string> customButtonLabels = null)
        {
            targetPanel = panel;
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
                CreateGantryPositionButtons();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading positions: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CreateGantryPositionButtons()
        {
            if (workingPositions?.Gantries == null || workingPositions.Gantries.Count == 0)
            {
                MessageBox.Show("No gantry positions found in the file.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Create a WrapPanel to hold the buttons
            WrapPanel wrapPanel = new WrapPanel
            {
                Margin = new Thickness(5),
                HorizontalAlignment = HorizontalAlignment.Left
            };

            foreach (var gantry in workingPositions.Gantries)
            {
                foreach (var position in gantry.Positions.Where(p => allowedPositions.Contains(p.Key)))
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
                    positionButton.Click += (sender, e) => OnPositionButtonClick(gantry.GantryId, position.Key, position.Value);
                    wrapPanel.Children.Add(positionButton);
                }
            }

            targetPanel.Children.Add(wrapPanel);
        }

        private void OnPositionButtonClick(int gantryId, string positionName, Position position)
        {
            // TODO: Implement the actual movement logic here
            MessageBox.Show($"Moving Gantry {gantryId} to position {positionName}\n" +
                          $"X: {position.X:F4}\n" +
                          $"Y: {position.Y:F4}\n" +
                          $"Z: {position.Z:F4}\n" +
                          $"U: {position.U:F4}\n" +
                          $"V: {position.V:F4}\n" +
                          $"W: {position.W:F4}");
        }
    }
}