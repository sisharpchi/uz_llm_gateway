namespace UZLLM.Persistence;

public sealed class OrganizationEntity
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }

    public List<OrganizationMemberEntity> Members { get; } = [];

    public List<ProjectEntity> Projects { get; } = [];

    public List<OrganizationInvitationEntity> Invitations { get; } = [];
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

    public List<ProjectGrantEntity> ProjectGrants { get; } = [];
}

public sealed class OrganizationInvitationEntity
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Email { get; set; } = null!;
    public string Role { get; set; } = null!;
    public byte[] TokenHash { get; set; } = null!;
    public Guid InvitedByAccountId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public OrganizationEntity Organization { get; set; } = null!;
}

public sealed class ProjectGrantEntity
{
    public Guid OrganizationId { get; set; }
    public Guid AccountId { get; set; }
    public Guid ProjectId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public OrganizationMemberEntity Member { get; set; } = null!;
    public ProjectEntity Project { get; set; } = null!;
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

    public List<ProjectGrantEntity> Grants { get; } = [];
}
