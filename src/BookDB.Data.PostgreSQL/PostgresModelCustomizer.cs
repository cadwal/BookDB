using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BookDB.Data.PostgreSQL;

/// <summary>
/// Pins every <see cref="DateTime"/>/<c>DateTime?</c> property to <c>timestamp without time zone</c> and
/// normalizes <see cref="DateTimeKind"/> at the boundary. Npgsql's default maps <see cref="DateTime"/> to
/// <c>timestamptz</c>, which would force a session-timezone assignment cast into our <c>timestamp</c> columns:
/// harmless on a UTC server, but on a non-UTC server (or between two clients in different zones — the
/// multi-client case) it shifts the stored wall-clock and corrupts the instant. Storing the UTC wall-clock
/// verbatim and re-tagging it <see cref="DateTimeKind.Utc"/> on read makes the round-trip timezone-independent.
/// </summary>
internal sealed class PostgresModelCustomizer : RelationalModelCustomizer
{
    // Entities always carry UTC (DateTime.UtcNow, or normalized during migration copy). Strip the Kind so
    // Npgsql accepts the value for a `timestamp without time zone` parameter, then restore it on read.
    private static readonly ValueConverter<DateTime, DateTime> UtcConverter = new(
        toDb => DateTime.SpecifyKind(toDb, DateTimeKind.Unspecified),
        fromDb => DateTime.SpecifyKind(fromDb, DateTimeKind.Utc));

    public PostgresModelCustomizer(ModelCustomizerDependencies dependencies)
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
            property.SetColumnType("timestamp without time zone");
            property.SetValueConverter(UtcConverter);
        }
    }
}
