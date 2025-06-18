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

            // Build filter expression based on the state category
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

            return deployments.OrderBy(d => d.requestedAt ?? d.CreatedAt).ToList();
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

                // Special handling for approval workflows
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
    }
}