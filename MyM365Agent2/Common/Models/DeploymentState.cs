using System;
using Azure;
using Azure.Data.Tables;

namespace MyM365Agent2.Common.Models
{
    public class DeploymentState : ITableEntity
    {
        // Azure Table Storage required properties
        public string PartitionKey { get; set; } // Repository name
        public string RowKey { get; set; } // Workflow run ID
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        // Core deployment properties - using strings to avoid casting issues
        public DateTime? createdAt { get; set; }
        public string creator { get; set; }
        public string currentStatus { get; set; }
        public string deploymentId { get; set; } // Changed from double? to string
        public string environment { get; set; }
        public string eventType { get; set; }
        public DateTime? lastStatusUpdate { get; set; }
        public string note { get; set; }
        public string @ref { get; set; } // Branch/tag reference (ref is C# keyword, so using @ref)
        public string repository { get; set; }
        public string runNumber { get; set; } // Changed from int? to string
        public DateTime? runStartedAt { get; set; }
        public string runStatus { get; set; }
        public string runUrl { get; set; }
        public string statusHistory { get; set; }
        public string triggerEvent { get; set; }
        public DateTime? updatedAt { get; set; }
        public string workflowName { get; set; }
        public string workflowPath { get; set; }
        public string workflowRunId { get; set; } // Changed from int? to string
        public string workflowUrl { get; set; }

        // Approval workflow properties
        public DateTime? requestedAt { get; set; }
        public string status { get; set; }
        public string approvalHistory { get; set; }
        public string requestor { get; set; }
        public string reviewers { get; set; }
        public string action { get; set; }

        // Helper properties for backward compatibility and UI convenience
        public string State => currentStatus ?? runStatus ?? status ?? "unknown";
        public string CreatorLogin => creator ?? requestor ?? "Unknown";
        public DateTime CreatedAt => createdAt ?? runStartedAt ?? requestedAt ?? DateTime.MinValue;
        public DateTime UpdatedAt => updatedAt ?? lastStatusUpdate ?? createdAt ?? DateTime.MinValue;
        public string Branch => @ref ?? "main";
        public string Environment => environment ?? "production";
        public string Repository => repository ?? PartitionKey ?? "Unknown";
        public string WorkflowRunUrl => runUrl ?? workflowUrl ?? "";

        // Parsed numeric properties with safe conversion
        public double? DeploymentIdNumeric
        {
            get
            {
                if (string.IsNullOrEmpty(deploymentId)) return null;
                return double.TryParse(deploymentId, out var result) ? result : null;
            }
        }

        public int? RunNumberNumeric
        {
            get
            {
                if (string.IsNullOrEmpty(runNumber)) return null;
                return int.TryParse(runNumber, out var result) ? result : null;
            }
        }

        public int? WorkflowRunIdNumeric
        {
            get
            {
                if (string.IsNullOrEmpty(workflowRunId)) return null;
                return int.TryParse(workflowRunId, out var result) ? result : null;
            }
        }

        // Enhanced display properties
        public string DisplayStatus
        {
            get
            {
                var state = State.ToLower();
                return state switch
                {
                    "success" or "completed" or "approved" => "Success",
                    "failure" or "failed" or "rejected" => "Failed",
                    "pending" or "waiting" or "pending_approval" => "Pending",
                    "in_progress" or "running" => "Running",
                    "queued" => "Queued",
                    "cancelled" or "canceled" => "Cancelled",
                    _ => State
                };
            }
        }

        public string StatusCategory
        {
            get
            {
                var state = State.ToLower();
                return state switch
                {
                    "success" or "completed" or "approved" => "success",
                    "failure" or "failed" or "rejected" => "failure",
                    "pending" or "waiting" or "pending_approval" or "queued" => "pending",
                    "in_progress" or "running" => "in_progress",
                    "cancelled" or "canceled" => "cancelled",
                    _ => "unknown"
                };
            }
        }

        public string WorkflowDisplayName => workflowName ?? workflowPath?.Split('/').LastOrDefault() ?? "Unknown Workflow";

        public bool HasApprovalWorkflow => !string.IsNullOrEmpty(requestor) || !string.IsNullOrEmpty(reviewers) || !string.IsNullOrEmpty(approvalHistory);

        public string TriggerType => triggerEvent ?? eventType ?? "manual";

        public TimeSpan? Duration
        {
            get
            {
                if (runStartedAt.HasValue && updatedAt.HasValue)
                {
                    return updatedAt.Value - runStartedAt.Value;
                }
                return null;
            }
        }

        public string FormattedDuration
        {
            get
            {
                var duration = Duration;
                if (!duration.HasValue) return "N/A";

                if (duration.Value.TotalDays >= 1)
                    return $"{(int)duration.Value.TotalDays}d {duration.Value.Hours}h {duration.Value.Minutes}m";
                else if (duration.Value.TotalHours >= 1)
                    return $"{duration.Value.Hours}h {duration.Value.Minutes}m";
                else if (duration.Value.TotalMinutes >= 1)
                    return $"{duration.Value.Minutes}m {duration.Value.Seconds}s";
                else
                    return $"{duration.Value.Seconds}s";
            }
        }

        // Helper property for display in UI
        public string RunNumberDisplay => RunNumberNumeric?.ToString() ?? runNumber ?? "N/A";
        public string DeploymentIdDisplay => DeploymentIdNumeric?.ToString("F0") ?? deploymentId ?? "N/A";
        public string WorkflowRunIdDisplay => WorkflowRunIdNumeric?.ToString() ?? workflowRunId ?? "N/A";
    }
}