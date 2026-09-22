// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Reflection;
using CourseMoth.Core.Abstractions;

namespace CourseMoth.Core.Tests.Fakes;

/// <summary>
/// Builds the services under test from the fakes, without knowing their constructor signatures.
///
/// Why reflection. The Core services are being written in parallel with this suite against a
/// frozen interface contract; constructors are the one part of a service that the contract
/// does not pin down. Resolving by parameter type keeps these tests compiling and running
/// against any constructor that takes the declared abstractions, instead of failing the whole
/// build because a service happens to want <c>(a, b)</c> rather than <c>(b, a)</c>.
/// </summary>
public sealed class TestServices
{
    private readonly Dictionary<Type, object> _replacements;

    public TestServices(
        InMemoryRepositories repositories,
        IClock clock,
        IStableKeyGenerator stableKeys,
        params (Type Service, object Instance)[] replacements)
    {
        Repositories = repositories;
        Clock = clock;
        StableKeys = stableKeys;

        _replacements = new Dictionary<Type, object>
        {
            [typeof(IClock)] = clock,
            [typeof(IStableKeyGenerator)] = stableKeys,
        };

        foreach (var (service, instance) in replacements)
        {
            _replacements[service] = instance;
        }
    }

    public InMemoryRepositories Repositories { get; }

    public IClock Clock { get; }

    public IStableKeyGenerator StableKeys { get; }

    /// <summary>Resolves a concrete Core service, filling its dependencies from the fakes.</summary>
    public T Get<T>() => (T)Resolve(typeof(T));

    private object Resolve(Type serviceType)
    {
        if (_replacements.TryGetValue(serviceType, out var direct))
        {
            return direct;
        }

        var implementations = serviceType.Assembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false, IsPublic: true })
            .Where(t => serviceType.IsAssignableFrom(t))
            .ToList();

        if (implementations.Count == 0)
        {
            throw new InvalidOperationException(
                $"No implementation of {serviceType.Name} was found in {serviceType.Assembly.GetName().Name}. " +
                "The Core services may not be implemented yet.");
        }

        if (implementations.Count > 1)
        {
            throw new InvalidOperationException(
                $"More than one implementation of {serviceType.Name}: " +
                string.Join(", ", implementations.Select(t => t.Name)));
        }

        var implementation = implementations[0];
        var constructor = implementation.GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"{implementation.Name} has no public constructor.");

        var arguments = constructor.GetParameters()
            .Select(p => ResolveDependency(p.ParameterType))
            .ToArray();

        return Activator.CreateInstance(implementation, arguments)
            ?? throw new InvalidOperationException($"Could not construct {implementation.Name}.");
    }

    private object ResolveDependency(Type dependencyType)
    {
        if (_replacements.TryGetValue(dependencyType, out var replacement))
        {
            return replacement;
        }

        // The repositories are the only shared state; the same instance must be handed to
        // every service, so this lookup is a switch rather than a fresh instance per call.
        var repository = Repositories.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p => dependencyType.IsAssignableFrom(p.PropertyType));

        if (repository is not null)
        {
            return repository.GetValue(Repositories)!;
        }

        if (dependencyType == typeof(DayBoundaryOptions) || dependencyType == typeof(StreakContext))
        {
            return DefaultFor(dependencyType);
        }

        return Resolve(dependencyType);
    }

    private static object DefaultFor(Type optionsType)
        => optionsType == typeof(StreakContext)
            ? new StreakContext(new DayBoundaryOptions(), Guid.Empty)
            : new DayBoundaryOptions();
}
