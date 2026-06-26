using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BookDB.Data.Sqlite;

/// <summary>
/// Stores every <see cref="bool"/>/<c>bool?</c> property as an integer, since SQLite has no native
/// boolean type. This was a global convention on the shared <c>BookDbContext</c>; it lives in the SQLite
/// provider so the context stays engine-neutral (PostgreSQL uses the native <c>boolean</c> type).
/// </summary>
internal sealed class SqliteModelCustomizer : RelationalModelCustomizer
{
    public SqliteModelCustomizer(ModelCustomizerDependencies dependencies)
        : base(dependencies)
    {
    }

    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        var converter = new BoolToZeroOneConverter<int>();
        foreach (var property in modelBuilder.Model.GetEntityTypes()
                     .SelectMany(entityType => entityType.GetProperties())
                     .Where(property => property.ClrType == typeof(bool) || property.ClrType == typeof(bool?)))
        {
            property.SetValueConverter(converter);
        }
    }
}
