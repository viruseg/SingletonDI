using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using SingletonDI.Generator.Emitters;
using SingletonDI.Generator.Helpers;
using SingletonDI.Generator.Models;
using SingletonDI.Generator.Validators;

namespace SingletonDI.Generator;

/// <summary>
/// Incremental Source Generator for SingletonDI dependency injection.
/// </summary>
[Generator(LanguageNames.CSharp)]
public partial class SingletonDIGenerator : IIncrementalGenerator
{
    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var generatorOptions = context.AnalyzerConfigOptionsProvider
            .Select(static (optionsProvider, _) => ReadGeneratorOptions(optionsProvider));

        var providerCandidates = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "SingletonDI.Attributes.SingletonDIProvideAttribute",
                predicate: static (node, _) => node is TypeDeclarationSyntax,
                transform: static (context, token) =>
                {
                    var typeDeclaration = (TypeDeclarationSyntax)context.TargetNode;
                    var typeSymbol = context.SemanticModel.GetDeclaredSymbol(typeDeclaration, token) as INamedTypeSymbol;
                    if (typeSymbol is null)
                    {
                        return new ProviderCandidate(
                            null,
                            ImmutableArray<Diagnostic>.Empty,
                            null);
                    }

                    var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
                    var model = ProviderValidator.Validate(
                        typeDeclaration,
                        typeSymbol,
                        context.SemanticModel.Compilation,
                        ImmutableHashSet<string>.Empty,
                        diagnostics.Add);
                    return new ProviderCandidate(
                        model,
                        diagnostics.ToImmutable(),
                        ServiceTypeIdentity.FromSymbol(typeSymbol));
                });

        var consumerDeclarations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "SingletonDI.Attributes.SingletonDIConsumeAttribute",
                predicate: static (node, _) => node is TypeDeclarationSyntax,
                transform: static (context, token) =>
                {
                    var typeDeclaration = (TypeDeclarationSyntax)context.TargetNode;
                    return context.SemanticModel.GetDeclaredSymbol(typeDeclaration, token) == null
                        ? null
                        : typeDeclaration;
                })
            .Where(static typeDeclaration => typeDeclaration is not null)
            .Select(static (typeDeclaration, _) => typeDeclaration!);

        var providerInputs = providerCandidates
            .Collect()
            .Combine(generatorOptions)
            .WithTrackingName("ProviderModuleOutput");
        context.RegisterSourceOutput(providerInputs, (sourceProductionContext, input) =>
        {
            var (candidates, options) = input;
            ReportProviderCandidateDiagnostics(candidates, sourceProductionContext.ReportDiagnostic);
            if (options.IsCompositionRoot)
            {
                return;
            }

            EmitLocalProviderModule(candidates, sourceProductionContext);
        });

        var compositionInputs = providerCandidates
            .Collect()
            .Combine(consumerDeclarations.Collect())
            .Combine(context.CompilationProvider)
            .Combine(generatorOptions)
            .WithTrackingName("CompositionRootOutput");
        context.RegisterSourceOutput(compositionInputs, (sourceProductionContext, input) =>
        {
            var (((candidates, consumerDeclarationsForRoot), compilation), options) = input;
            if (!options.IsCompositionRoot)
            {
                return;
            }

            EmitCompositionRoot(
                candidates,
                consumerDeclarationsForRoot,
                compilation,
                sourceProductionContext);
        });

        var consumerInputs = consumerDeclarations
            .Collect()
            .Combine(context.CompilationProvider)
            .Combine(providerCandidates.Collect())
            .Combine(generatorOptions)
            .WithTrackingName("ConsumerOutput");
        context.RegisterSourceOutput(consumerInputs, (sourceProductionContext, input) =>
        {
            var (((consumerDeclarationsForOutput, compilation), providerCandidatesForOutput), options) = input;
            if (consumerDeclarationsForOutput.IsDefault || consumerDeclarationsForOutput.IsEmpty)
            {
                return;
            }

            EmitConsumers(
                consumerDeclarationsForOutput,
                compilation,
                providerCandidatesForOutput,
                options,
                sourceProductionContext);
        });
    }

    private static void EmitLocalProviderModule(
        ImmutableArray<ProviderCandidate> candidates,
        SourceProductionContext sourceProductionContext)
    {
        var providerModels = GetProviderModels(candidates);
        if (providerModels.Count == 0)
        {
            return;
        }

        var serviceTypeMapResult = ServiceTypeResolver.BuildServiceTypeMap(providerModels);
        var hasInvalidGraph = false;
        foreach (var conflict in serviceTypeMapResult.Conflicts)
        {
            hasInvalidGraph = true;
            ReportServiceTypeConflict(
                conflict,
                providerModels,
                sourceProductionContext.ReportDiagnostic);
        }

        if (hasInvalidGraph ||
            !ValidateProviderPropertyNames(providerModels, sourceProductionContext.ReportDiagnostic))
        {
            return;
        }

        var sortResult = TopologicalSorter.SortByLevels(
            providerModels.ToImmutableArray(),
            serviceTypeMapResult.IdentityMap);
        if (sortResult.HasCycle)
        {
            sourceProductionContext.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.CircularDependency,
                Location.None,
                string.Join(" -> ", sortResult.Cycle)));
            return;
        }

        var moduleSource = ProviderModuleEmitter.Generate(
            providerModels.ToImmutableArray(),
            ImmutableArray<ProviderAssemblyModel>.Empty,
            isCompositionRoot: false);
        AddProviderModuleSource(sourceProductionContext, moduleSource);
    }

    private static void EmitCompositionRoot(
        ImmutableArray<ProviderCandidate> candidates,
        ImmutableArray<TypeDeclarationSyntax> consumerDeclarations,
        Compilation compilation,
        SourceProductionContext sourceProductionContext)
    {
        var localProviders = GetProviderModels(candidates);
        var externalProviders = ProviderSymbolCollector.CollectReferencedProviders(
            compilation,
            sourceProductionContext.CancellationToken,
            sourceProductionContext.ReportDiagnostic,
            out var externalProviderAssemblies);
        var referencedConsumerDependencies =
            ReferencedConsumerCollector.CollectReferencedConsumerDependencyIdentities(
                compilation,
                sourceProductionContext.CancellationToken);
        var allProviders = localProviders
            .Concat(externalProviders)
            .OrderBy(provider => provider.FullyQualifiedName, StringComparer.Ordinal)
            .ThenBy(provider => provider.AssemblyIdentity, StringComparer.Ordinal)
            .ToList();

        foreach (var providerAssembly in externalProviderAssemblies)
        {
            if (!providerAssembly.HasModuleMarker)
            {
                sourceProductionContext.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.MissingProviderModuleMarker,
                    Location.None,
                    providerAssembly.AssemblyIdentity));
            }
            else if (!providerAssembly.HasBootstrapMethod)
            {
                sourceProductionContext.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.MissingProviderBootstrap,
                    Location.None,
                    providerAssembly.AssemblyIdentity));
            }
        }

        var hasInvalidGraph = false;
        var serviceTypeMapResult = ServiceTypeResolver.BuildServiceTypeMap(allProviders);
        var propertyNameDependencySets = new List<ImmutableArray<ServiceTypeIdentity>>();
        foreach (var conflict in serviceTypeMapResult.Conflicts)
        {
            hasInvalidGraph = true;
            ReportServiceTypeConflict(
                conflict,
                allProviders,
                sourceProductionContext.ReportDiagnostic);
        }

        var missingDependencies = new Dictionary<ServiceTypeIdentity, Location>();
        foreach (var provider in allProviders)
        {
            if (provider.Dependencies.IsDefault)
            {
                continue;
            }

            foreach (var dependency in provider.DependencyIdentities)
            {
                if (!serviceTypeMapResult.IdentityMap.ContainsKey(dependency))
                {
                    AddMissingDependency(missingDependencies, dependency, provider.Location);
                }
            }
        }

        foreach (var dependencySet in referencedConsumerDependencies)
        {
            propertyNameDependencySets.Add(dependencySet);
            foreach (var dependency in dependencySet)
            {
                if (!serviceTypeMapResult.IdentityMap.ContainsKey(dependency))
                {
                    AddMissingDependency(missingDependencies, dependency, Location.None);
                }
            }
        }

        var localProviderIdentities = GetLocalProviderIdentities(candidates);
        foreach (var consumerDeclaration in consumerDeclarations)
        {
            if (consumerDeclaration is null)
            {
                continue;
            }

            var semanticModel = compilation.GetSemanticModel(consumerDeclaration.SyntaxTree);
            if (semanticModel.GetDeclaredSymbol(consumerDeclaration) is not INamedTypeSymbol consumerSymbol)
            {
                continue;
            }

            var consumerModel = ConsumerValidator.Validate(
                consumerDeclaration,
                consumerSymbol,
                localProviderIdentities,
                static _ => { });
            if (!consumerModel.HasValue)
            {
                continue;
            }

            var dependencyIdentities = consumerModel.Value.Dependencies
                .Select(dependency => dependency.Identity ??
                    new ServiceTypeIdentity(
                        dependency.FullyQualifiedName,
                        compilation.Assembly.Identity.ToString()))
                .ToImmutableArray();
            propertyNameDependencySets.Add(dependencyIdentities);
            foreach (var dependencyIdentity in dependencyIdentities)
            {
                if (!serviceTypeMapResult.IdentityMap.ContainsKey(dependencyIdentity))
                {
                    AddMissingDependency(
                        missingDependencies,
                        dependencyIdentity,
                        consumerDeclaration.Identifier.GetLocation());
                }
            }
        }

        foreach (var provider in allProviders)
        {
            if (!provider.DependencyIdentities.IsDefault &&
                !provider.DependencyIdentities.IsEmpty)
            {
                propertyNameDependencySets.Add(provider.DependencyIdentities);
            }
        }

        foreach (var missingDependency in missingDependencies
                     .OrderBy(pair => pair.Key.FullyQualifiedName, StringComparer.Ordinal)
                     .ThenBy(pair => pair.Key.AssemblyIdentity, StringComparer.Ordinal))
        {
            hasInvalidGraph = true;
            sourceProductionContext.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.MissingExternalProvider,
                missingDependency.Value,
                FormatServiceTypeIdentity(missingDependency.Key)));
        }

        var validatedProviderSets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dependencySet in propertyNameDependencySets)
        {
            var dependencyProviders = ResolveDependencyProviders(
                dependencySet,
                allProviders,
                serviceTypeMapResult.IdentityMap);
            if (dependencyProviders.Count == 0)
            {
                continue;
            }

            var providerSetKey = string.Join(
                "\u001f",
                dependencyProviders
                    .Select(provider => provider.TypeIdentity.ToString())
                    .OrderBy(identity => identity, StringComparer.Ordinal));
            if (!validatedProviderSets.Add(providerSetKey))
            {
                continue;
            }

            if (!ValidateProviderPropertyNames(
                    dependencyProviders,
                    sourceProductionContext.ReportDiagnostic))
            {
                hasInvalidGraph = true;
            }
        }

        if (allProviders.Count > 0)
        {
            var sortResult = TopologicalSorter.SortByLevels(
                allProviders.ToImmutableArray(),
                serviceTypeMapResult.IdentityMap);
            if (sortResult.HasCycle)
            {
                hasInvalidGraph = true;
                sourceProductionContext.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.CircularDependency,
                    Location.None,
                    sortResult.CycleIdentities.IsEmpty
                        ? FormatCycle(sortResult.Cycle, allProviders)
                        : FormatCycle(sortResult.CycleIdentities)));
            }
        }

        if (hasInvalidGraph)
        {
            return;
        }

        var moduleSource = ProviderModuleEmitter.Generate(
            localProviders.ToImmutableArray(),
            externalProviderAssemblies,
            isCompositionRoot: true);
        AddProviderModuleSource(sourceProductionContext, moduleSource);
    }

    private static void EmitConsumers(
        ImmutableArray<TypeDeclarationSyntax> consumerDeclarations,
        Compilation compilation,
        ImmutableArray<ProviderCandidate> providerCandidates,
        GeneratorOptions options,
        SourceProductionContext sourceProductionContext)
    {
        var providerModels = GetProviderModels(providerCandidates);
        var localProviderIdentities = GetLocalProviderIdentities(providerCandidates);
        var serviceTypeMapResult = ServiceTypeResolver.BuildServiceTypeMap(providerModels);
        var propertyNames = PropertyNameResolver.ResolvePropertyNamesByIdentity(providerModels);
        var customPropertyNames = providerModels
            .ToImmutableDictionary(provider => provider.TypeIdentity, provider => provider.PropertyName);
        var consumerModels = new List<ConsumerModel>();
        var isExecutable = IsExecutable(options, compilation);
        var currentAssemblyIdentity = compilation.Assembly.Identity.ToString();

        foreach (var typeDeclaration in consumerDeclarations)
        {
            var semanticModel = compilation.GetSemanticModel(typeDeclaration.SyntaxTree);
            var typeSymbol = semanticModel.GetDeclaredSymbol(typeDeclaration) as INamedTypeSymbol;
            if (typeSymbol is null)
            {
                continue;
            }

            var model = ConsumerValidator.Validate(
                typeDeclaration,
                typeSymbol,
                localProviderIdentities,
                sourceProductionContext.ReportDiagnostic);
            if (!model.HasValue)
            {
                continue;
            }

            if (!options.IsCompositionRoot &&
                isExecutable &&
                HasExternalDependency(
                    model.Value,
                    localProviderIdentities,
                    serviceTypeMapResult.IdentityMap,
                    currentAssemblyIdentity))
            {
                sourceProductionContext.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.MissingCompositionRoot,
                    typeDeclaration.Identifier.GetLocation(),
                    model.Value.Dependencies
                        .Where(dependency =>
                        {
                            var identity = dependency.Identity ??
                                new ServiceTypeIdentity(
                                    dependency.FullyQualifiedName,
                                    currentAssemblyIdentity);
                            return !serviceTypeMapResult.IdentityMap.ContainsKey(identity) &&
                                   (dependency.IsContract ||
                                    !localProviderIdentities.Contains(identity));
                        })
                        .Select(dependency => dependency.Identity is { } identity
                            ? FormatServiceTypeIdentity(identity)
                            : dependency.FullyQualifiedName)
                        .FirstOrDefault() ?? model.Value.FullyQualifiedName));
            }

            ReportConsumerPropertyNameConflicts(
                model.Value,
                typeDeclaration.Identifier.GetLocation(),
                propertyNames,
                customPropertyNames,
                sourceProductionContext.ReportDiagnostic);
            consumerModels.Add(model.Value);
        }

        var consumerSources = ConsumerEmitter.Generate(
            consumerModels,
            propertyNames,
            customPropertyNames);
        foreach (var generatedSource in consumerSources)
        {
            sourceProductionContext.AddSource(
                generatedSource.Key,
                SourceText.From(generatedSource.Value, Encoding.UTF8));
        }
    }

    private static List<ProviderModel> GetProviderModels(
        IEnumerable<ProviderCandidate> candidates)
    {
        return candidates
            .Where(candidate => candidate.Model.HasValue)
            .Select(candidate => candidate.Model!.Value)
            .OrderBy(provider => provider.FullyQualifiedName, StringComparer.Ordinal)
            .ThenBy(provider => provider.AssemblyIdentity, StringComparer.Ordinal)
            .ToList();
    }

    private static ImmutableHashSet<ServiceTypeIdentity> GetLocalProviderIdentities(
        IEnumerable<ProviderCandidate> candidates)
    {
        return candidates
            .Where(candidate => candidate.TypeIdentity.HasValue)
            .Select(candidate => candidate.TypeIdentity!.Value)
            .ToImmutableHashSet();
    }

    private static void ReportProviderCandidateDiagnostics(
        ImmutableArray<ProviderCandidate> candidates,
        Action<Diagnostic> reportDiagnostic)
    {
        foreach (var candidate in candidates)
        {
            foreach (var diagnostic in candidate.Diagnostics)
            {
                reportDiagnostic(diagnostic);
            }
        }
    }

    private static List<ProviderModel> ResolveDependencyProviders(
        IEnumerable<ServiceTypeIdentity> dependencies,
        IReadOnlyCollection<ProviderModel> providers,
        ImmutableDictionary<ServiceTypeIdentity, ServiceTypeIdentity> serviceTypeMap)
    {
        var providerIdentities = new HashSet<ServiceTypeIdentity>();
        foreach (var dependency in dependencies)
        {
            if (serviceTypeMap.TryGetValue(dependency, out var providerIdentity))
            {
                providerIdentities.Add(providerIdentity);
            }
        }

        return providers
            .Where(provider => providerIdentities.Contains(provider.TypeIdentity))
            .GroupBy(provider => provider.TypeIdentity)
            .Select(group => group.First())
            .ToList();
    }

    private static void AddMissingDependency(
        Dictionary<ServiceTypeIdentity, Location> missingDependencies,
        ServiceTypeIdentity dependency,
        Location location)
    {
        if (!missingDependencies.TryGetValue(dependency, out var existingLocation) ||
            existingLocation == Location.None && location.IsInSource)
        {
            missingDependencies[dependency] = location;
        }
    }

    private static void ReportServiceTypeConflict(
        ServiceTypeConflict conflict,
        IReadOnlyCollection<ProviderModel> providers,
        Action<Diagnostic> reportDiagnostic)
    {
        var conflictingProviders = conflict.ProviderTypeIdentities.IsEmpty
            ? providers
                .Where(provider => conflict.ProviderFullyQualifiedNames.Contains(
                    provider.FullyQualifiedName,
                    StringComparer.Ordinal))
                .ToList()
            : providers
                .Where(provider => conflict.ProviderTypeIdentities.Contains(
                    new ServiceTypeIdentity(provider.FullyQualifiedName, provider.AssemblyIdentity)))
                .ToList();
        conflictingProviders = conflictingProviders
            .OrderBy(provider => provider.FullyQualifiedName, StringComparer.Ordinal)
            .ThenBy(provider => provider.AssemblyIdentity, StringComparer.Ordinal)
            .ToList();
        var providerNames = conflict.ProviderTypeIdentities.IsEmpty
            ? conflictingProviders.Count == 0
                ? string.Join(", ", conflict.ProviderFullyQualifiedNames)
                : string.Join(", ", conflictingProviders.Select(FormatProviderName))
            : string.Join(", ", conflict.ProviderTypeIdentities.Select(FormatServiceTypeIdentity));
        var serviceTypeName = !conflict.ServiceTypeIdentities.IsEmpty
            ? string.Join(", ", conflict.ServiceTypeIdentities.Select(FormatServiceTypeIdentity))
            : conflict.ServiceTypeIdentity is { } identity
                ? FormatServiceTypeIdentity(identity)
                : conflict.ServiceTypeFullyQualifiedName;
        var location = conflictingProviders
            .Where(provider => provider.Location.IsInSource)
            .Select(provider => provider.Location)
            .FirstOrDefault() ?? Location.None;
        reportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.ServiceTypeConflict,
            location,
            serviceTypeName,
            providerNames));
    }

    private static string FormatCycle(ImmutableArray<ServiceTypeIdentity> cycle)
    {
        return string.Join(" -> ", cycle.Select(FormatServiceTypeIdentity));
    }

    private static string FormatServiceTypeIdentity(ServiceTypeIdentity identity)
    {
        return identity.ToDiagnosticString();
    }

    private static string FormatCycle(
        ImmutableArray<string> cycle,
        IReadOnlyCollection<ProviderModel> providers)
    {
        var providersByName = providers
            .GroupBy(provider => provider.FullyQualifiedName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var includeAssemblyNames = providers.Any(provider => !provider.Location.IsInSource);
        return string.Join(
            " -> ",
            cycle.Select(cycleName =>
            {
                if (!providersByName.TryGetValue(cycleName, out var provider))
                {
                    return cycleName;
                }

                return includeAssemblyNames || !provider.Location.IsInSource
                    ? FormatProviderName(provider)
                    : provider.FullyQualifiedName;
            }));
    }

    private static string FormatProviderName(ProviderModel provider)
    {
        return provider.Location.IsInSource
            ? provider.FullyQualifiedName
            : $"{provider.FullyQualifiedName}, {provider.AssemblyIdentity}";
    }

    private static bool HasExternalDependency(
        ConsumerModel consumer,
        ImmutableHashSet<ServiceTypeIdentity> localProviderIdentities,
        ImmutableDictionary<ServiceTypeIdentity, ServiceTypeIdentity> serviceTypeMap,
        string currentAssemblyIdentity)
    {
        foreach (var dependency in consumer.Dependencies)
        {
            var identity = dependency.Identity ??
                new ServiceTypeIdentity(dependency.FullyQualifiedName, currentAssemblyIdentity);
            if (serviceTypeMap.ContainsKey(identity))
            {
                continue;
            }

            if (dependency.IsContract)
            {
                return true;
            }

            if (localProviderIdentities.Contains(identity) ||
                string.Equals(
                    dependency.Identity?.AssemblyIdentity,
                    currentAssemblyIdentity,
                    StringComparison.Ordinal))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool IsExecutable(GeneratorOptions options, Compilation compilation)
    {
        if (!string.IsNullOrWhiteSpace(options.OutputType))
        {
            return string.Equals(options.OutputType, "Exe", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(options.OutputType, "WinExe", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(options.OutputType, "ConsoleApplication", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(options.OutputType, "WindowsApplication", StringComparison.OrdinalIgnoreCase);
        }

        return compilation.Options.OutputKind is OutputKind.ConsoleApplication or OutputKind.WindowsApplication;
    }

    private static GeneratorOptions ReadGeneratorOptions(AnalyzerConfigOptionsProvider optionsProvider)
    {
        var globalOptions = optionsProvider.GlobalOptions;
        var isCompositionRoot = globalOptions.TryGetValue(
                "build_property.SingletonDICompositionRoot",
                out var compositionRootValue) &&
            string.Equals(compositionRootValue?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        globalOptions.TryGetValue("build_property.OutputType", out var outputType);
        return new GeneratorOptions(isCompositionRoot, outputType?.Trim());
    }

    private static void AddProviderModuleSource(
        SourceProductionContext sourceProductionContext,
        string moduleSource)
    {
        if (moduleSource.Length == 0)
        {
            return;
        }

        sourceProductionContext.AddSource(
            "__SingletonDIProviderModule__.g.cs",
            SourceText.From(moduleSource, Encoding.UTF8));
    }

    private static void ReportConsumerPropertyNameConflicts(
        ConsumerModel consumer,
        Location location,
        ImmutableDictionary<ServiceTypeIdentity, string> propertyNames,
        ImmutableDictionary<ServiceTypeIdentity, string?> customPropertyNames,
        Action<Diagnostic> reportDiagnostic)
    {
        _ = PropertyNameResolver.ResolveConsumerPropertyNamesByIdentity(
            consumer.Dependencies,
            propertyNames,
            customPropertyNames,
            out var conflicts);

        foreach (var conflict in conflicts)
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.PropertyNameConflictsWithGenerated,
                location,
                conflict.PropertyName,
                conflict.SecondReference.Identity is { } identity
                    ? FormatServiceTypeIdentity(identity)
                    : conflict.SecondReference.FullyQualifiedName));
        }
    }

    private static bool ValidateProviderPropertyNames(
        IReadOnlyCollection<ProviderModel> providers,
        Action<Diagnostic> reportDiagnostic)
    {
        var propertyNameToProvider = new Dictionary<string, ProviderModel>(StringComparer.Ordinal);
        foreach (var provider in providers)
        {
            if (provider.PropertyName is null)
            {
                continue;
            }

            if (propertyNameToProvider.TryGetValue(provider.PropertyName, out var existingProvider))
            {
                reportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.PropertyNameConflict,
                    existingProvider.Location,
                    provider.PropertyName,
                    FormatProviderName(existingProvider),
                    FormatProviderName(provider)));
                reportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.PropertyNameConflict,
                    provider.Location,
                    provider.PropertyName,
                    FormatProviderName(existingProvider),
                    FormatProviderName(provider)));
                return false;
            }

            propertyNameToProvider[provider.PropertyName] = provider;
        }

        foreach (var provider in providers)
        {
            if (provider.PropertyName is null)
            {
                continue;
            }

            foreach (var otherProvider in providers)
            {
                if (PropertyNameResolver.GetAllPossibleGeneratedNames(otherProvider)
                    .Any(possibleName => string.Equals(
                        possibleName,
                        provider.PropertyName,
                        StringComparison.Ordinal)))
                {
                    reportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.PropertyNameConflictsWithGenerated,
                        provider.PropertyNameLocation ?? provider.Location,
                        provider.PropertyName,
                        FormatProviderName(otherProvider)));
                    return false;
                }
            }
        }

        return true;
    }

    private readonly record struct GeneratorOptions(bool IsCompositionRoot, string? OutputType);

    private readonly record struct ProviderCandidate(
        ProviderModel? Model,
        ImmutableArray<Diagnostic> Diagnostics,
        ServiceTypeIdentity? TypeIdentity);
}
