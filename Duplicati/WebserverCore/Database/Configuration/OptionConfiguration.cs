using Duplicati.WebserverCore.Database.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Duplicati.WebserverCore.Database.Configuration;

public class OptionConfiguration : IEntityTypeConfiguration<Option>
{
    public void Configure(EntityTypeBuilder<Option> builder)
    {
        builder.ToTable(nameof(Option))
            .HasKey(o => new { o.BackupID, o.Name });
    }
}