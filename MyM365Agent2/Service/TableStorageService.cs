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
        Task<Dictionary<string, int>> GetDeploymentStatisticsAsync();
        Task<Dictionary<string, Dictionary<string, int>>> GetEnvironmentStatisticsAsync();
        Task<Dictionary<string, Dictionary<string, int>>> GetRepositoryStatisticsAsync();
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

            // Map UI state to actual runStatus values
            string filterExpression = state.ToLower() switch
            {
                "success" => "runStatus eq 'success' or runStatus eq 'completed'",
                "failure" => "runStatus eq 'failure' or runStatus eq 'failed'",
                "pending" => "runStatus eq 'pending' or runStatus eq 'queued'",
                _ => $"runStatus eq '{state}'"
            };

            await foreach (var deployment in _tableClient.QueryAsync<DeploymentState>(filter: filterExpression))
            {
                deployments.Add(deployment);
            }

            return deployments.OrderByDescending(d => d.CreatedAt).ToList();
        }

        public async Task<Dictionary<string, int>> GetDeploymentStatisticsAsync()
        {
            var statistics = new Dictionary<string, int>
            {
                { "success", 0 },
                { "failure", 0 },
                { "error", 0 },
                { "pending", 0 },
                { "in_progress", 0 },
                { "queued", 0 },
                { "total", 0 }
            };

            await foreach (var deployment in _tableClient.QueryAsync<DeploymentState>())
            {
                statistics["total"]++;
                var state = deployment.runStatus?.ToLower() ?? "unknown";

                // Map states to our standard categories
                if (state == "completed" || state == "success")
                {
                    statistics["success"]++;
                }
                else if (state == "failed" || state == "failure")
                {
                    statistics["failure"]++;
                }
                else if (state == "queued")
                {
                    statistics["pending"]++;
                    statistics["queued"]++;
                }
                else if (state == "in_progress")
                {
                    statistics["in_progress"]++;
                }
                else if (statistics.ContainsKey(state))
                {
                    statistics[state]++;
                }
            }

            return statistics;
        }

        public async Task<Dictionary<string, Dictionary<string, int>>> GetEnvironmentStatisticsAsync()
        {
            var statistics = new Dictionary<string, Dictionary<string, int>>();

            await foreach (var deployment in _tableClient.QueryAsync<DeploymentState>())
            {
                var env = deployment.environment ?? "Unknown";
                if (!statistics.ContainsKey(env))
                {
                    statistics[env] = new Dictionary<string, int>
                    {
                        { "success", 0 },
                        { "failure", 0 },
                        { "error", 0 },
                        { "pending", 0 },
                        { "in_progress", 0 }
                    };
                }

                var state = deployment.runStatus?.ToLower() ?? "unknown";

                // Map states to our standard categories
                if (state == "completed" || state == "success")
                {
                    statistics[env]["success"]++;
                }
                else if (state == "failed" || state == "failure")
                {
                    statistics[env]["failure"]++;
                }
                else if (state == "queued")
                {
                    statistics[env]["pending"]++;
                }
                else if (state == "in_progress")
                {
                    statistics[env]["in_progress"]++;
                }
                else if (statistics[env].ContainsKey(state))
                {
                    statistics[env][state]++;
                }
            }

            return statistics;
        }

        public async Task<Dictionary<string, Dictionary<string, int>>> GetRepositoryStatisticsAsync()
        {
            var statistics = new Dictionary<string, Dictionary<string, int>>();

            await foreach (var deployment in _tableClient.QueryAsync<DeploymentState>())
            {
                var repo = deployment.PartitionKey ?? "Unknown";
                if (!statistics.ContainsKey(repo))
                {
                    statistics[repo] = new Dictionary<string, int>
                    {
                        { "success", 0 },
                        { "failure", 0 },
                        { "error", 0 },
                        { "pending", 0 },
                        { "in_progress", 0 }
                    };
                }

                var state = deployment.runStatus?.ToLower() ?? "unknown";

                // Map states to our standard categories
                if (state == "completed" || state == "success")
                {
                    statistics[repo]["success"]++;
                }
                else if (state == "failed" || state == "failure")
                {
                    statistics[repo]["failure"]++;
                }
                else if (state == "queued")
                {
                    statistics[repo]["pending"]++;
                }
                else if (state == "in_progress")
                {
                    statistics[repo]["in_progress"]++;
                }
                else if (statistics[repo].ContainsKey(state))
                {
                    statistics[repo][state]++;
                }
            }

            return statistics;
        }
    }
}
