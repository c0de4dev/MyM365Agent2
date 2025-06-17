using System;
using Azure;
using Azure.Data.Tables;

namespace MyM365Agent2.Common.Models
{
    public class DeploymentState : ITableEntity
    {
        public string PartitionKey { get; set; } // Repository name
        public string RowKey { get; set; } // Workflow run ID
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
        
        // GitHub deployment properties from actual data
        public string creator { get; set; }
        public long? deploymentId { get; set; }
        public string environment { get; set; }
        public string eventType { get; set; }
        public DateTime? inserted { get; set; }
        public string branch { get; set; } // Branch/tag reference
        public DateTime? runStartedAt { get; set; }
        public string runStatus { get; set; } // queued, in_progress, completed, etc.
        public string statusHistory { get; set; } // JSON array of status changes
        public string workflowName { get; set; }
        public long? workflowRunId { get; set; }
        public string workflowUrl { get; set; }
        public string workflowrunUrl { get; set; }
        
        // Helper properties for backward compatibility
        public string State => runStatus;
        public string CreatorLogin => creator;
        public DateTime CreatedAt => inserted ?? runStartedAt ?? DateTime.MinValue;
        public DateTime UpdatedAt => inserted ?? runStartedAt ?? DateTime.MinValue;
        public string Branch => branch;
        public string Environment => environment;
    }
}