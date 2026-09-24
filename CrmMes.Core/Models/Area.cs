namespace CrmMes.Core.Models;

public class Area
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<User> Users { get; set; } = new List<User>();

    /// <summary>Which physical site (seconda sede) this area belongs to. Null = not yet assigned, not an
    /// error — most installations have a single site and never need to set this.</summary>
    public Guid? SiteId { get; set; }
    public Site? Site { get; set; }
}
