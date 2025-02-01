using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using UaaSolutionWpf.Controls;

namespace UaaSolutionWpf
{

    
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private StackPanel leftPanel;

        public MainWindow()
        {
            InitializeComponent();
            // Set names for each Hexapod
            if (LeftHexapodControl != null)
                ((HexapodControl)LeftHexapodControl).RobotName = "Left Hexapod";

            if (BottomHexapodControl != null)
                ((HexapodControl)BottomHexapodControl).RobotName = "Bottom Hexapod";

            if (RightHexapodControl != null)
                ((HexapodControl)RightHexapodControl).RobotName = "Right Hexapod";
        }


        private void InitializeControls()
        {
            // Find the StackPanel from the Grid
            leftPanel = (StackPanel)this.FindName("LeftStackPanel");

            // If the StackPanel wasn't found by name, we can get it from the Grid
            if (leftPanel == null)
            {
                // Get the main grid
                Grid mainGrid = (Grid)this.Content;
                // Get the first StackPanel in column 0
                leftPanel = (StackPanel)mainGrid.Children[0];
            }

            // Create and add the TEC Controller
            TECController tecController = new TECController();
            leftPanel.Children.Add(tecController);
        }

    }
}