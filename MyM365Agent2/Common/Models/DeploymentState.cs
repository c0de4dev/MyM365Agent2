using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;

namespace MyM365Agent2.Common.Models
{
    public class DeploymentState : ITableEntity
    {
        // Azure Table Storage required properties
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        // Core deployment properties
        public string CurrentStatus { get; set; }
        public string DeploymentHistory { get; set; }      // JSON string
        public JsonElement DeploymentId { get; set; }      // JsonElement for polymorphic JSON
        public string EventType { get; set; }
        public string JobHistory { get; set; }             // JSON string
        public JsonElement LastStatusUpdate { get; set; }  // JsonElement for polymorphic JSON
        public string Note { get; set; }
        public string Repository { get; set; }
        public JsonElement RunNumber { get; set; }         // JsonElement for polymorphic JSON
        public JsonElement RunStartedAt { get; set; }      // JsonElement for polymorphic JSON
        public string RunStatus { get; set; }
        public string RunUrl { get; set; }
        public string StatusHistory { get; set; }          // JSON string
        public string TriggerEvent { get; set; }
        public string WorkflowName { get; set; }
        public string WorkflowPath { get; set; }
        public JsonElement WorkflowRunId { get; set; }     // JsonElement for polymorphic JSON
        public string WorkflowUrl { get; set; }

        // Parsed deployment history properties
        private DeploymentHistoryInfo _deploymentInfo;
        public DeploymentHistoryInfo DeploymentInfo
        {
            get
            {
                if (_deploymentInfo == null && !string.IsNullOrEmpty(DeploymentHistory))
                {
                    try
                    {
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        if (DeploymentHistory.TrimStart().StartsWith("["))
                        {
                            var array = JsonSerializer.Deserialize<DeploymentHistoryInfo[]>(DeploymentHistory, options);
                            _deploymentInfo = array?.Length > 0 ? array[0] : new DeploymentHistoryInfo();
                        }
                        else
                        {
                            _deploymentInfo = JsonSerializer.Deserialize<DeploymentHistoryInfo>(DeploymentHistory, options);
                        }
                    }
                    catch
                    {
                        _deploymentInfo = new DeploymentHistoryInfo();
                    }
                }
                return _deploymentInfo ?? new DeploymentHistoryInfo();
            }
        }

        // Date conversion helpers
        public DateTime? LastStatusUpdateDateTime => SafeConvertToDateTime(LastStatusUpdate);
        public DateTime? RunStartedAtDateTime => SafeConvertToDateTime(RunStartedAt);

        private DateTime? SafeConvertToDateTime(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    if (DateTimeOffset.TryParse(element.GetString(), out var dto))
                        return dto.DateTime;
                    break;
                case JsonValueKind.Number:
                    if (element.TryGetInt64(out var seconds))
                        return DateTimeOffset.FromUnixTimeSeconds(seconds).DateTime;
                    break;
                default:
                    break;
            }
            return null;
        }

        // Display helpers
        public string State => CurrentStatus ?? RunStatus ?? "unknown";
        public string CreatorLogin => DeploymentInfo.Creator ?? "Unknown";
        public DateTime CreatedAt => DeploymentInfo.CreatedAtDateTime
                                   ?? RunStartedAtDateTime
                                   ?? LastStatusUpdateDateTime
                                   ?? DateTime.MinValue;
        public DateTime UpdatedAt => LastStatusUpdateDateTime
                                   ?? DeploymentInfo.UpdatedAtDateTime
                                   ?? DateTime.MinValue;
        public string Branch => DeploymentInfo.Ref ?? "main";
        public string Environment => DeploymentInfo.Environment ?? "production";
        public string WorkflowRunUrl => RunUrl ?? WorkflowUrl ?? "";

        public string DisplayStatus
        {
            get
            {
                var state = State.ToLowerInvariant();
                return state switch
                {
                    "success" or "completed" => "Success",
                    "failure" or "failed" => "Failed",
                    "pending" or "waiting" or "queued" => "Pending",
                    "in_progress" or "running" => "Running",
                    "cancelled" or "canceled" => "Cancelled",
                    _ => State
                };
            }
        }

        public string StatusCategory
        {
            get
            {
                var state = State.ToLowerInvariant();
                return state switch
                {
                    "success" or "completed" => "success",
                    "failure" or "failed" => "failure",
                    "pending" or "waiting" or "queued" => "pending",
                    "in_progress" or "running" => "in_progress",
                    "cancelled" or "canceled" => "cancelled",
                    _ => "unknown"
                };
            }
        }

        public string WorkflowDisplayName => WorkflowName ?? WorkflowPath?.Split('/').LastOrDefault() ?? "Unknown Workflow";
        public bool HasApprovalWorkflow => false;
        public string TriggerType => TriggerEvent ?? EventType ?? "manual";

        public TimeSpan? Duration
        {
            get
            {
                var start = RunStartedAtDateTime;
                var end = LastStatusUpdateDateTime;
                return (start.HasValue && end.HasValue)
                    ? end.Value - start.Value
                    : (TimeSpan?)null;
            }
        }

        public string FormattedDuration
        {
            get
            {
                var d = Duration;
                if (!d.HasValue) return "N/A";
                if (d.Value.TotalDays >= 1)
                    return $"{(int)d.Value.TotalDays}d {d.Value.Hours}h {d.Value.Minutes}m";
                if (d.Value.TotalHours >= 1)
                    return $"{d.Value.Hours}h {d.Value.Minutes}m";
                if (d.Value.TotalMinutes >= 1)
                    return $"{d.Value.Minutes}m {d.Value.Seconds}s";
                return $"{d.Value.Seconds}s";
            }
        }

        private string SafeGetRawText(JsonElement element)
        {
            try
            {
                return element.ValueKind switch
                {
                    JsonValueKind.String => element.GetString() ?? string.Empty,
                    JsonValueKind.Number => element.GetRawText(),
                    JsonValueKind.Null => string.Empty,
                    JsonValueKind.Undefined => string.Empty,
                    _ => element.ToString() ?? string.Empty,
                };
            }
            catch
            {
                return string.Empty;
            }
        }

        public string RunNumberDisplay => SafeGetRawText(RunNumber);
        public string DeploymentIdDisplay => SafeGetRawText(DeploymentId);
        public string WorkflowRunIdDisplay => SafeGetRawText(WorkflowRunId);

        // Job summary
        public JobSummary GetJobSummary()
        {
            var summary = new JobSummary();
            if (string.IsNullOrEmpty(JobHistory))
                return summary;

            try
            {
                var jobs = JsonSerializer.Deserialize<JobInfo[]>(JobHistory, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (jobs != null)
                {
                    summary.TotalJobs = jobs.Length;
                    summary.CompletedJobs = jobs.Count(j => j.status == "completed");
                    summary.SuccessfulJobs = jobs.Count(j => j.conclusion == "success");
                    summary.FailedJobs = jobs.Count(j => j.conclusion == "failure");
                    summary.InProgressJobs = jobs.Count(j => j.status == "in_progress");
                    summary.QueuedJobs = jobs.Count(j => j.status == "queued");
                    summary.TotalDuration = jobs.Where(j => j.duration_seconds.HasValue)
                                                  .Sum(j => j.duration_seconds.Value);
                }
            }
            catch { }

            return summary;
        }

        // Environment statuses
        public List<EnvironmentStatus> GetEnvironmentStatuses()
        {
            var statuses = new List<EnvironmentStatus>();
            if (string.IsNullOrEmpty(StatusHistory))
                return statuses;

            try
            {
                var items = JsonSerializer.Deserialize<StatusHistoryItem[]>(StatusHistory, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (items != null)
                {
                    statuses.AddRange(
                        items.GroupBy(s => s.Environment)
                             .Where(g => !string.IsNullOrEmpty(g.Key))
                             .Select(g => new EnvironmentStatus
                             {
                                 Environment = g.Key,
                                 Status = g.OrderByDescending(x => x.UpdatedAtDateTime ?? DateTime.MinValue)
                                               .First().State,
                                 LastUpdate = g.Max(x => x.UpdatedAtDateTime),
                                 Creator = g.First().Creator
                             })
                    );
                }
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"Error parsing status history: {ex.Message}");
                try
                {
                    if (!string.IsNullOrEmpty(Environment))
                        statuses.Add(new EnvironmentStatus
                        {
                            Environment = Environment,
                            Status = State,
                            LastUpdate = (UpdatedAt != DateTime.MinValue) ? UpdatedAt : (DateTime?)null,
                            Creator = CreatorLogin
                        });
                }
                catch { }
            }

            return statuses;
        }
    }

    public class DeploymentHistoryInfo
    {
        public string Creator { get; set; }
        public JsonElement CreatedAt { get; set; }
        public string Ref { get; set; }
        public string Id { get; set; }
        public string Environment { get; set; }
        public JsonElement UpdatedAt { get; set; }

        public DateTime? CreatedAtDateTime => SafeConvert(CreatedAt);
        public DateTime? UpdatedAtDateTime => SafeConvert(UpdatedAt);

        private static DateTime? SafeConvert(JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.String:
                    if (DateTimeOffset.TryParse(e.GetString(), out var dto))
                        return dto.DateTime;
                    break;
                case JsonValueKind.Number:
                    if (e.TryGetInt64(out var ms))
                        return DateTimeOffset.FromUnixTimeMilliseconds(ms).DateTime;
                    break;
            }
            return null;
        }
    }

    public class JobInfo
    {
        public string id { get; set; }
        public string name { get; set; }
        public string status { get; set; }
        public string html_url { get; set; }
        public string conclusion { get; set; }
        public double? duration_seconds { get; set; }
        public string runner_name { get; set; }
    }

    public class StatusHistoryItem
    {
        public string Creator { get; set; }
        public JsonElement CreatedAt { get; set; }
        public string Description { get; set; }
        public string State { get; set; }
        public JsonElement UpdatedAt { get; set; }
        public string LogUrl { get; set; }
        public string Environment { get; set; }

        public DateTime? CreatedAtDateTime => SafeConvert(CreatedAt);
        public DateTime? UpdatedAtDateTime => SafeConvert(UpdatedAt);

        private static DateTime? SafeConvert(JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.String:
                    if (DateTimeOffset.TryParse(e.GetString(), out var dto))
                        return dto.DateTime;
                    break;
                case JsonValueKind.Number:
                    if (e.TryGetInt64(out var seconds))
                        return DateTimeOffset.FromUnixTimeSeconds(seconds).DateTime;
                    break;
            }
            return null;
        }
    }

    public class JobSummary
    {
        public int TotalJobs { get; set; }
        public int CompletedJobs { get; set; }
        public int SuccessfulJobs { get; set; }
        public int FailedJobs { get; set; }
        public int InProgressJobs { get; set; }
        public int QueuedJobs { get; set; }
        public double TotalDuration { get; set; }

        public double SuccessRate => TotalJobs > 0
            ? (double)SuccessfulJobs / TotalJobs * 100
            : 0;

        public string FormattedDuration => TimeSpan.FromSeconds(TotalDuration)
            .ToString(@"hh\:mm\:ss");
    }

    public class EnvironmentStatus
    {
        public string Environment { get; set; }
        public string Status { get; set; }
        public DateTime? LastUpdate { get; set; }
        public string Creator { get; set; }
    }
}
