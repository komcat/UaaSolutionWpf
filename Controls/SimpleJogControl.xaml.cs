using System.Windows.Controls;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace UaaSolutionWpf.Controls
{
    public partial class SimpleJogControl : UserControl, INotifyPropertyChanged
    {
        private double stepSize = 0.1;
        private string selectedDevice;

        public event PropertyChangedEventHandler ?PropertyChanged;

        public double StepSize
        {
            get => stepSize;
            set
            {
                stepSize = value;
                OnPropertyChanged();
            }
        }

        public string SelectedDevice
        {
            get => selectedDevice;
            set
            {
                selectedDevice = value;
                OnPropertyChanged();
            }
        }

        public SimpleJogControl()
        {
            InitializeComponent();
            DataContext = this;

            // Set default selection
            DeviceListBox.SelectedIndex = 0;
            StepSizeListBox.SelectedIndex = 0;

            // Add selection change handlers
            DeviceListBox.SelectionChanged += (s, e) =>
            {
                if (e.AddedItems.Count > 0)
                {
                    var item = e.AddedItems[0] as ListBoxItem;
                    SelectedDevice = item?.Content.ToString();
                }
            };

            StepSizeListBox.SelectionChanged += (s, e) =>
            {
                if (e.AddedItems.Count > 0)
                {
                    var item = e.AddedItems[0] as ListBoxItem;
                    if (item != null)
                    {
                        string content = item.Content.ToString();
                        double.TryParse(content.Split(' ')[0], out double size);
                        StepSize = size;
                    }
                }
            };
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}