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

        // String properties (as they exist in your table)
        public string CurrentStatus { get; set; }
        public string DeploymentId { get; set; }
        public string EventType { get; set; }
        public string LastStatusUpdate { get; set; }
        public string Note { get; set; }
        public string Repository { get; set; }
        public string RunNumber { get; set; }
        public string RunStartedAt { get; set; }
        public string RunStatus { get; set; }
        public string RunUrl { get; set; }
        public string TriggerEvent { get; set; }
        public string WorkflowName { get; set; }
        public string WorkflowPath { get; set; }
        public string WorkflowRunId { get; set; }
        public string WorkflowUrl { get; set; }

        // JSON string properties (these contain JSON data)
        public string DeploymentHistory { get; set; }      // JSON string
        public string JobHistory { get; set; }             // JSON string
        public string StatusHistory { get; set; }          // JSON string


        // NEW: Approval workflow properties
        public bool IsApprovalRecord => EventType == "deployment_review";
        public bool IsDeploymentRecord => EventType == "deployment";
        public string BaseWorkflowRunId => RowKey?.Contains('_') == true
            ? RowKey.Split('_')[0]
            : RowKey;

        // Enhanced approval workflow methods
        public ApprovalWorkflowSummary GetApprovalSummary()
        {
            var summary = new ApprovalWorkflowSummary();
            if (string.IsNullOrEmpty(StatusHistory))
                return summary;

            try
            {
                var items = JsonSerializer.Deserialize<StatusHistoryItem[]>(StatusHistory,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (items != null)
                {
                    summary.TotalEnvironments = items.Select(i => i.Environment).Distinct().Count();
                    summary.CompletedEnvironments = items.Where(i => i.State == "success").Select(i => i.Environment).Distinct().Count();
                    summary.PendingEnvironments = items.Where(i => i.State == "waiting").Select(i => i.Environment).Distinct().ToList();
                    summary.FailedEnvironments = items.Where(i => i.State == "failure").Select(i => i.Environment).Distinct().ToList();
                    summary.CurrentEnvironment = items.OrderByDescending(i => i.UpdatedAtDateTime ?? DateTime.MinValue).FirstOrDefault()?.Environment;
                    summary.OverallStatus = DetermineOverallApprovalStatus(items);
                }
            }
            catch { }

            return summary;
        }

        private string DetermineOverallApprovalStatus(StatusHistoryItem[] items)
        {
            if (items.Any(i => i.State == "failure"))
                return "rejected";
            if (items.Any(i => i.State == "waiting"))
                return "pending_approval";
            if (items.All(i => i.State == "success"))
                return "approved";
            return "in_progress";
        }

        // Enhanced environment progression tracking
        public List<EnvironmentProgression> GetEnvironmentProgression()
        {
            var progressions = new List<EnvironmentProgression>();
            if (string.IsNullOrEmpty(StatusHistory))
                return progressions;

            try
            {
                var items = JsonSerializer.Deserialize<StatusHistoryItem[]>(StatusHistory,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (items != null)
                {
                    var environmentGroups = items.GroupBy(i => i.Environment)
                                                 .Where(g => !string.IsNullOrEmpty(g.Key));

                    foreach (var envGroup in environmentGroups)
                    {
                        var orderedStates = envGroup.OrderBy(i => i.CreatedAtDateTime ?? DateTime.MinValue).ToList();
                        var progression = new EnvironmentProgression
                        {
                            Environment = envGroup.Key,
                            States = orderedStates.Select(s => new StateTransition
                            {
                                State = s.State,
                                Timestamp = s.UpdatedAtDateTime ?? s.CreatedAtDateTime ?? DateTime.MinValue,
                                Duration = CalculateStateDuration(orderedStates, s)
                            }).ToList(),
                            CurrentState = orderedStates.LastOrDefault()?.State ?? "unknown",
                            StartTime = orderedStates.FirstOrDefault()?.CreatedAtDateTime,
                            LastUpdateTime = orderedStates.LastOrDefault()?.UpdatedAtDateTime,
                            TotalDuration = CalculateEnvironmentDuration(orderedStates)
                        };
                        progressions.Add(progression);
                    }
                }
            }
            catch { }

            return progressions.OrderBy(p => p.StartTime ?? DateTime.MaxValue).ToList();
        }

        private TimeSpan? CalculateStateDuration(List<StatusHistoryItem> orderedStates, StatusHistoryItem currentState)
        {
            var currentIndex = orderedStates.IndexOf(currentState);
            if (currentIndex < orderedStates.Count - 1)
            {
                var nextState = orderedStates[currentIndex + 1];
                var start = currentState.CreatedAtDateTime ?? currentState.UpdatedAtDateTime;
                var end = nextState.CreatedAtDateTime ?? nextState.UpdatedAtDateTime;
                if (start.HasValue && end.HasValue)
                    return end.Value - start.Value;
            }
            return null;
        }

        private TimeSpan? CalculateEnvironmentDuration(List<StatusHistoryItem> states)
        {
            var first = states.FirstOrDefault()?.CreatedAtDateTime;
            var last = states.LastOrDefault()?.UpdatedAtDateTime;
            if (first.HasValue && last.HasValue)
                return last.Value - first.Value;
            return null;
        }

        // Enhanced display properties
        public string ApprovalDisplayStatus
        {
            get
            {
                if (!IsApprovalRecord) return DisplayStatus;

                return CurrentStatus?.ToLowerInvariant() switch
                {
                    "requested" => "Approval Requested",
                    "approved" => "Approved",
                    "rejected" => "Rejected",
                    "pending_approval" => "Pending Approval",
                    _ => CurrentStatus ?? "Unknown"
                };
            }
        }

        public bool RequiresApproval => IsApprovalRecord ||
            (!string.IsNullOrEmpty(StatusHistory) && StatusHistory.Contains("waiting"));

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
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error parsing deployment history: {ex.Message}");
                        _deploymentInfo = new DeploymentHistoryInfo();
                    }
                }
                return _deploymentInfo ?? new DeploymentHistoryInfo();
            }
        }

        // Date conversion helpers for string date fields
        public DateTime? LastStatusUpdateDateTime => SafeParseDateTime(LastStatusUpdate);
        public DateTime? RunStartedAtDateTime => SafeParseDateTime(RunStartedAt);

        private DateTime? SafeParseDateTime(string dateString)
        {
            if (string.IsNullOrEmpty(dateString)) return null;

            try
            {
                if (DateTimeOffset.TryParse(dateString, out var dto))
                    return dto.DateTime;

                if (DateTime.TryParse(dateString, out var dt))
                    return dt;

                // Try parsing as Unix timestamp if it's a number
                if (long.TryParse(dateString, out var unixTimestamp))
                {
                    return DateTimeOffset.FromUnixTimeSeconds(unixTimestamp).DateTime;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing date '{dateString}': {ex.Message}");
            }

            return null;
        }

        // Display helpers
        public string State => CurrentStatus ?? RunStatus ?? "unknown";
        public string CreatorLogin => DeploymentInfo.Creator ?? "Unknown";

        public DateTime CreatedAt
        {
            get
            {
                var candidates = new[]
                {
                    DeploymentInfo.CreatedAtDateTime,
                    RunStartedAtDateTime,
                    LastStatusUpdateDateTime
                };

                return candidates.FirstOrDefault(d => d.HasValue) ?? DateTime.MinValue;
            }
        }

        public DateTime UpdatedAt
        {
            get
            {
                var candidates = new[]
                {
                    LastStatusUpdateDateTime,
                    DeploymentInfo.UpdatedAtDateTime
                };

                return candidates.FirstOrDefault(d => d.HasValue) ?? DateTime.MinValue;
            }
        }

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
                return (start.HasValue && end.HasValue && end > start)
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

        // String display helpers for IDs and numbers
        public string RunNumberDisplay => RunNumber ?? "N/A";
        public string DeploymentIdDisplay => DeploymentId ?? "N/A";
        public string WorkflowRunIdDisplay => WorkflowRunId ?? "N/A";

        // Job summary with improved error handling
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
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing job history: {ex.Message}");
            }

            return summary;
        }

        // Environment statuses with improved error handling
        public List<EnvironmentStatus> GetEnvironmentStatuses()
        {
            var statuses = new List<EnvironmentStatus>();
            if (string.IsNullOrEmpty(StatusHistory))
            {
                // Fallback to creating a single environment status from basic info
                if (!string.IsNullOrEmpty(Environment))
                {
                    statuses.Add(new EnvironmentStatus
                    {
                        Environment = Environment,
                        Status = State,
                        LastUpdate = UpdatedAt != DateTime.MinValue ? UpdatedAt : (DateTime?)null,
                        Creator = CreatorLogin
                    });
                }
                return statuses;
            }

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

                // Fallback to basic environment status
                if (!string.IsNullOrEmpty(Environment))
                {
                    statuses.Add(new EnvironmentStatus
                    {
                        Environment = Environment,
                        Status = State,
                        LastUpdate = UpdatedAt != DateTime.MinValue ? UpdatedAt : (DateTime?)null,
                        Creator = CreatorLogin
                    });
                }
            }

            return statuses;
        }
    }

    public class DeploymentHistoryInfo
    {
        public string Creator { get; set; }
        public string CreatedAt { get; set; }
        public string Ref { get; set; }
        public string Id { get; set; }
        public string Environment { get; set; }
        public string UpdatedAt { get; set; }

        public DateTime? CreatedAtDateTime => SafeParseDateTime(CreatedAt);
        public DateTime? UpdatedAtDateTime => SafeParseDateTime(UpdatedAt);

        private static DateTime? SafeParseDateTime(string dateString)
        {
            if (string.IsNullOrEmpty(dateString)) return null;

            try
            {
                if (DateTimeOffset.TryParse(dateString, out var dto))
                    return dto.DateTime;

                if (DateTime.TryParse(dateString, out var dt))
                    return dt;

                // Try parsing as Unix timestamp if it's a number
                if (long.TryParse(dateString, out var unixTimestamp))
                {
                    return DateTimeOffset.FromUnixTimeSeconds(unixTimestamp).DateTime;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing date in DeploymentHistoryInfo '{dateString}': {ex.Message}");
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
        public string Type { get; set; } // ReviewRequest, ApproverResponse, ProtectionRuleRequest
        public string Creator { get; set; }
        public string CreatedAt { get; set; }
        public string Description { get; set; }
        public string State { get; set; }
        public string UpdatedAt { get; set; }
        public string LogUrl { get; set; }
        public string Environment { get; set; }

        // ReviewRequest specific
        public List<string> ReviewersNames { get; set; }

        // ApproverResponse specific
        public string ApproverName { get; set; }
        public string Comment { get; set; }

        // ProtectionRuleRequest specific
        public string CallbackUrl { get; set; }

        public DateTime? CreatedAtDateTime => SafeParseDateTime(CreatedAt);
        public DateTime? UpdatedAtDateTime => SafeParseDateTime(UpdatedAt);

        private static DateTime? SafeParseDateTime(string dateString)
        {
            if (string.IsNullOrEmpty(dateString)) return null;

            try
            {
                if (DateTimeOffset.TryParse(dateString, out var dto))
                    return dto.DateTime;

                if (DateTime.TryParse(dateString, out var dt))
                    return dt;

                // Try parsing as Unix timestamp if it's a number
                if (long.TryParse(dateString, out var unixTimestamp))
                {
                    return DateTimeOffset.FromUnixTimeSeconds(unixTimestamp).DateTime;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing date in StatusHistoryItem '{dateString}': {ex.Message}");
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

    public class ApprovalWorkflowSummary
    {
        public int TotalEnvironments { get; set; }
        public int CompletedEnvironments { get; set; }
        public List<string> PendingEnvironments { get; set; } = new();
        public List<string> FailedEnvironments { get; set; } = new();
        public string CurrentEnvironment { get; set; }
        public string OverallStatus { get; set; }

        public double CompletionPercentage => TotalEnvironments > 0
            ? (CompletedEnvironments * 100.0) / TotalEnvironments
            : 0;
    }
    public class StateTransition
    {
        public string State { get; set; }
        public DateTime Timestamp { get; set; }
        public TimeSpan? Duration { get; set; }
    }

    public class EnvironmentProgression
    {
        public string Environment { get; set; }
        public List<StateTransition> States { get; set; } = new();
        public string CurrentState { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? LastUpdateTime { get; set; }
        public TimeSpan? TotalDuration { get; set; }
    }
    // Helper class to group pending approvals by environment
    public class EnvironmentApprovalInfo
    {
        public string Environment { get; set; }
        public List<ReviewerApproval> ReviewerApprovals { get; set; } = new();
        public ProtectionRuleApproval ProtectionRule { get; set; }
    }

    public class ReviewerApproval
    {
        public string Description { get; set; }
        public List<string> PendingReviewers { get; set; } = new();
        public List<ApproverInfo> Responses { get; set; } = new();
        public DateTime RequestedAt { get; set; }
    }

    public class ApproverInfo
    {
        public string Name { get; set; }
        public string State { get; set; }
        public string Comment { get; set; }
        public DateTime ApprovedAt { get; set; }
    }

    public class ProtectionRuleApproval
    {
        public string Description { get; set; }
        public string CallbackUrl { get; set; }
        public string State { get; set; }
        public DateTime RequestedAt { get; set; }
    }
}