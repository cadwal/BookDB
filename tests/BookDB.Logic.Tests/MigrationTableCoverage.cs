using System;
using System.Collections.Generic;
using System.Linq;
using BookDB.Data.DbContexts;
using BookDB.Models;
using Microsoft.EntityFrameworkCore;

namespace BookDB.Logic.Tests;

/// <summary>
/// Maps every <see cref="BookDbContext"/> DbSet (minus the named exceptions) to its
/// <see cref="MigrationTable"/> member, so the move and restore coverage guards fail when a new
/// entity set is added without being wired into those engines — the same guard the CSV backup
/// archive already has.
/// </summary>
internal static class MigrationTableCoverage
{
    public static IReadOnlyList<MigrationTable> TablesForEveryEntitySetExcept(params string[] dbSetPropertyNames)
        => typeof(BookDbContext).GetProperties()
            .Where(p => p.PropertyType.IsGenericType
                && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>)
                && !dbSetPropertyNames.Contains(p.Name))
            .Select(p => p.PropertyType.GetGenericArguments()[0].Name)
            .Select(name => Enum.TryParse<MigrationTable>(name, out var table)
                ? table
                : throw new InvalidOperationException(
                    $"Entity '{name}' has no MigrationTable member — add one and wire it into the move and restore engines."))
            .ToList();
}
