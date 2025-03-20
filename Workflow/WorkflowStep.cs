using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UaaSolutionWpf.Workflow
{
    public enum OperationStatus
    {
        NotStarted,
        InProgress,
        Completed,
        Failed,
        Skipped,
        Waiting,
        Paused
    }

    public class WorkflowStep
    {
        public string Name { get; set; }
        public string ButtonText { get; set; }
        public string NextStepId { get; set; }
        public Func<Task> Operation { get; set; }
        public bool RequiresConfirmation { get; set; }

        // Status properties
        public OperationStatus Status { get; set; } = OperationStatus.NotStarted;
        public string StatusMessage { get; set; } = string.Empty;
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public TimeSpan Duration => (EndTime ?? DateTime.Now) - (StartTime ?? DateTime.Now);
        public int RetryCount { get; set; } = 0;
        public int MaxRetries { get; set; } = 3;

        // Helper methods
        public void ResetStatus()
        {
            Status = OperationStatus.NotStarted;
            StatusMessage = string.Empty;
            StartTime = null;
            EndTime = null;
            RetryCount = 0;
        }

        public void MarkInProgress()
        {
            Status = OperationStatus.InProgress;
            StartTime = DateTime.Now;
            EndTime = null;
        }

        public void MarkCompleted(string message = "Operation completed successfully")
        {
            Status = OperationStatus.Completed;
            EndTime = DateTime.Now;
            StatusMessage = message;
        }

        public void MarkFailed(string errorMessage)
        {
            Status = OperationStatus.Failed;
            EndTime = DateTime.Now;
            StatusMessage = errorMessage;
        }

        public bool CanRetry => RetryCount < MaxRetries;

        public async Task<bool> ExecuteWithStatus()
        {
            try
            {
                MarkInProgress();
                await Operation();
                MarkCompleted();
                return true;
            }
            catch (Exception ex)
            {
                RetryCount++;
                MarkFailed(ex.Message);
                return false;
            }
        }


    }
}
