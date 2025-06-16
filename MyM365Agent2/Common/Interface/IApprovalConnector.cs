using MyM365Agent2.Common.Models;

namespace MyM365Agent2.Common.Interface
{
    public interface IApprovalConnector
    {
        Task<IEnumerable<ApprovalItem>> GetPendingApprovalsAsync(string managerId);
        Task<bool> UpdateApprovalStatusAsync(string id, string managerId, int newStatus);
    }
}
