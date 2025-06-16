namespace MyM365Agent2.Common.Models
{
    public class ApprovalItem
    {
        public string Id { get; set; } = default!;
        public string Title { get; set; } = default!;
        public string Description { get; set; } = default!;
        public string RequesterId { get; set; } = default!;
        public DateTime SubmittedUtc { get; set; }
    }
}
}
