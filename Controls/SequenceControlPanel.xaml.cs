using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;
using Serilog;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using UaaSolutionWpf.Motion;
using UaaSolutionWpf.Sequence;

namespace UaaSolutionWpf.Controls
{
    public class SequenceItem : INotifyPropertyChanged
    {
        private readonly Action _executeAction;
        private bool _isRunning;
        private bool _isEnabled = true;

        public string Name { get; }
        public ICommand ExecuteCommand { get; }

        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                _isRunning = value;
                OnPropertyChanged();
                IsEnabled = !value;
            }
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                _isEnabled = value;
                OnPropertyChanged();
            }
        }

        public SequenceItem(string name, Action executeAction)
        {
            Name = name;
            _executeAction = executeAction;
            ExecuteCommand = new RelayCommand(Execute);
        }

        private async void Execute()
        {
            IsRunning = true;
            try
            {
                await Task.Run(() => _executeAction());
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error executing sequence {Name}: {ex.Message}",
                    "Sequence Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                IsRunning = false;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke() ?? true;
        public void Execute(object parameter) => _execute();

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }

    public partial class SequenceControlPanel : UserControl
    {
        private  ILogger _logger;
        private  MotionCoordinator _motionCoordinator;
        private readonly ObservableCollection<SequenceItem> _sequences;

        public SequenceControlPanel()
        {
            InitializeComponent();
            _sequences = new ObservableCollection<SequenceItem>();
            SequencesList.ItemsSource = _sequences;
        }

        public void Initialize(MotionCoordinator motionCoordinator, ILogger logger)
        {
            _motionCoordinator = motionCoordinator;
            _logger = logger.ForContext<SequenceControlPanel>();

            LoadPredefinedSequences();
        }

        private void LoadPredefinedSequences()
        {
            _sequences.Clear();

            // Get all static methods from MotionSequences class
            var sequenceMethods = typeof(MotionSequences)
                .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Where(m => m.ReturnType == typeof(List<CoordinatedMovement>));

            foreach (var method in sequenceMethods)
            {
                // Format the sequence name from the method name
                // e.g., "LeftPick" becomes "Left Pick"
                string sequenceName = string.Concat(method.Name.Select(c => char.IsUpper(c) ? " " + c : c.ToString())).Trim();

                AddSequence(sequenceName, async () =>
                {
                    var sequence = (List<CoordinatedMovement>)method.Invoke(null, null);
                    await _motionCoordinator.ExecuteCoordinatedMove(sequence);
                });
            }
        }

        private void AddSequence(string name, Action executeAction)
        {
            try
            {
                _sequences.Add(new SequenceItem(name, executeAction));
                _logger.Information("Added sequence: {SequenceName}", name);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error adding sequence {SequenceName}", name);
            }
        }
    }
}