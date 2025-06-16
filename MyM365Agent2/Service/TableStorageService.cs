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
                filter: $"Environment eq '{environment}'"))
            {
                deployments.Add(deployment);
            }

            return deployments.OrderByDescending(d => d.CreatedAt).ToList();
        }

        public async Task<List<DeploymentState>> GetDeploymentsByStateAsync(string state)
        {
            var deployments = new List<DeploymentState>();

            await foreach (var deployment in _tableClient.QueryAsync<DeploymentState>(
                filter: $"State eq '{state}'"))
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
                { "total", 0 }
            };

            await foreach (var deployment in _tableClient.QueryAsync<DeploymentState>())
            {
                statistics["total"]++;
                if (statistics.ContainsKey(deployment.State.ToLower()))
                {
                    statistics[deployment.State.ToLower()]++;
                }
            }

            return statistics;
        }

        public async Task<Dictionary<string, Dictionary<string, int>>> GetEnvironmentStatisticsAsync()
        {
            var statistics = new Dictionary<string, Dictionary<string, int>>();

            await foreach (var deployment in _tableClient.QueryAsync<DeploymentState>())
            {
                if (!statistics.ContainsKey(deployment.Environment))
                {
                    statistics[deployment.Environment] = new Dictionary<string, int>
                    {
                        { "success", 0 },
                        { "failure", 0 },
                        { "error", 0 },
                        { "pending", 0 }
                    };
                }

                if (statistics[deployment.Environment].ContainsKey(deployment.State.ToLower()))
                {
                    statistics[deployment.Environment][deployment.State.ToLower()]++;
                }
            }

            return statistics;
        }

        public async Task<Dictionary<string, Dictionary<string, int>>> GetRepositoryStatisticsAsync()
        {
            var statistics = new Dictionary<string, Dictionary<string, int>>();

            await foreach (var deployment in _tableClient.QueryAsync<DeploymentState>())
            {
                if (!statistics.ContainsKey(deployment.PartitionKey))
                {
                    statistics[deployment.PartitionKey] = new Dictionary<string, int>
                    {
                        { "success", 0 },
                        { "failure", 0 },
                        { "error", 0 },
                        { "pending", 0 }
                    };
                }

                if (statistics[deployment.PartitionKey].ContainsKey(deployment.State.ToLower()))
                {
                    statistics[deployment.PartitionKey][deployment.State.ToLower()]++;
                }
            }

            return statistics;
        }
    }
}
