using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Configuration;
using MyM365Agent2.Common.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MyM365Agent2.Services
{
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

        // New enhanced methods for repository and environment filtering
        Task<List<DeploymentState>> GetDeploymentsWithFiltersAsync(string repository = null, string environment = null, string status = null);
        Task<List<string>> GetAvailableRepositoriesAsync();
        Task<List<string>> GetAvailableEnvironmentsAsync();
        Task<List<string>> GetAvailableEnvironmentsForRepositoryAsync(string repository);
        Task<Dictionary<string, int>> GetDeploymentStatisticsForRepoAsync(string repository);
        Task<Dictionary<string, int>> GetDeploymentStatisticsForEnvironmentAsync(string environment);
        Task<Dictionary<string, int>> GetDeploymentStatisticsWithFiltersAsync(string repository = null, string environment = null);
        Task<List<DeploymentState>> GetWorkflowRunsForRepositoryAsync(string repository, int limit = 50);
        Task<Dictionary<string, List<DeploymentState>>> GetLatestDeploymentsByEnvironmentAsync(string repository = null);
    }

    public class AzureTableStorageService : IAzureTableStorageService
    {
        private readonly TableClient _tableClient;
        private readonly IConfiguration _configuration;

        public AzureTableStorageService(IConfiguration configuration)
        {
            _configuration = configuration;
            var connectionString = _configuration["AzureTableStorage:ConnectionString"];
            var tableName = _configuration["AzureTableStorage:DeploymentTableName"] ?? "GitHubDeployments";

            var serviceClient = new TableServiceClient(connectionString);
            _tableClient = serviceClient.GetTableClient(tableName);
            _tableClient.CreateIfNotExists();
        }

        #region Existing Methods (maintained for compatibility)

        public async Task<List<DeploymentState>> GetDeploymentsAsync(string repository = null)
        {
            var deployments = new List<DeploymentState>();

            if (string.IsNullOrEmpty(repository))
            {
                await foreach (var deployment in _tableClient.QueryAsync<DeploymentState>())
                {
                    deployments.Add(deployment);
                }
            }
            else
            {
                await foreach (var deployment in _tableClient.QueryAsync<DeploymentState>(
                    filter: $"PartitionKey eq '{repository}'"))
                {
                    deployments.Add(deployment);
                }
            }

            return deployments.OrderByDescending(d => d.CreatedAt).ToList();
        }

        public async Task<DeploymentState> GetDeploymentAsync(string repository, string deploymentId)
        {
            try
            {
                var response = await _tableClient.GetEntityAsync<DeploymentState>(repository, deploymentId);
                return response.Value;
            }
            catch
            {
                return null;
            }
        }

        public async Task<List<DeploymentState>> GetDeploymentsByEnvironmentAsync(string environment)
        {
            var deployments = new List<DeploymentState>();

            await foreach (var deployment in _tableClient.QueryAsync<DeploymentState>(
                filter: $"environment eq '{environment}'"))
            {
                deployments.Add(deployment);
            }

            return deployments.OrderByDescending(d => d.CreatedAt).ToList();
        }

        public async Task<List<DeploymentState>> GetDeploymentsByStateAsync(string state)
        {
            var deployments = new List<DeploymentState>();

            string filterExpression = state.ToLower() switch
            {
                "success" => "currentStatus eq 'success' or currentStatus eq 'completed' or currentStatus eq 'approved' or runStatus eq 'success' or runStatus eq 'completed' or status eq 'approved'",
                "failure" => "currentStatus eq 'failure' or currentStatus eq 'failed' or currentStatus eq 'rejected' or runStatus eq 'failure' or runStatus eq 'failed' or status eq 'rejected'",
                "pending" => "currentStatus eq 'pending' or currentStatus eq 'waiting' or currentStatus eq 'pending_approval' or currentStatus eq 'queued' or runStatus eq 'pending' or runStatus eq 'queued' or status eq 'pending' or status eq 'waiting'",
                "in_progress" => "currentStatus eq 'in_progress' or currentStatus eq 'running' or runStatus eq 'in_progress' or runStatus eq 'running'",
                "cancelled" => "currentStatus eq 'cancelled' or currentStatus eq 'canceled' or runStatus eq 'cancelled' or runStatus eq 'canceled'",
                _ => $"currentStatus eq '{state}' or runStatus eq '{state}' or status eq '{state}'"
            };

            await foreach (var deployment in _tableClient.QueryAsync<DeploymentState>(filter: filterExpression))
            {
                deployments.Add(deployment);
            }

            return deployments.OrderByDescending(d => d.CreatedAt).ToList();
        }

        public async Task<List<DeploymentState>> GetDeploymentsByCreatorAsync(string creator)
        {
            var deployments = new List<DeploymentState>();

            await foreach (var deployment in _tableClient.QueryAsync<DeploymentState>(
                filter: $"creator eq '{creator}' or requestor eq '{creator}'"))
            {
                deployments.Add(deployment);
            }

            return deployments.OrderByDescending(d => d.CreatedAt).ToList();
        }

        public async Task<List<DeploymentState>> GetPendingApprovalsAsync()
        {
            var deployments = new List<DeploymentState>();

            await foreach (var deployment in _tableClient.QueryAsync<DeploymentState>(
                filter: "currentStatus eq 'pending_approval' or status eq 'pending' or status eq 'waiting'"))
            {
                deployments.Add(deployment);
            }

            return deployments.OrderBy(d => d.RunStartedAtDateTime ?? d.CreatedAt).ToList();
        }

        public async Task<Dictionary<string, int>> GetDeploymentStatisticsAsync()
        {
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

            await foreach (var deployment in _tableClient.QueryAsync<DeploymentState>())
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

            return statistics;
        }

        public async Task<Dictionary<string, Dictionary<string, int>>> GetEnvironmentStatisticsAsync()
        {
            var statistics = new Dictionary<string, Dictionary<string, int>>();

            await foreach (var deployment in _tableClient.QueryAsync<DeploymentState>())
            {
                var env = deployment.Environment;
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

            return statistics;
        }

        public async Task<Dictionary<string, Dictionary<string, int>>> GetRepositoryStatisticsAsync()
        {
            var statistics = new Dictionary<string, Dictionary<string, int>>();

            await foreach (var deployment in _tableClient.QueryAsync<DeploymentState>())
            {
                var repo = deployment.Repository;
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

            return statistics;
        }

        public async Task<Dictionary<string, Dictionary<string, int>>> GetCreatorStatisticsAsync()
        {
            var statistics = new Dictionary<string, Dictionary<string, int>>();

            await foreach (var deployment in _tableClient.QueryAsync<DeploymentState>())
            {
                var creator = deployment.CreatorLogin;
                if (!statistics.ContainsKey(creator))
                {
                    statistics[creator] = new Dictionary<string, int>
                    {
                        { "success", 0 },
                        { "failure", 0 },
                        { "pending", 0 },
                        { "in_progress", 0 },
                        { "cancelled", 0 },
                        { "total", 0 }
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
            var deployments = new List<DeploymentState>();

            await foreach (var deployment in _tableClient.QueryAsync<DeploymentState>())
            {
                deployments.Add(deployment);
            }

            return deployments
                .OrderByDescending(d => d.CreatedAt)
                .Take(count)
                .ToList();
        }

        #endregion

        #region New Enhanced Methods

        public async Task<List<DeploymentState>> GetDeploymentsWithFiltersAsync(string repository = null, string environment = null, string status = null)
        {
            var deployments = new List<DeploymentState>();
            var filters = new List<string>();

            if (!string.IsNullOrEmpty(repository))
            {
                filters.Add($"PartitionKey eq '{repository}'");
            }

            if (!string.IsNullOrEmpty(environment))
            {
                filters.Add($"environment eq '{environment}'");
            }

            var filterExpression = filters.Any() ? string.Join(" and ", filters) : null;

            await foreach (var deployment in _tableClient.QueryAsync<DeploymentState>(filter: filterExpression))
            {
                // Apply status filter in memory for complex status logic
                if (string.IsNullOrEmpty(status) || deployment.StatusCategory == status)
                {
                    deployments.Add(deployment);
                }
            }

            return deployments.OrderByDescending(d => d.CreatedAt).ToList();
        }

        public async Task<List<string>> GetAvailableRepositoriesAsync()
        {
            var repositories = new HashSet<string>();

            await foreach (var deployment in _tableClient.QueryAsync<DeploymentState>())
            {
                if (!string.IsNullOrEmpty(deployment.Repository))
                {
                    repositories.Add(deployment.Repository);
                }
            }

            return repositories.OrderBy(r => r).ToList();
        }

        public async Task<List<string>> GetAvailableEnvironmentsAsync()
        {
            var environments = new HashSet<string>();

            await foreach (var deployment in _tableClient.QueryAsync<DeploymentState>())
            {
                if (!string.IsNullOrEmpty(deployment.Environment))
                {
                    environments.Add(deployment.Environment);
                }
            }

            return environments.OrderBy(e => e).ToList();
        }

        public async Task<List<string>> GetAvailableEnvironmentsForRepositoryAsync(string repository)
        {
            var environments = new HashSet<string>();

            await foreach (var deployment in _tableClient.QueryAsync<DeploymentState>(
                filter: $"PartitionKey eq '{repository}'"))
            {
                if (!string.IsNullOrEmpty(deployment.Environment))
                {
                    environments.Add(deployment.Environment);
                }
            }

            return environments.OrderBy(e => e).ToList();
        }

        public async Task<Dictionary<string, int>> GetDeploymentStatisticsForRepoAsync(string repository)
        {
            var statistics = new Dictionary<string, int>
            {
                { "success", 0 },
                { "failure", 0 },
                { "pending", 0 },
                { "in_progress", 0 },
                { "cancelled", 0 },
                { "total", 0 }
            };

            await foreach (var deployment in _tableClient.QueryAsync<DeploymentState>(
                filter: $"PartitionKey eq '{repository}'"))
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
                { "success", 0 },
                { "failure", 0 },
                { "pending", 0 },
                { "in_progress", 0 },
                { "cancelled", 0 },
                { "total", 0 }
            };

            await foreach (var deployment in _tableClient.QueryAsync<DeploymentState>(
                filter: $"environment eq '{environment}'"))
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
                { "success", 0 },
                { "failure", 0 },
                { "pending", 0 },
                { "in_progress", 0 },
                { "cancelled", 0 },
                { "total", 0 }
            };

            var filters = new List<string>();

            if (!string.IsNullOrEmpty(repository))
            {
                filters.Add($"PartitionKey eq '{repository}'");
            }

            if (!string.IsNullOrEmpty(environment))
            {
                filters.Add($"environment eq '{environment}'");
            }

            var filterExpression = filters.Any() ? string.Join(" and ", filters) : null;

            await foreach (var deployment in _tableClient.QueryAsync<DeploymentState>(filter: filterExpression))
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
            var deployments = new List<DeploymentState>();

            await foreach (var deployment in _tableClient.QueryAsync<DeploymentState>(
                filter: $"PartitionKey eq '{repository}'"))
            {
                deployments.Add(deployment);
            }

            return deployments
                .OrderByDescending(d => d.CreatedAt)
                .Take(limit)
                .ToList();
        }

        public async Task<Dictionary<string, List<DeploymentState>>> GetLatestDeploymentsByEnvironmentAsync(string repository = null)
        {
            var deploymentsByEnv = new Dictionary<string, List<DeploymentState>>();
            var filter = !string.IsNullOrEmpty(repository) ? $"PartitionKey eq '{repository}'" : null;

            await foreach (var deployment in _tableClient.QueryAsync<DeploymentState>(filter: filter))
            {
                var env = deployment.Environment;
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
    }
}