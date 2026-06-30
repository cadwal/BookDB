using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BookDB.Data.MySql;

/// <summary>
/// Pins every <see cref="DateTime"/>/<c>DateTime?</c> property to <c>datetime(6)</c> and normalizes
/// <see cref="DateTimeKind"/> at the boundary. MySQL's <c>datetime</c> carries no timezone; MySqlConnector
/// returns it with <see cref="DateTimeKind.Unspecified"/>, so storing the UTC wall-clock verbatim and
/// re-tagging it <see cref="DateTimeKind.Utc"/> on read makes the round-trip timezone-independent — the same
/// instant survives between two clients in different zones (the multi-client case).
/// </summary>
internal sealed class MySqlModelCustomizer : RelationalModelCustomizer
{
    // Entities always carry UTC (DateTime.UtcNow, or normalized during migration copy). Strip the Kind on write,
    // restore it on read so the stored wall-clock is interpreted as UTC regardless of the server's timezone.
    private static readonly ValueConverter<DateTime, DateTime> UtcConverter = new(
        toDb => DateTime.SpecifyKind(toDb, DateTimeKind.Unspecified),
        fromDb => DateTime.SpecifyKind(fromDb, DateTimeKind.Utc));

    public MySqlModelCustomizer(ModelCustomizerDependencies dependencies)
        : base(dependencies)
    {
    }

    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        foreach (var property in modelBuilder.Model.GetEntityTypes()
                     .SelectMany(entityType => entityType.GetProperties())
                     .Where(property => property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?)))
        {
            property.SetColumnType("datetime(6)");
            property.SetValueConverter(UtcConverter);
        }
    }
}
