using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using UaaSolutionWpf.Workflow;

namespace UaaSolutionWpf
{
    public partial class VisionMotionWindow
    {
        private WorkflowController _workflowController;
        public void InitializeWorkflow()
        {
            

            // Initialize the workflow controller with the UI elements
            _workflowController = new WorkflowController(
                WorkflowButtonPanel,
                StatusTextBlock,
                StatusProgressBar,
                TimeTextBlock
            );
            
            SetStatus("Workflow initialized");
            
        }

        private void ResetWorkflow_Click(object sender, RoutedEventArgs e)
        {
            _workflowController.InitializeWorkflow();
            SetStatus("Workflow reset");
        }
    }
}
