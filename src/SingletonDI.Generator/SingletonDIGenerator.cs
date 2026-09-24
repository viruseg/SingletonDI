using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
        var languageSupport = context.CompilationProvider
            .Select(static (compilation, _) => GetLanguageSupport(compilation));

        context.RegisterSourceOutput(
            context.CompilationProvider,
            static (sourceProductionContext, compilation) =>
                ReportLanguageDiagnostics(compilation, sourceProductionContext.ReportDiagnostic));

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

        var consumerCandidates = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "SingletonDI.Attributes.SingletonDIConsumeAttribute",
                predicate: static (node, _) => node is TypeDeclarationSyntax,
                transform: static (context, token) =>
                {
                    var typeDeclaration = (TypeDeclarationSyntax)context.TargetNode;
                    var typeSymbol = context.SemanticModel.GetDeclaredSymbol(typeDeclaration, token) as INamedTypeSymbol;
                    if (typeSymbol is null)
                    {
                        return CreateConsumerCandidate(
                            null,
                            ImmutableArray<Diagnostic>.Empty,
                            Location.None,
                            ImmutableDictionary<string, Location>.Empty);
                    }

                    var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
                    var model = ConsumerValidator.Validate(
                        typeDeclaration,
                        typeSymbol,
                        diagnostics.Add);
                    var existingMemberLocations = GetExistingMemberLocations(
                        typeSymbol,
                        model.HasValue
                            ? model.Value.Dependencies
                            : ImmutableArray<ServiceReferenceModel>.Empty);
                    var declarationLocation = typeDeclaration.Identifier.GetLocation();
                    return CreateConsumerCandidate(
                        model,
                        diagnostics.ToImmutable(),
                        declarationLocation,
                        existingMemberLocations);
                });

        var providerInputs = providerCandidates
            .Collect()
            .Combine(languageSupport)
            .Combine(generatorOptions)
            .WithTrackingName("ProviderModuleOutput");
        context.RegisterSourceOutput(providerInputs, (sourceProductionContext, input) =>
        {
            var ((candidates, language), options) = input;
            if (!language.CanEmit)
            {
                return;
            }

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
            if (!CanEmitGeneratedSource(compilation) || !options.IsCompositionRoot)
            {
                return;
            }

            EmitCompositionRoot(
                candidates,
                consumerDeclarationsForRoot,
                compilation,
                sourceProductionContext);
        });

        var consumerInputs = consumerCandidates
            .Collect()
            .Combine(providerCandidates.Collect())
            .Combine(languageSupport)
            .Combine(generatorOptions)
            .WithTrackingName("ConsumerOutput");
        context.RegisterSourceOutput(consumerInputs, (sourceProductionContext, input) =>
        {
            var (((consumerCandidatesForOutput, providerCandidatesForOutput), language), options) = input;
            if (!language.CanEmit ||
                ((consumerCandidatesForOutput.IsDefault || consumerCandidatesForOutput.IsEmpty) &&
                 (options.IsCompositionRoot || !IsExecutable(options))))
            {
                return;
            }

            EmitConsumers(
                consumerCandidatesForOutput,
                providerCandidatesForOutput,
                options,
                sourceProductionContext);
        });

        var referencedValidationInputs = providerCandidates
            .Collect()
            .Combine(context.CompilationProvider)
            .Combine(generatorOptions)
            .WithTrackingName("ReferencedConsumerValidation");
        context.RegisterSourceOutput(referencedValidationInputs, (sourceProductionContext, input) =>
        {
            var ((providerCandidatesForValidation, compilation), options) = input;
            if (!CanEmitGeneratedSource(compilation) ||
                options.IsCompositionRoot ||
                !IsExecutable(options, compilation))
            {
                return;
            }

            EmitReferencedConsumerValidation(
                providerCandidatesForValidation,
                compilation,
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
        ImmutableArray<ConsumerCandidate> consumerCandidates,
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
        var isExecutable = IsExecutable(options);

        foreach (var candidate in consumerCandidates)
        {
            foreach (var diagnostic in candidate.Diagnostics)
            {
                sourceProductionContext.ReportDiagnostic(diagnostic);
            }

            if (!candidate.Model.HasValue)
            {
                continue;
            }

            var model = candidate.Model.Value;
            if (!options.IsCompositionRoot &&
                isExecutable &&
                HasExternalDependency(
                    model,
                    localProviderIdentities,
                    serviceTypeMapResult.IdentityMap))
            {
                sourceProductionContext.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.MissingCompositionRoot,
                    candidate.DeclarationLocation,
                    model.Dependencies
                        .Where(dependency =>
                        {
                            var identity = dependency.Identity;
                            return identity is not null &&
                                   !serviceTypeMapResult.IdentityMap.ContainsKey(identity.Value) &&
                                   (dependency.IsContract ||
                                    !localProviderIdentities.Contains(identity.Value));
                        })
                        .Select(dependency => dependency.Identity is { } identity
                            ? FormatServiceTypeIdentity(identity)
                            : dependency.FullyQualifiedName)
                        .FirstOrDefault() ?? model.FullyQualifiedName));
            }

            ReportConsumerPropertyNameConflicts(
                model,
                candidate.DeclarationLocation,
                propertyNames,
                customPropertyNames,
                sourceProductionContext.ReportDiagnostic);
            consumerModels.Add(MarkExistingConsumerMemberConflicts(
                model,
                candidate.ExistingMemberLocations,
                candidate.DeclarationLocation,
                propertyNames,
                customPropertyNames,
                sourceProductionContext.ReportDiagnostic));
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

    private static void EmitReferencedConsumerValidation(
        ImmutableArray<ProviderCandidate> providerCandidates,
        Compilation compilation,
        SourceProductionContext sourceProductionContext)
    {
        var localProviders = GetProviderModels(providerCandidates);
        var externalProviders = ProviderSymbolCollector.CollectReferencedProviders(
            compilation,
            sourceProductionContext.CancellationToken,
            sourceProductionContext.ReportDiagnostic,
            out _);
        var allProviders = localProviders
            .Concat(externalProviders)
            .ToImmutableArray();
        var serviceTypeMapResult = ServiceTypeResolver.BuildServiceTypeMap(allProviders);
        var referencedConsumerDependencies =
            ReferencedConsumerCollector.CollectReferencedConsumerDependencyIdentities(
                compilation,
                sourceProductionContext.CancellationToken);
        var missingDependencies = new HashSet<ServiceTypeIdentity>();

        foreach (var dependencySet in referencedConsumerDependencies)
        {
            foreach (var dependency in dependencySet)
            {
                if (!serviceTypeMapResult.IdentityMap.ContainsKey(dependency) &&
                    missingDependencies.Add(dependency))
                {
                    sourceProductionContext.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.MissingExternalProvider,
                        Location.None,
                        FormatServiceTypeIdentity(dependency)));
                }
            }
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
        ImmutableDictionary<ServiceTypeIdentity, ServiceTypeIdentity> serviceTypeMap)
    {
        foreach (var dependency in consumer.Dependencies)
        {
            if (dependency.Identity is not { } identity)
            {
                return dependency.IsContract;
            }

            if (serviceTypeMap.ContainsKey(identity))
            {
                continue;
            }

            if (dependency.IsContract || !localProviderIdentities.Contains(identity))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsExecutable(GeneratorOptions options)
    {
        return !string.IsNullOrWhiteSpace(options.OutputType) &&
               (string.Equals(options.OutputType, "Exe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(options.OutputType, "WinExe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(options.OutputType, "ConsoleApplication", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(options.OutputType, "WindowsApplication", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsExecutable(GeneratorOptions options, Compilation compilation)
    {
        if (!string.IsNullOrWhiteSpace(options.OutputType))
        {
            return IsExecutable(options);
        }

        return compilation.Options.OutputKind is OutputKind.ConsoleApplication or OutputKind.WindowsApplication;
    }

    private static void ReportLanguageDiagnostics(
        Compilation compilation,
        Action<Diagnostic> reportDiagnostic)
    {
        var inputLocation = FindGeneratorAttributeLocation(compilation, "SingletonDIConsume") ??
            FindGeneratorAttributeLocation(compilation, "SingletonDIProvide");
        if (inputLocation is null)
        {
            return;
        }

        var effectiveVersion = GetEffectiveLanguageVersion(compilation);
        if (effectiveVersion.CompareTo(LanguageVersion.CSharp9) < 0)
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.GeneratedLanguageVersionNotSupported,
                inputLocation,
                effectiveVersion));
        }

        var fileScopedLocation = FindFileScopedConsumerLocation(compilation);
        if (fileScopedLocation is not null &&
            effectiveVersion.CompareTo(LanguageVersion.CSharp10) < 0)
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.FileScopedConsumerLanguageVersionNotSupported,
                fileScopedLocation,
                effectiveVersion));
        }
    }

    private readonly record struct LanguageSupport(
        LanguageVersion EffectiveVersion,
        bool HasFileScopedConsumer)
    {
        public bool CanEmit =>
            EffectiveVersion.CompareTo(LanguageVersion.CSharp9) >= 0 &&
            (!HasFileScopedConsumer || EffectiveVersion.CompareTo(LanguageVersion.CSharp10) >= 0);
    }

    private static bool CanEmitGeneratedSource(Compilation compilation)
    {
        return GetLanguageSupport(compilation).CanEmit;
    }

    private static LanguageSupport GetLanguageSupport(Compilation compilation)
    {
        return new LanguageSupport(
            GetEffectiveLanguageVersion(compilation),
            FindFileScopedConsumerLocation(compilation) is not null);
    }

    private static LanguageVersion GetEffectiveLanguageVersion(Compilation compilation)
    {
        return compilation.SyntaxTrees
            .Select(tree => tree.Options)
            .OfType<CSharpParseOptions>()
            .Select(options => LanguageVersionFacts.MapSpecifiedToEffectiveVersion(options.LanguageVersion))
            .OrderBy(version => (int)version)
            .FirstOrDefault();
    }

    private static Location? FindGeneratorAttributeLocation(
        Compilation compilation,
        string attributeName)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            foreach (var typeDeclaration in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                foreach (var attribute in typeDeclaration.AttributeLists
                             .SelectMany(attributeList => attributeList.Attributes))
                {
                    if (IsAttribute(attribute, attributeName))
                    {
                        return attribute.GetLocation();
                    }
                }
            }
        }

        return null;
    }

    private static Location? FindFileScopedConsumerLocation(Compilation compilation)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            foreach (var typeDeclaration in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (!typeDeclaration.Ancestors().OfType<FileScopedNamespaceDeclarationSyntax>().Any() ||
                    !typeDeclaration.AttributeLists
                        .SelectMany(attributeList => attributeList.Attributes)
                        .Any(attribute => IsAttribute(attribute, "SingletonDIConsume")))
                {
                    continue;
                }

                return typeDeclaration.Ancestors()
                    .OfType<FileScopedNamespaceDeclarationSyntax>()
                    .First()
                    .GetLocation();
            }
        }

        return null;
    }

    private static bool IsAttribute(AttributeSyntax attribute, string name)
    {
        var attributeName = attribute.Name switch
        {
            QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
            AliasQualifiedNameSyntax alias => alias.Name.Identifier.Text,
            SimpleNameSyntax simple => simple.Identifier.Text,
            _ => attribute.Name.ToString(),
        };
        return attributeName == name || attributeName == name + "Attribute";
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

    private static ConsumerCandidate CreateConsumerCandidate(
        ConsumerModel? model,
        ImmutableArray<Diagnostic> diagnostics,
        Location declarationLocation,
        ImmutableDictionary<string, Location> existingMemberLocations)
    {
        var key = new StringBuilder();
        key.Append(model.HasValue ? "model" : "none");
        if (model is { } consumer)
        {
            key.Append('\u001f').Append(consumer.FullyQualifiedName);
            key.Append('\u001f').Append(consumer.ShortName);
            key.Append('\u001f').Append(consumer.Namespace);
            key.Append('\u001f').Append(consumer.IsPartial);
            AppendShapeKey(key, consumer.Shape);
            foreach (var dependency in consumer.Dependencies)
            {
                key.Append('\u001f').Append("dependency");
                key.Append('\u001f').Append(dependency.FullyQualifiedName);
                key.Append('\u001f').Append(dependency.ShortName);
                key.Append('\u001f').Append(dependency.Namespace);
                key.Append('\u001f').Append(dependency.PropertyName);
                key.Append('\u001f').Append(dependency.IsContract);
                key.Append('\u001f').Append(dependency.CanUseProtectedProperty);
                key.Append('\u001f').Append(dependency.Identity?.CanonicalIdentity);
            }
        }

        foreach (var diagnostic in diagnostics)
        {
            key.Append('\u001f').Append("diagnostic");
            key.Append('\u001f').Append(diagnostic.Id);
            key.Append('\u001f').Append(diagnostic.Severity);
            key.Append('\u001f').Append(diagnostic.Descriptor.Title);
            key.Append('\u001f').Append(diagnostic.GetMessage());
            AppendLocationKey(key, diagnostic.Location);
        }

        key.Append('\u001f').Append("declaration");
        if (diagnostics.Length > 0 ||
            existingMemberLocations.Values.Any(location => location.IsInSource))
        {
            AppendLocationKey(key, declarationLocation);
        }
        else
        {
            key.Append("\u001f").Append("none");
        }

        foreach (var member in existingMemberLocations.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            key.Append('\u001f').Append("member").Append('\u001f').Append(member.Key);
            AppendLocationKey(key, member.Value);
        }

        return new ConsumerCandidate(
            model,
            diagnostics,
            declarationLocation,
            existingMemberLocations,
            key.ToString());
    }

    private static void AppendShapeKey(StringBuilder key, ConsumerDeclarationShape shape)
    {
        key.Append('\u001f').Append("shape");
        key.Append('\u001f').Append(shape.DeclarationKind);
        key.Append('\u001f').Append(shape.Name);
        key.Append('\u001f').Append(shape.Namespace);
        key.Append('\u001f').Append(shape.Arity);
        foreach (var typeParameter in shape.TypeParameters)
        {
            key.Append('\u001f').Append(typeParameter);
        }

        key.Append('\u001f').Append(shape.TypeParameterList);
        key.Append('\u001f').Append(shape.ConstraintClauses);
        key.Append('\u001f').Append(shape.IsPartial);
        key.Append('\u001f').Append(shape.IsSealed);
        foreach (var containingType in shape.ContainingTypes)
        {
            key.Append('\u001f').Append("containing");
            key.Append('\u001f').Append(containingType.Name);
            key.Append('\u001f').Append(containingType.DeclarationKind);
            key.Append('\u001f').Append(containingType.Arity);
            foreach (var typeParameter in containingType.TypeParameters)
            {
                key.Append('\u001f').Append(typeParameter);
            }

            key.Append('\u001f').Append(containingType.TypeParameterList);
            key.Append('\u001f').Append(containingType.ConstraintClauses);
            key.Append('\u001f').Append(containingType.IsPartial);
        }

        key.Append('\u001f').Append(shape.IsFileScoped);
    }

    private static void AppendLocationKey(StringBuilder key, Location location)
    {
        key.Append('\u001f').Append(location.IsInSource ? "source" : "none");
        if (location.IsInSource)
        {
            key.Append(location.SourceSpan.Start)
                .Append(':')
                .Append(location.SourceSpan.End);
        }
    }

    private static ImmutableDictionary<string, Location> GetExistingMemberLocations(
        INamedTypeSymbol typeSymbol,
        ImmutableArray<ServiceReferenceModel> dependencies)
    {
        var possibleNames = GetPotentialPropertyNames(dependencies);
        var locations = ImmutableDictionary.CreateBuilder<string, Location>(StringComparer.Ordinal);
        for (INamedTypeSymbol? current = typeSymbol;
             current is not null;
             current = current.BaseType)
        {
            AddMemberLocations(current);
        }

        foreach (var interfaceType in typeSymbol.AllInterfaces)
        {
            AddMemberLocations(interfaceType);
        }

        return locations.ToImmutable();

        void AddMemberLocations(INamedTypeSymbol type)
        {
            foreach (var member in type.GetMembers())
            {
                if (possibleNames.Contains(member.Name) && !locations.ContainsKey(member.Name))
                {
                    locations.Add(
                        member.Name,
                        member.Locations.FirstOrDefault() ?? Location.None);
                }
            }
        }
    }

    private static ImmutableHashSet<string> GetPotentialPropertyNames(
        ImmutableArray<ServiceReferenceModel> dependencies)
    {
        var names = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var dependency in dependencies)
        {
            var baseNames = new List<string>
            {
                $"{dependency.ShortName}Instance",
            };
            var namespaceName = dependency.Namespace;
            if (namespaceName.StartsWith("global::", StringComparison.Ordinal))
            {
                namespaceName = namespaceName.Substring("global::".Length);
            }

            var namespacePrefix = namespaceName.Replace('.', '_');
            if (!string.IsNullOrEmpty(namespacePrefix))
            {
                baseNames.Add($"{namespacePrefix}_{dependency.ShortName}Instance");
            }

            if (!string.IsNullOrEmpty(dependency.PropertyName))
            {
                baseNames.Add(dependency.PropertyName!);
            }

            foreach (var baseName in baseNames)
            {
                names.Add(baseName);
                for (var suffix = 2; suffix <= dependencies.Length + 1; suffix++)
                {
                    names.Add($"{baseName}_{suffix}");
                }
            }
        }

        return names.ToImmutable();
    }

    private static ConsumerModel MarkExistingConsumerMemberConflicts(
        ConsumerModel consumer,
        ImmutableDictionary<string, Location> existingMemberLocations,
        Location location,
        ImmutableDictionary<ServiceTypeIdentity, string> propertyNames,
        ImmutableDictionary<ServiceTypeIdentity, string?> customPropertyNames,
        Action<Diagnostic> reportDiagnostic)
    {
        var resolvedNames = PropertyNameResolver.ResolveConsumerPropertyNamesByIdentity(
            consumer.Dependencies,
            propertyNames,
            customPropertyNames,
            out _);
        var existingMemberNames = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);

        foreach (var dependency in consumer.Dependencies)
        {
            var identity = dependency.Identity ??
                new ServiceTypeIdentity(dependency.FullyQualifiedName, string.Empty);
            if (!resolvedNames.TryGetValue(identity, out var propertyName) ||
                !existingMemberLocations.TryGetValue(propertyName, out var existingMemberLocation))
            {
                continue;
            }

            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.ConsumerPropertyNameAlreadyExists,
                existingMemberLocation == Location.None ? location : existingMemberLocation,
                consumer.FullyQualifiedName,
                propertyName));
            existingMemberNames.Add(propertyName);
        }

        return existingMemberNames.Count == 0
            ? consumer
            : new ConsumerModel(
                consumer.FullyQualifiedName,
                consumer.ShortName,
                consumer.Namespace,
                consumer.IsPartial,
                consumer.Dependencies,
                consumer.Shape,
                existingMemberNames.ToImmutable());
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
