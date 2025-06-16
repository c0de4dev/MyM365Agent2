using MyM365Agent2.Common.Interface;
using MyM365Agent2.Common.Models;

namespace MyM365Agent2.Common.Connector
{
    public class AzureTableApprovalConnector : IApprovalConnector
    {
        private readonly TableClient _table;

        public AzureTableApprovalConnector(string connectionString, string tableName)
        {
            _table = new TableClient(connectionString, tableName);
            _table.CreateIfNotExists();
        }

        public async Task<IEnumerable<ApprovalItem>> GetPendingApprovalsAsync(string managerId)
        {
            var list = new List<ApprovalItem>();
            var filter = TableClient.CreateQueryFilter<TableEntity>(
                e => e.PartitionKey == managerId && e.GetInt32("Status") == 0);

            await foreach (var ent in _table.QueryAsync<TableEntity>(filter))
            {
                list.Add(new ApprovalItem
                {
                    Id = ent.RowKey,
                    Title = ent.GetString("Title"),
                    Description = ent.GetString("Description"),
                    RequesterId = ent.GetString("RequesterId"),
                    SubmittedUtc = ent.GetDateTime("SubmittedUtc") ?? DateTime.MinValue
                });
            }
            return list;
        }

        public async Task<bool> UpdateApprovalStatusAsync(string id, string managerId, int newStatus)
        {
            var resp = await _table.GetEntityAsync<TableEntity>(managerId, id);
            var ent = resp.Value;
            ent["Status"] = newStatus;
            await _table.UpdateEntityAsync(ent, ent.ETag, TableUpdateMode.Replace);
            return true;
        }
    }
}

