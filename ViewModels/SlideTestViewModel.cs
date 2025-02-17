using System;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using UaaSolutionWpf.Services;

namespace UaaSolutionWpf.ViewModels
{
    public class SlideTestViewModel : INotifyPropertyChanged
    {
        private readonly string _slideId;
        private readonly string _name;
        private readonly PneumaticSlideService _slideService;
        private SlideState _currentState;
        private bool _isOperating;
        private string _statusMessage;
        private SolidColorBrush _statusColor;

        public event PropertyChangedEventHandler PropertyChanged;

        public string SlideId => _slideId;
        public string Name => _name;

        public SlideState CurrentState
        {
            get => _currentState;
            private set
            {
                if (_currentState != value)
                {
                    _currentState = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsOperating
        {
            get => _isOperating;
            private set
            {
                if (_isOperating != value)
                {
                    _isOperating = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CanOperate));
                }
            }
        }

        public bool CanOperate => !IsOperating;

        public string StatusMessage
        {
            get => _statusMessage;
            private set
            {
                if (_statusMessage != value)
                {
                    _statusMessage = value;
                    OnPropertyChanged();
                }
            }
        }

        public SolidColorBrush StatusColor
        {
            get => _statusColor;
            private set
            {
                if (_statusColor != value)
                {
                    _statusColor = value;
                    OnPropertyChanged();
                }
            }
        }

        public ICommand ActivateCommand { get; }
        public ICommand DeactivateCommand { get; }

        public SlideTestViewModel(string slideId, string name, PneumaticSlideService slideService)
        {
            _slideId = slideId;
            _name = name;
            _slideService = slideService;
            _currentState = slideService.GetCurrentState(slideId);
            _statusColor = new SolidColorBrush(Colors.Gray);

            ActivateCommand = new RelayCommand(async () => await OperateSlideAsync(true));
            DeactivateCommand = new RelayCommand(async () => await OperateSlideAsync(false));

            slideService.SlideStateChanged += OnSlideStateChanged;
        }

        private void OnSlideStateChanged(object sender, SlideStateChangedEventArgs e)
        {
            if (e.SlideId == _slideId)
            {
                CurrentState = e.NewState;
            }
        }

        private async Task OperateSlideAsync(bool activate)
        {
            if (IsOperating) return;

            try
            {
                IsOperating = true;
                StatusMessage = "Operating...";
                StatusColor = new SolidColorBrush(Colors.Gray);

                var result = activate ?
                    await _slideService.ActivateSlideAsync(_slideId) :
                    await _slideService.DeactivateSlideAsync(_slideId);

                if (result.Success)
                {
                    StatusMessage = $"Operation completed in {result.Duration.TotalMilliseconds:F0}ms";
                    StatusColor = new SolidColorBrush(Colors.Green);
                }
                else
                {
                    StatusMessage = $"Operation failed: {result.Error}";
                    StatusColor = new SolidColorBrush(Colors.Red);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                StatusColor = new SolidColorBrush(Colors.Red);
            }
            finally
            {
                IsOperating = false;
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}