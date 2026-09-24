using Microsoft.EntityFrameworkCore;

namespace UZLLM.Persistence;

public sealed class FoundationDbContext(DbContextOptions<FoundationDbContext> options) : DbContext(options);
