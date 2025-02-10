using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace UaaSolutionWpf.ViewModels
{
    public enum SlideState
    {
        Up,
        Down,
        Unknown
    }

    public class PneumaticSlideViewModel : INotifyPropertyChanged
    {
        private string _name;
        private SlideState _state;
        private bool _isUpSensorActive;
        private bool _isDownSensorActive;

        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                OnPropertyChanged();
            }
        }

        public SlideState State
        {
            get => _state;
            private set
            {
                _state = value;
                OnPropertyChanged();
            }
        }

        public bool IsUpSensorActive
        {
            get => _isUpSensorActive;
            set
            {
                _isUpSensorActive = value;
                UpdateState();
                OnPropertyChanged();
            }
        }

        public bool IsDownSensorActive
        {
            get => _isDownSensorActive;
            set
            {
                _isDownSensorActive = value;
                UpdateState();
                OnPropertyChanged();
            }
        }

        private void UpdateState()
        {
            if (IsUpSensorActive && !IsDownSensorActive)
                State = SlideState.Up;
            else if (!IsUpSensorActive && IsDownSensorActive)
                State = SlideState.Down;
            else
                State = SlideState.Unknown;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class PneumaticSlidesViewModel : INotifyPropertyChanged
    {
        public PneumaticSlideViewModel UVSlide { get; }
        public PneumaticSlideViewModel DispenserSlide { get; }
        public PneumaticSlideViewModel PickUpToolSlide { get; }

        public PneumaticSlidesViewModel()
        {
            UVSlide = new PneumaticSlideViewModel { Name = "UV Head" };
            DispenserSlide = new PneumaticSlideViewModel { Name = "Dispenser Head" };
            PickUpToolSlide = new PneumaticSlideViewModel { Name = "Pick Up Tool" };
        }

        public void UpdateSensorState(string sensorName, bool state)
        {
            switch (sensorName)
            {
                case "UV_Head_Up":
                    UVSlide.IsUpSensorActive = state;
                    break;
                case "UV_Head_Down":
                    UVSlide.IsDownSensorActive = state;
                    break;
                case "Dispenser_Head_Up":
                    DispenserSlide.IsUpSensorActive = state;
                    break;
                case "Dispenser_Head_Down":
                    DispenserSlide.IsDownSensorActive = state;
                    break;
                case "Pick_Up_Tool_Up":
                    PickUpToolSlide.IsUpSensorActive = state;
                    break;
                case "Pick_Up_Tool_Down":
                    PickUpToolSlide.IsDownSensorActive = state;
                    break;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}