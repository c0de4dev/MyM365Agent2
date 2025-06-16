using System;
using Azure;
using Azure.Data.Tables;

namespace MyM365Agent2.Common.Models
{
    public class DeploymentState : ITableEntity
    {
        public string PartitionKey { get; set; } // Repository name
        public string RowKey { get; set; } // Deployment ID
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        // GitHub deployment properties
        public string Environment { get; set; }
        public string State { get; set; } // pending, success, error, failure
        public string Description { get; set; }
        public string CreatorLogin { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string Ref { get; set; } // Branch/tag reference
        public string Sha { get; set; } // Commit SHA
        public string TaskUrl { get; set; }
        public string LogUrl { get; set; }
        public string EnvironmentUrl { get; set; }
        public int AutoMerge { get; set; }
        public string Payload { get; set; } // JSON payload
    }
}