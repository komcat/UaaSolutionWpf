using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using UaaSolutionWpf.IO;
using UaaSolutionWpf.Config;

namespace UaaSolutionWpf.ViewModels
{


    public class SlideViewModel : INotifyPropertyChanged
    {
        private SlideState _state = SlideState.Unknown;
        private readonly IOManager _ioManager;
        private readonly SlideConfiguration _config;

        public event PropertyChangedEventHandler PropertyChanged;

        public SlideViewModel(SlideConfiguration config, IOManager ioManager)
        {
            _config = config;
            _ioManager = ioManager;

            // Initialize commands
            MoveUpCommand = new RelayCommand(MoveUp, CanMoveUp);
            MoveDownCommand = new RelayCommand(MoveDown, CanMoveDown);
        }

        public string Name => _config.Name;
        public SlideConfiguration Config => _config;

        public SlideState State
        {
            get => _state;
            set
            {
                if (_state != value)
                {
                    _state = value;
                    OnPropertyChanged();
                }
            }
        }

        public ICommand MoveUpCommand { get; }
        public ICommand MoveDownCommand { get; }

        private bool CanMoveUp() =>
            State != SlideState.Up &&
            _config.Controls.Output.SetToMoveUp;

        private bool CanMoveDown() =>
            State != SlideState.Down &&
            _config.Controls.Output.ClearToMoveDown;

        private void MoveUp()
        {
            try
            {
                // Send move up signal
                _ioManager.SetOutput(
                    _config.Controls.Output.Device,
                    _config.Controls.Output.PinName
                    
                );
            }
            catch (Exception ex)
            {
                // Log or handle error
                System.Diagnostics.Debug.WriteLine($"Error moving {Name} up: {ex.Message}");
            }
        }

        private void MoveDown()
        {
            try
            {
                // Send move down signal
                _ioManager.SetOutput(
                    _config.Controls.Output.Device,
                    _config.Controls.Output.PinName
                    
                );
            }
            catch (Exception ex)
            {
                // Log or handle error
                System.Diagnostics.Debug.WriteLine($"Error moving {Name} down: {ex.Message}");
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // Utility command class for ICommand implementation
        private class RelayCommand : ICommand
        {
            private readonly Action _execute;
            private readonly Func<bool> _canExecute;

            public RelayCommand(Action execute, Func<bool> canExecute = null)
            {
                _execute = execute ?? throw new ArgumentNullException(nameof(execute));
                _canExecute = canExecute;
            }

            public event EventHandler CanExecuteChanged
            {
                add { CommandManager.RequerySuggested += value; }
                remove { CommandManager.RequerySuggested -= value; }
            }

            public bool CanExecute(object parameter) =>
                _canExecute == null || _canExecute();

            public void Execute(object parameter) => _execute();
        }
    }
}