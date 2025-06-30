using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MyM365Agent2.Common.Models;
using System.Text.Json;

namespace MyM365Agent2.Services
{
    // Raw entity that matches your actual Azure Table data structure
    public class RawDeploymentEntity : ITableEntity
    {
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        // String properties (camelCase as they appear in your table)
        public string currentStatus { get; set; }
        public string deploymentId { get; set; }
        public string eventType { get; set; }
        public string lastStatusUpdate { get; set; }
        public string note { get; set; }
        public string repository { get; set; }
        public string runNumber { get; set; }
        public string runStartedAt { get; set; }
        public string runStatus { get; set; }
        public string runUrl { get; set; }
        public string triggerEvent { get; set; }
        public string workflowName { get; set; }
        public string workflowPath { get; set; }
        public string workflowRunId { get; set; }
        public string workflowUrl { get; set; }
        public string environment { get; set; } // Add environment field

        // JSON string properties (these contain JSON data)
        public string deploymentHistory { get; set; }
        public string jobHistory { get; set; }
        public string statusHistory { get; set; }

        // Convert to DeploymentState
        public DeploymentState ToDeploymentState()
        {
            var deployment = new DeploymentState
            {
                PartitionKey = PartitionKey ?? repository, // Use repository as PartitionKey if not set
                RowKey = RowKey,
                Timestamp = Timestamp,
                ETag = ETag,

                // Direct string mappings
                CurrentStatus = currentStatus,
                EventType = eventType,
                Note = note,
                Repository = repository,
                RunStatus = runStatus,
                RunUrl = runUrl,
                TriggerEvent = triggerEvent,
                WorkflowName = workflowName,
                WorkflowPath = workflowPath,
                WorkflowUrl = workflowUrl,

                // JSON string mappings (clean up escaped quotes)
                DeploymentHistory = CleanJsonString(deploymentHistory),
                JobHistory = CleanJsonString(jobHistory),
                StatusHistory = CleanJsonString(statusHistory)
            };

            // Handle JsonElement properties with proper conversion
            if (!string.IsNullOrEmpty(deploymentId))
                deployment.DeploymentId = deploymentId;

            if (!string.IsNullOrEmpty(lastStatusUpdate))
                deployment.LastStatusUpdate = lastStatusUpdate;

            if (!string.IsNullOrEmpty(runNumber))
                deployment.RunNumber = runNumber;

            if (!string.IsNullOrEmpty(runStartedAt))
                deployment.RunStartedAt = runStartedAt;

            if (!string.IsNullOrEmpty(workflowRunId))
                deployment.WorkflowRunId = workflowRunId;

            return deployment;
        }

        private string CleanJsonString(string jsonValue)
        {
            if (string.IsNullOrEmpty(jsonValue)) return jsonValue;

            // Handle double-escaped quotes from CSV export
            return jsonValue.Replace("\"\"", "\"");
        }

        // Convert from DeploymentState for updates
        public static RawDeploymentEntity FromDeploymentState(DeploymentState deployment)
        {
            return new RawDeploymentEntity
            {
                PartitionKey = deployment.PartitionKey,
                RowKey = deployment.RowKey,
                Timestamp = deployment.Timestamp,
                ETag = deployment.ETag,

                currentStatus = deployment.CurrentStatus,
                deploymentId = deployment.DeploymentId,
                eventType = deployment.EventType,
                lastStatusUpdate = deployment.LastStatusUpdate,
                note = deployment.Note,
                repository = deployment.Repository,
                runNumber = deployment.RunNumber,
                runStartedAt = deployment.RunStartedAt,
                runStatus = deployment.RunStatus,
                runUrl = deployment.RunUrl,
                triggerEvent = deployment.TriggerEvent,
                workflowName = deployment.WorkflowName,
                workflowPath = deployment.WorkflowPath,
                workflowRunId = deployment.WorkflowRunId,
                workflowUrl = deployment.WorkflowUrl,
                environment = deployment.Environment,

                deploymentHistory = deployment.DeploymentHistory,
                jobHistory = deployment.JobHistory,
                statusHistory = deployment.StatusHistory
            };
        }

        private static string GetStringFromJsonElement(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.GetRawText(),
                JsonValueKind.Null => null,
                JsonValueKind.Undefined => null,
                _ => element.ToString()
            };
        }
    }

    public interface IAzureTableStorageService
    {
        // Existing methods
        Task<List<DeploymentState>> GetDeploymentsAsync(string repository = null);
        Task<DeploymentState> GetDeploymentAsync(string repository, string deploymentId);
        Task<List<DeploymentState>> GetDeploymentsByEnvironmentAsync(string environment);
        Task<List<DeploymentState>> GetDeploymentsByStateAsync(string state);
        Task<List<DeploymentState>> GetDeploymentsByCreatorAsync(string creator);
        Task<List<DeploymentState>> GetPendingApprovalsAsync();
        Task<Dictionary<string, int>> GetDeploymentStatisticsAsync();
        Task<Dictionary<string, Dictionary<string, int>>> GetEnvironmentStatisticsAsync();
        Task<Dictionary<string, Dictionary<string, int>>> GetRepositoryStatisticsAsync();
        Task<Dictionary<string, Dictionary<string, int>>> GetCreatorStatisticsAsync();
        Task<List<DeploymentState>> GetRecentDeploymentsAsync(int count = 10);

        // Enhanced methods for repository and environment filtering
        Task<List<DeploymentState>> GetDeploymentsWithFiltersAsync(string repository = null, string environment = null, string status = null);
        Task<List<string>> GetAvailableRepositoriesAsync();
        Task<List<string>> GetAvailableEnvironmentsAsync();
        Task<List<string>> GetAvailableEnvironmentsForRepositoryAsync(string repository);
        Task<Dictionary<string, int>> GetDeploymentStatisticsForRepoAsync(string repository);
        Task<Dictionary<string, int>> GetDeploymentStatisticsForEnvironmentAsync(string environment);
        Task<Dictionary<string, int>> GetDeploymentStatisticsWithFiltersAsync(string repository = null, string environment = null);
        Task<List<DeploymentState>> GetWorkflowRunsForRepositoryAsync(string repository, int limit = 50);
        Task<Dictionary<string, List<DeploymentState>>> GetLatestDeploymentsByEnvironmentAsync(string repository = null);

        // Approval workflow methods
        Task<bool> UpdateDeploymentAsync(DeploymentState deployment);
        Task<bool> UpdateDeploymentStatusAsync(string repository, string deploymentId, string newStatus, string approver, string comment = null);
        Task<DeploymentState> GetRelatedDeploymentAsync(string repository, string workflowRunId, bool getProtectionRule = false);
        Task<List<DeploymentState>> GetPendingApprovalsByEnvironmentAsync(string environment = null);
        Task<bool> ApproveDeploymentAsync(string repository, string deploymentId, string approver, string comment = null);
        Task<bool> RejectDeploymentAsync(string repository, string deploymentId, string approver, string comment = null);

        // Diagnostic methods
        Task<bool> TestConnectionAsync();
        Task<long> GetTableEntityCountAsync();
        string GetConnectionInfo();
    }

    public class AzureTableStorageService : IAzureTableStorageService
    {
        private readonly TableClient _tableClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AzureTableStorageService> _logger;
        private readonly string _connectionString;
        private readonly string _tableName;

        public AzureTableStorageService(IConfiguration configuration, ILogger<AzureTableStorageService> logger)
        {
            _configuration = configuration;
            _logger = logger;

            _connectionString = _configuration["AzureTableStorage:ConnectionString"];
            _tableName = _configuration["AzureTableStorage:DeploymentTableName"] ?? "GitHubDeployments";

            if (string.IsNullOrEmpty(_connectionString))
            {
                var errorMsg = "Azure Table Storage connection string is not configured. Please check your appsettings.json.";
                _logger.LogError(errorMsg);
                throw new InvalidOperationException(errorMsg);
            }

            try
            {
                var serviceClient = new TableServiceClient(_connectionString);
                _tableClient = serviceClient.GetTableClient(_tableName);

                _logger.LogInformation($"Azure Table Storage initialized successfully. Table: {_tableName}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Azure Table Storage connection");
                throw;
            }
        }

        #region Diagnostic Methods

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                _logger.LogInformation("Testing Azure Table Storage connection...");

                // Try to query a single entity to test connection
                await foreach (var entity in _tableClient.QueryAsync<RawDeploymentEntity>(maxPerPage: 1))
                {
                    _logger.LogInformation($"Connection test successful. Found entity with RowKey: {entity.RowKey}");
                    return true;
                }

                _logger.LogInformation("Connection successful but table appears to be empty");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Connection test failed");
                return false;
            }
        }

        public async Task<long> GetTableEntityCountAsync()
        {
            try
            {
                long count = 0;
                await foreach (var entity in _tableClient.QueryAsync<RawDeploymentEntity>())
                {
                    count++;
                    if (count % 1000 == 0)
                    {
                        _logger.LogInformation($"Counted {count} entities so far...");
                    }
                }

                _logger.LogInformation($"Table contains {count} entities");
                return count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get entity count");
                throw;
            }
        }

        public string GetConnectionInfo()
        {
            try
            {
                var info = new
                {
                    TableName = _tableName,
                    HasConnectionString = !string.IsNullOrEmpty(_connectionString),
                    ConnectionStringLength = _connectionString?.Length ?? 0,
                    StorageAccount = ExtractStorageAccountName(_connectionString)
                };

                return System.Text.Json.JsonSerializer.Serialize(info, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get connection info");
                return $"Error getting connection info: {ex.Message}";
            }
        }

        private string ExtractStorageAccountName(string connectionString)
        {
            try
            {
                if (string.IsNullOrEmpty(connectionString)) return "Unknown";

                var accountStart = connectionString.IndexOf("AccountName=");
                if (accountStart == -1) return "Unknown";

                accountStart += 12; // Length of "AccountName="
                var accountEnd = connectionString.IndexOf(";", accountStart);
                if (accountEnd == -1) accountEnd = connectionString.Length;

                return connectionString.Substring(accountStart, accountEnd - accountStart);
            }
            catch
            {
                return "Unknown";
            }
        }

        #endregion

        #region Helper Methods

        private async Task<List<DeploymentState>> GetDeploymentsFromRawEntitiesAsync(string filter = null)
        {
            try
            {
                var deployments = new List<DeploymentState>();

                await foreach (var rawEntity in _tableClient.QueryAsync<RawDeploymentEntity>(filter: filter))
                {
                    try
                    {
                        var deployment = rawEntity.ToDeploymentState();
                        deployments.Add(deployment);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Error converting raw entity to deployment state for RowKey: {rawEntity.RowKey}");
                    }
                }

                _logger.LogInformation($"Successfully converted {deployments.Count} raw entities to deployment states");
                return deployments;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error querying raw entities with filter: {filter}");
                throw;
            }
        }

        #endregion

        #region Core Methods

        public async Task<List<DeploymentState>> GetDeploymentsAsync(string repository = null)
        {
            try
            {
                _logger.LogInformation($"Fetching deployments for repository: {repository ?? "all"}");

                string filter = null;
                if (!string.IsNullOrEmpty(repository))
                {
                    // Try both PartitionKey and repository field
                    filter = $"PartitionKey eq '{repository}' or repository eq '{repository}'";
                }

                var deployments = await GetDeploymentsFromRawEntitiesAsync(filter);

                _logger.LogInformation($"Retrieved {deployments.Count} deployments");
                return deployments.OrderByDescending(d => d.CreatedAt).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching deployments for repository: {repository}");
                throw;
            }
        }

        public async Task<DeploymentState> GetDeploymentAsync(string repository, string deploymentId)
        {
            try
            {
                _logger.LogInformation($"Fetching deployment: {repository}/{deploymentId}");

                // Try different combinations since PartitionKey might not be set correctly
                string[] partitionKeys = { repository, deploymentId.Split('_')[0] };

                foreach (var partitionKey in partitionKeys)
                {
                    try
                    {
                        var response = await _tableClient.GetEntityAsync<RawDeploymentEntity>(partitionKey, deploymentId);
                        var deployment = response.Value.ToDeploymentState();

                        _logger.LogInformation($"Successfully retrieved deployment: {repository}/{deploymentId}");
                        return deployment;
                    }
                    catch (RequestFailedException ex) when (ex.Status == 404)
                    {
                        // Continue to next partition key
                        continue;
                    }
                }

                // If not found by exact keys, try querying by RowKey
                var filter = $"RowKey eq '{deploymentId}'";
                await foreach (var rawEntity in _tableClient.QueryAsync<RawDeploymentEntity>(filter: filter))
                {
                    var deployment = rawEntity.ToDeploymentState();
                    if (deployment.Repository == repository || string.IsNullOrEmpty(repository))
                    {
                        _logger.LogInformation($"Found deployment by RowKey: {repository}/{deploymentId}");
                        return deployment;
                    }
                }

                _logger.LogWarning($"Deployment not found: {repository}/{deploymentId}");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching deployment: {repository}/{deploymentId}");
                throw;
            }
        }

        public async Task<Dictionary<string, int>> GetDeploymentStatisticsAsync()
        {
            try
            {
                _logger.LogInformation("Calculating deployment statistics");

                var statistics = new Dictionary<string, int>
                {
                    { "success", 0 },
                    { "failure", 0 },
                    { "pending", 0 },
                    { "in_progress", 0 },
                    { "cancelled", 0 },
                    { "pending_approval", 0 },
                    { "total", 0 }
                };

                var allDeployments = await GetDeploymentsFromRawEntitiesAsync();

                foreach (var deployment in allDeployments)
                {
                    statistics["total"]++;
                    var category = deployment.StatusCategory;

                    if (statistics.ContainsKey(category))
                    {
                        statistics[category]++;
                    }

                    if (deployment.HasApprovalWorkflow && deployment.State.ToLower().Contains("pending"))
                    {
                        statistics["pending_approval"]++;
                    }
                }

                _logger.LogInformation($"Calculated statistics for {statistics["total"]} deployments");
                return statistics;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating deployment statistics");
                throw;
            }
        }

        public async Task<Dictionary<string, Dictionary<string, int>>> GetRepositoryStatisticsAsync()
        {
            try
            {
                _logger.LogInformation("Calculating repository statistics");

                var statistics = new Dictionary<string, Dictionary<string, int>>();
                var allDeployments = await GetDeploymentsFromRawEntitiesAsync();

                foreach (var deployment in allDeployments)
                {
                    var repo = deployment.Repository;
                    if (string.IsNullOrEmpty(repo)) continue;

                    if (!statistics.ContainsKey(repo))
                    {
                        statistics[repo] = new Dictionary<string, int>
                        {
                            { "success", 0 },
                            { "failure", 0 },
                            { "pending", 0 },
                            { "in_progress", 0 },
                            { "cancelled", 0 },
                            { "total", 0 }
                        };
                    }

                    statistics[repo]["total"]++;
                    var category = deployment.StatusCategory;

                    if (statistics[repo].ContainsKey(category))
                    {
                        statistics[repo][category]++;
                    }
                }

                _logger.LogInformation($"Calculated statistics for {statistics.Count} repositories");
                return statistics;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating repository statistics");
                throw;
            }
        }

        public async Task<Dictionary<string, Dictionary<string, int>>> GetEnvironmentStatisticsAsync()
        {
            try
            {
                _logger.LogInformation("Calculating environment statistics");

                var statistics = new Dictionary<string, Dictionary<string, int>>();
                var allDeployments = await GetDeploymentsFromRawEntitiesAsync();

                foreach (var deployment in allDeployments)
                {
                    var env = deployment.Environment;
                    if (string.IsNullOrEmpty(env)) continue;

                    if (!statistics.ContainsKey(env))
                    {
                        statistics[env] = new Dictionary<string, int>
                        {
                            { "success", 0 },
                            { "failure", 0 },
                            { "pending", 0 },
                            { "in_progress", 0 },
                            { "cancelled", 0 },
                            { "total", 0 }
                        };
                    }

                    statistics[env]["total"]++;
                    var category = deployment.StatusCategory;

                    if (statistics[env].ContainsKey(category))
                    {
                        statistics[env][category]++;
                    }
                }

                _logger.LogInformation($"Calculated statistics for {statistics.Count} environments");
                return statistics;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating environment statistics");
                throw;
            }
        }

        #endregion

        #region Filter and Query Methods

        public async Task<List<DeploymentState>> GetDeploymentsByEnvironmentAsync(string environment)
        {
            var allDeployments = await GetDeploymentsFromRawEntitiesAsync();
            return allDeployments
                .Where(d => d.Environment.Equals(environment, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(d => d.CreatedAt)
                .ToList();
        }

        public async Task<List<DeploymentState>> GetDeploymentsByStateAsync(string state)
        {
            var allDeployments = await GetDeploymentsFromRawEntitiesAsync();
            return allDeployments
                .Where(d => d.StatusCategory.Equals(state, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(d => d.CreatedAt)
                .ToList();
        }

        public async Task<List<DeploymentState>> GetDeploymentsByCreatorAsync(string creator)
        {
            var allDeployments = await GetDeploymentsFromRawEntitiesAsync();
            return allDeployments
                .Where(d => d.CreatorLogin.Equals(creator, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(d => d.CreatedAt)
                .ToList();
        }

        public async Task<List<DeploymentState>> GetPendingApprovalsAsync()
        {
            var allDeployments = await GetDeploymentsFromRawEntitiesAsync();
            return allDeployments
                .Where(d => d.StatusCategory == "pending" || d.State.ToLower().Contains("pending"))
                .OrderBy(d => d.RunStartedAtDateTime ?? d.CreatedAt)
                .ToList();
        }

        public async Task<Dictionary<string, Dictionary<string, int>>> GetCreatorStatisticsAsync()
        {
            var statistics = new Dictionary<string, Dictionary<string, int>>();
            var allDeployments = await GetDeploymentsFromRawEntitiesAsync();

            foreach (var deployment in allDeployments)
            {
                var creator = deployment.CreatorLogin;
                if (string.IsNullOrEmpty(creator)) continue;

                if (!statistics.ContainsKey(creator))
                {
                    statistics[creator] = new Dictionary<string, int>
                    {
                        { "success", 0 }, { "failure", 0 }, { "pending", 0 },
                        { "in_progress", 0 }, { "cancelled", 0 }, { "total", 0 }
                    };
                }

                statistics[creator]["total"]++;
                var category = deployment.StatusCategory;
                if (statistics[creator].ContainsKey(category))
                {
                    statistics[creator][category]++;
                }
            }

            return statistics;
        }

        public async Task<List<DeploymentState>> GetRecentDeploymentsAsync(int count = 10)
        {
            var allDeployments = await GetDeploymentsFromRawEntitiesAsync();
            return allDeployments
                .OrderByDescending(d => d.CreatedAt)
                .Take(count)
                .ToList();
        }

        public async Task<List<DeploymentState>> GetDeploymentsWithFiltersAsync(string repository = null, string environment = null, string status = null)
        {
            var allDeployments = await GetDeploymentsFromRawEntitiesAsync();

            var filtered = allDeployments.AsEnumerable();

            if (!string.IsNullOrEmpty(repository))
                filtered = filtered.Where(d => d.Repository.Equals(repository, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(environment))
                filtered = filtered.Where(d => d.Environment.Equals(environment, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(status))
                filtered = filtered.Where(d => d.StatusCategory.Equals(status, StringComparison.OrdinalIgnoreCase));

            return filtered.OrderByDescending(d => d.CreatedAt).ToList();
        }

        public async Task<List<string>> GetAvailableRepositoriesAsync()
        {
            var allDeployments = await GetDeploymentsFromRawEntitiesAsync();
            return allDeployments
                .Select(d => d.Repository)
                .Where(r => !string.IsNullOrEmpty(r))
                .Distinct()
                .OrderBy(r => r)
                .ToList();
        }

        public async Task<List<string>> GetAvailableEnvironmentsAsync()
        {
            var allDeployments = await GetDeploymentsFromRawEntitiesAsync();
            return allDeployments
                .Select(d => d.Environment)
                .Where(e => !string.IsNullOrEmpty(e))
                .Distinct()
                .OrderBy(e => e)
                .ToList();
        }

        public async Task<List<string>> GetAvailableEnvironmentsForRepositoryAsync(string repository)
        {
            var allDeployments = await GetDeploymentsFromRawEntitiesAsync();
            return allDeployments
                .Where(d => d.Repository.Equals(repository, StringComparison.OrdinalIgnoreCase))
                .Select(d => d.Environment)
                .Where(e => !string.IsNullOrEmpty(e))
                .Distinct()
                .OrderBy(e => e)
                .ToList();
        }

        public async Task<Dictionary<string, int>> GetDeploymentStatisticsForRepoAsync(string repository)
        {
            var statistics = new Dictionary<string, int>
            {
                { "success", 0 }, { "failure", 0 }, { "pending", 0 },
                { "in_progress", 0 }, { "cancelled", 0 }, { "total", 0 }
            };

            var allDeployments = await GetDeploymentsFromRawEntitiesAsync();
            var repoDeployments = allDeployments.Where(d => d.Repository.Equals(repository, StringComparison.OrdinalIgnoreCase));

            foreach (var deployment in repoDeployments)
            {
                statistics["total"]++;
                var category = deployment.StatusCategory;
                if (statistics.ContainsKey(category))
                {
                    statistics[category]++;
                }
            }

            return statistics;
        }

        public async Task<Dictionary<string, int>> GetDeploymentStatisticsForEnvironmentAsync(string environment)
        {
            var statistics = new Dictionary<string, int>
            {
                { "success", 0 }, { "failure", 0 }, { "pending", 0 },
                { "in_progress", 0 }, { "cancelled", 0 }, { "total", 0 }
            };

            var allDeployments = await GetDeploymentsFromRawEntitiesAsync();
            var envDeployments = allDeployments.Where(d => d.Environment.Equals(environment, StringComparison.OrdinalIgnoreCase));

            foreach (var deployment in envDeployments)
            {
                statistics["total"]++;
                var category = deployment.StatusCategory;
                if (statistics.ContainsKey(category))
                {
                    statistics[category]++;
                }
            }

            return statistics;
        }

        public async Task<Dictionary<string, int>> GetDeploymentStatisticsWithFiltersAsync(string repository = null, string environment = null)
        {
            var statistics = new Dictionary<string, int>
            {
                { "success", 0 }, { "failure", 0 }, { "pending", 0 },
                { "in_progress", 0 }, { "cancelled", 0 }, { "total", 0 }
            };

            var filteredDeployments = await GetDeploymentsWithFiltersAsync(repository, environment);

            foreach (var deployment in filteredDeployments)
            {
                statistics["total"]++;
                var category = deployment.StatusCategory;
                if (statistics.ContainsKey(category))
                {
                    statistics[category]++;
                }
            }

            return statistics;
        }

        public async Task<List<DeploymentState>> GetWorkflowRunsForRepositoryAsync(string repository, int limit = 50)
        {
            var allDeployments = await GetDeploymentsFromRawEntitiesAsync();
            return allDeployments
                .Where(d => d.Repository.Equals(repository, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(d => d.CreatedAt)
                .Take(limit)
                .ToList();
        }

        public async Task<Dictionary<string, List<DeploymentState>>> GetLatestDeploymentsByEnvironmentAsync(string repository = null)
        {
            var deploymentsByEnv = new Dictionary<string, List<DeploymentState>>();
            var allDeployments = await GetDeploymentsFromRawEntitiesAsync();

            var filteredDeployments = string.IsNullOrEmpty(repository)
                ? allDeployments
                : allDeployments.Where(d => d.Repository.Equals(repository, StringComparison.OrdinalIgnoreCase));

            foreach (var deployment in filteredDeployments)
            {
                var env = deployment.Environment;
                if (string.IsNullOrEmpty(env)) continue;

                if (!deploymentsByEnv.ContainsKey(env))
                {
                    deploymentsByEnv[env] = new List<DeploymentState>();
                }
                deploymentsByEnv[env].Add(deployment);
            }

            // Get latest deployment for each environment
            foreach (var env in deploymentsByEnv.Keys.ToList())
            {
                deploymentsByEnv[env] = deploymentsByEnv[env]
                    .OrderByDescending(d => d.CreatedAt)
                    .Take(5)
                    .ToList();
            }

            return deploymentsByEnv;
        }

        #endregion

        #region Approval Workflow Methods

        public async Task<bool> UpdateDeploymentAsync(DeploymentState deployment)
        {
            try
            {
                _logger.LogInformation($"Updating deployment: {deployment.Repository}/{deployment.RowKey}");

                var rawEntity = RawDeploymentEntity.FromDeploymentState(deployment);
                rawEntity.Timestamp = DateTimeOffset.UtcNow;

                await _tableClient.UpdateEntityAsync(rawEntity, ETag.All);

                _logger.LogInformation($"Successfully updated deployment: {deployment.Repository}/{deployment.RowKey}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating deployment: {deployment.Repository}/{deployment.RowKey}");
                return false;
            }
        }

        public async Task<bool> UpdateDeploymentStatusAsync(string repository, string deploymentId, string newStatus, string approver, string comment = null)
        {
            try
            {
                _logger.LogInformation($"Updating deployment status: {repository}/{deploymentId} to {newStatus}");

                var deployment = await GetDeploymentAsync(repository, deploymentId);
                if (deployment == null)
                {
                    _logger.LogWarning($"Deployment not found for status update: {repository}/{deploymentId}");
                    return false;
                }

                // Parse existing status history
                var statusItems = new List<StatusHistoryItem>();
                if (!string.IsNullOrEmpty(deployment.StatusHistory))
                {
                    try
                    {
                        var existing = JsonSerializer.Deserialize<StatusHistoryItem[]>(deployment.StatusHistory);
                        if (existing != null)
                            statusItems.AddRange(existing);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Error parsing existing status history for {deploymentId}");
                    }
                }

                // Add new status update
                var statusUpdate = new StatusHistoryItem
                {
                    Creator = approver,
                    State = newStatus,
                    Description = comment ?? $"Status updated to {newStatus}",
                    Environment = deployment.Environment,
                    LogUrl = deployment.WorkflowRunUrl,
                    UpdatedAt = DateTimeOffset.UtcNow.ToString("O")
                };

                statusItems.Add(statusUpdate);

                // Update deployment
                deployment.CurrentStatus = newStatus;
                deployment.StatusHistory = JsonSerializer.Serialize(statusItems);
                deployment.LastStatusUpdate = DateTimeOffset.UtcNow.ToString("O");

                var success = await UpdateDeploymentAsync(deployment);
                if (success)
                {
                    _logger.LogInformation($"Successfully updated deployment status: {repository}/{deploymentId} to {newStatus}");
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating deployment status: {repository}/{deploymentId}");
                return false;
            }
        }

        public async Task<DeploymentState> GetRelatedDeploymentAsync(string repository, string workflowRunId, bool getProtectionRule = false)
        {
            try
            {
                var relatedRowKey = getProtectionRule
                    ? $"{workflowRunId}_protection_rule"
                    : $"{workflowRunId}_deployment";

                _logger.LogInformation($"Fetching related deployment: {repository}/{relatedRowKey}");

                return await GetDeploymentAsync(repository, relatedRowKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching related deployment: {repository}/{workflowRunId}");
                return null;
            }
        }

        public async Task<List<DeploymentState>> GetPendingApprovalsByEnvironmentAsync(string environment = null)
        {
            try
            {
                _logger.LogInformation($"Fetching pending approvals for environment: {environment ?? "all"}");

                var allDeployments = await GetDeploymentsFromRawEntitiesAsync();

                var pendingApprovals = allDeployments
                    .Where(d => IsProtectionRuleEntry(d.RowKey))
                    .Where(d => IsPendingStatus(d.StatusCategory) || IsPendingStatus(d.State))
                    .Where(d => string.IsNullOrEmpty(environment) || d.Environment.Equals(environment, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(d => d.CreatedAt)
                    .ToList();

                _logger.LogInformation($"Found {pendingApprovals.Count} pending approvals");
                return pendingApprovals;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching pending approvals for environment: {environment}");
                throw;
            }
        }

        public async Task<bool> ApproveDeploymentAsync(string repository, string deploymentId, string approver, string comment = null)
        {
            return await UpdateDeploymentStatusAsync(repository, deploymentId, "approved", approver, comment ?? "Deployment approved");
        }

        public async Task<bool> RejectDeploymentAsync(string repository, string deploymentId, string approver, string comment = null)
        {
            return await UpdateDeploymentStatusAsync(repository, deploymentId, "rejected", approver, comment ?? "Deployment rejected");
        }

        #endregion

        #region Helper Methods

        private bool IsProtectionRuleEntry(string rowKey)
        {
            return rowKey?.Contains("_protection_rule") == true;
        }

        private bool IsPendingStatus(string status)
        {
            if (string.IsNullOrEmpty(status)) return false;

            var lowerStatus = status.ToLower();
            return lowerStatus == "pending" ||
                   lowerStatus == "waiting" ||
                   lowerStatus.Contains("pending") ||
                   lowerStatus.Contains("waiting");
        }

        #endregion
    }
}