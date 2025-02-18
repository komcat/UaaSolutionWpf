using System.Windows;
using System.Windows.Input;
using UaaSolutionWpf.Gantry;
using UaaSolutionWpf.Services;
using UaaSolutionWpf.Controls;
using Serilog;
using System.Windows.Controls;
using System;

namespace UaaSolutionWpf.Motion
{
    public class EmergencyStopManager : IDisposable
    {
        private readonly System.Windows.Window _mainWindow;
        private readonly AcsGantryConnectionManager _gantryManager;
        private readonly GantryMovementService _movementService;
        private readonly ILogger _logger;
        private EmergencyStopWindow _stopWindow;

        public EmergencyStopManager(
            System.Windows.Window mainWindow,
            AcsGantryConnectionManager gantryManager,
            GantryMovementService movementService,
            ILogger logger)
        {
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
            _gantryManager = gantryManager ?? throw new ArgumentNullException(nameof(gantryManager));
            _movementService = movementService ?? throw new ArgumentNullException(nameof(movementService));
            _logger = logger.ForContext<EmergencyStopManager>();

            Initialize();
        }

        private void Initialize()
        {
            // Create the emergency stop window
            _stopWindow = new EmergencyStopWindow(_gantryManager, _movementService);
            _stopWindow.Show();

            // Hook up the key event handler to the main window
            _mainWindow.PreviewKeyDown += MainWindow_PreviewKeyDown;

            _logger.Information("Emergency stop manager initialized");
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                _logger.Information("Escape key pressed - triggering emergency stop");
                TriggerEmergencyStop();
                e.Handled = true;
            }
        }

        private void TriggerEmergencyStop()
        {
            if (_stopWindow != null)
            {
                // Simulate button click on the emergency stop window
                _stopWindow.Dispatcher.Invoke(() =>
                {
                    var button = _stopWindow.GetType().GetField("stopButton",
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance)?.GetValue(_stopWindow) as Button;

                    button?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                });
            }
        }

        public void Dispose()
        {
            // Unhook the event handler
            if (_mainWindow != null)
            {
                _mainWindow.PreviewKeyDown -= MainWindow_PreviewKeyDown;
            }

            // Close the emergency stop window
            _stopWindow?.Close();
        }
    }
}