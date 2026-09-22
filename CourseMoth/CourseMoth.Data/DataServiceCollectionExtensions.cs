// SPDX-License-Identifier: AGPL-3.0-or-later

using CourseMoth.Core.Abstractions;
using CourseMoth.Data.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace CourseMoth.Data;

/// <summary>
/// Registers the storage layer.
///
/// <para>
/// <b>Every registration here is a singleton, and that is the whole point.</b> The repositories
/// are stateless — the state they work on is in the database — and they all share one
/// <see cref="CourseMothDatabase"/>, which in turn owns one
/// <see cref="SQLite.SQLiteAsyncConnection"/>. Registering a repository as transient would not
/// open a second connection (the connection is resolved from the singleton database), but it
/// would allocate a pointless object per resolution, and more importantly it would leave a future
/// reader with the impression that a second connection was acceptable. ServerApiSpec §2 says it
/// is not: the app's own server reads this same database and must not become a second writer.
/// </para>
///
/// <para>
/// <b>This method does not take a path.</b> The database location is platform knowledge —
/// <c>FileSystem.AppDataDirectory</c> on a MAUI head, a temp directory in the harness, something
/// else on a server — and this assembly has no platform reference. The composition root calls
/// <see cref="AddCourseMothData(IServiceCollection, string)"/> with the path it resolved.
/// </para>
/// </summary>
public static class DataServiceCollectionExtensions
{
    /// <summary>
    /// Registers the database, the repositories and the unit of work.
    /// </summary>
    /// <param name="services">The container. The composition root's, not one this layer creates.</param>
    /// <param name="databasePath">
    /// Full path to the SQLite file. The directory is created on first use if it is missing.
    /// </param>
    public static IServiceCollection AddCourseMothData(this IServiceCollection services, string databasePath)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        // The one connection. Registered by its concrete type as well as by instance so that a
        // future migration or a diagnostic can reach the path and the schema version without
        // going through a repository.
        services.AddSingleton(new CourseMothDatabase(databasePath));

        // One per interface in CourseMoth.Core.Abstractions.Repositories — the implement-the-
        // contract-and-nothing-else rule from SystemMap §5.
        services.AddSingleton<ICourseRepository, CourseRepository>();
        services.AddSingleton<ICourseModuleRepository, CourseModuleRepository>();
        services.AddSingleton<ILessonRepository, LessonRepository>();
        services.AddSingleton<IWatchStateRepository, WatchStateRepository>();
        services.AddSingleton<ITaskRepository, TaskRepository>();
        services.AddSingleton<IActivityRepository, ActivityRepository>();
        services.AddSingleton<ICategoryRepository, CategoryRepository>();
        services.AddSingleton<ILibrarySourceRepository, LibrarySourceRepository>();

        services.AddSingleton<IUnitOfWork, UnitOfWork>();

        return services;
    }
}
