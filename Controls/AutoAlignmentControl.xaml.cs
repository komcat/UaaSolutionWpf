using System.Linq;
using System.Windows.Controls;

namespace UaaSolutionWpf.Controls
{
    public partial class AutoAlignmentControl : UserControl
    {
        private readonly double[] coarseValues = { 0.002, 0.001, 0.0005, 0.0003 };
        private readonly double[] fineValues = { 0.0002, 0.0001 };
        private TextBlock resolutionTextBlock;

        public AutoAlignmentControl()
        {
            InitializeComponent();
            resolutionTextBlock = FindName("ResolutionTextBlock") as TextBlock;
            var listBox = FindName("ModeListBox") as ListBox;
            if (listBox != null)
            {
                listBox.SelectionChanged += ListBox_SelectionChanged;
                UpdateResolutionText((listBox.SelectedItem as ListBoxItem)?.Content.ToString());
            }
        }

        private void ListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.AddedItems.Count > 0 && e.AddedItems[0] is ListBoxItem selectedItem)
            {
                UpdateResolutionText(selectedItem.Content.ToString());
            }
        }

        private void UpdateResolutionText(string mode)
        {
            if (resolutionTextBlock == null) return;

            double[] values = mode == "Coarse" ? coarseValues : fineValues;
            string valuesText = string.Join(",", values.Select(v => v.ToString("0.0000")));
            resolutionTextBlock.Text = $"{valuesText} mm";
        }
    }
}