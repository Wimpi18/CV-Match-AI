using System;
using System.Collections.Generic;

namespace CvMatchApi.Models
{
    public class User
    {
        public string Id { get; set; } = string.Empty; // Google User Id (sub)
        public string Email { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation
        public ICollection<UsageLog> UsageLogs { get; set; } = new List<UsageLog>();
    }
}
