namespace UZLLM.Persistence;

public sealed class OrganizationEntity
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }

    public List<OrganizationMemberEntity> Members { get; } = [];

    public List<ProjectEntity> Projects { get; } = [];
}

public sealed class OrganizationMemberEntity
{
    public Guid OrganizationId { get; set; }

    public Guid AccountId { get; set; }

    public string Role { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }

    public OrganizationEntity Organization { get; set; } = null!;

    public IdentityAccountEntity Account { get; set; } = null!;
}

public sealed class ProjectEntity
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public string Name { get; set; } = null!;

    public string Status { get; set; } = null!;

    public string SettingsJson { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }

    public OrganizationEntity Organization { get; set; } = null!;
}
