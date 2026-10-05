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
        var languageScan = context.CompilationProvider
            .Select(static (compilation, _) => GetLanguageScan(compilation))
            .WithTrackingName("LanguageSupport");
        var referencedComposition = ReferencedCompositionCollector
            .CreateProvider(
                context.CompilationProvider,
                generatorOptions,
                ReferencedCompositionCollector.Instance,
                ShouldCollectReferencedComposition)
            .WithTrackingName("ReferencedCompositionSnapshot");

        // The scan is shared. Every consumer of the language facts derives from this one value, so a
        // project that does not use SingletonDI pays for a single pass over its syntax trees per
        // compilation rather than one per output. This is the generator's dominant per-keystroke cost
        // in such a project.
        context.RegisterSourceOutput(
            languageScan.Combine(generatorOptions),
            static (sourceProductionContext, input) =>
            {
                var (scan, options) = input;
                ReportLanguageDiagnostics(scan, options, sourceProductionContext.ReportDiagnostic);
            });

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
                            null,
                            string.Empty);
                    }

                    var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
                    var model = ProviderValidator.Validate(
                        typeDeclaration,
                        typeSymbol,
                        context.SemanticModel.Compilation,
                        diagnostics.Add);
                    var typeIdentity = ServiceTypeIdentity.FromSymbol(typeSymbol);
                    var reported = diagnostics.ToImmutable();
                    return new ProviderCandidate(
                        model,
                        reported,
                        typeIdentity,
                        CreateProviderCandidateKey(model, reported, typeIdentity));
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
            .Select(static (typeDeclaration, _) => typeDeclaration!)
            .Collect()
            .Select(static (declarations, _) => DistinctDeclarations(declarations));

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
                        diagnostics.Add,
                        context.SemanticModel);
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

        var languageSupport = languageScan.Select(static (scan, _) => scan.Support);

        // DM0036 asks what a consumer reads, so it cannot be answered from a cached candidate: a
        // keystroke inside a consumer changes the answer without changing anything the candidate's
        // key covers, and a candidate that carried its own locations would also point into the
        // compilation the previous run analyzed. The hint therefore holds nothing but a metadata name
        // and the validated dependencies, and the report below is combined with the compilation so
        // that it always reads the consumer back out of the one being analyzed.
        var consumerUsageHints = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "SingletonDI.Attributes.SingletonDIConsumeAttribute",
                predicate: static (node, _) => node is TypeDeclarationSyntax,
                transform: static (context, token) => CreateConsumerUsageHint(context, token))
            .Collect()
            .Select(static (hints, _) => hints.IsDefault
                ? ImmutableArray<ConsumerUsageHint>.Empty
                : hints
                    .Where(static hint => hint.HasValue)
                    .Select(static hint => hint!.Value)
                    .Distinct()
                    .ToImmutableArray());

        var consumerUsageInputs = consumerUsageHints
            .Combine(context.CompilationProvider)
            .Combine(languageSupport)
            .WithTrackingName("ConsumerUsageOutput");
        context.RegisterSourceOutput(consumerUsageInputs, (sourceProductionContext, input) =>
        {
            var ((hints, compilation), language) = input;
            if (!language.CanEmit || hints.IsDefault)
            {
                return;
            }

            ReportConsumerUsage(hints, compilation, sourceProductionContext.ReportDiagnostic);
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
            .Combine(consumerDeclarations)
            .Combine(context.CompilationProvider)
            .Combine(languageSupport)
            .Combine(referencedComposition)
            .Combine(generatorOptions)
            .WithTrackingName("CompositionRootOutput");
        context.RegisterSourceOutput(compositionInputs, (sourceProductionContext, input) =>
        {
            var (compositionData, options) = input;
            var (((candidatesAndConsumers, compilation), language), snapshot) = compositionData;
            var (candidates, consumerDeclarationsForRoot) = candidatesAndConsumers;
            if (!language.CanEmit || !options.IsCompositionRoot)
            {
                return;
            }

            ReportSnapshotDiagnostics(snapshot, sourceProductionContext.ReportDiagnostic);
            EmitCompositionRoot(
                candidates,
                consumerDeclarationsForRoot,
                compilation,
                snapshot,
                sourceProductionContext);
        });

        var consumerInputs = consumerCandidates
            .Collect()
            .Select(static (candidates, _) => candidates.Distinct().ToImmutableArray())
            .Combine(providerCandidates.Collect())
            .Combine(languageSupport)
            .Combine(generatorOptions)
            .WithTrackingName("ConsumerOutput");
        context.RegisterSourceOutput(consumerInputs, (sourceProductionContext, input) =>
        {
            var (((consumerCandidatesForOutput, providerCandidatesForOutput), language), options) = input;
            if (!language.CanEmit ||
                ((consumerCandidatesForOutput.IsDefault || consumerCandidatesForOutput.IsEmpty) &&
                 (options.IsCompositionRoot || (!IsExecutable(options) && !language.IsExecutable))))
            {
                return;
            }

            EmitConsumers(
                consumerCandidatesForOutput,
                providerCandidatesForOutput,
                options,
                IsExecutable(options) || language.IsExecutable,
                sourceProductionContext);
        });

        var referencedValidationInputs = providerCandidates
            .Collect()
            .Combine(referencedComposition)
            .Combine(languageSupport)
            .Combine(generatorOptions)
            .WithTrackingName("ReferencedConsumerValidation");
        context.RegisterSourceOutput(referencedValidationInputs, (sourceProductionContext, input) =>
        {
            var (((providerCandidatesForValidation, snapshot), language), options) = input;
            if (!language.CanEmit || options.IsCompositionRoot ||
                (!IsExecutable(options) && !language.IsExecutable))
            {
                return;
            }

            ReportSnapshotDiagnostics(snapshot, sourceProductionContext.ReportDiagnostic);
            EmitReferencedConsumerValidation(
                providerCandidatesForValidation,
                snapshot,
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

        var cycle = TopologicalSorter.TryFindCycle(
            providerModels.ToImmutableArray(),
            serviceTypeMapResult.IdentityMap);
        if (!cycle.IsEmpty)
        {
            sourceProductionContext.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.CircularDependency,
                GetCycleLocation(cycle, providerModels),
                string.Join(" -> ", cycle.Select(identity => identity.FullyQualifiedName))));
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
        ReferencedCompositionSnapshot referencedComposition,
        SourceProductionContext sourceProductionContext)
    {
        var localProviders = GetProviderModels(candidates);
        var declaredLocalProviderIdentities = GetLocalProviderIdentities(candidates);
        var externalProviders = referencedComposition.Providers;
        var externalProviderAssemblies = referencedComposition.ProviderAssemblies;
        var referencedConsumerDependencies = referencedComposition.ConsumerDependencySets;
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

        // A provider that failed validation is absent from the service type map, but it was declared
        // and its own diagnostic already explains why it cannot be registered. Reporting a missing
        // provider for it as well named a provider the author had written, and the duplicate failure
        // also suppressed this root's module.
        bool IsUnmapped(ServiceTypeIdentity identity) =>
            !serviceTypeMapResult.IdentityMap.ContainsKey(identity) &&
            !declaredLocalProviderIdentities.Contains(identity);

        foreach (var provider in allProviders)
        {
            if (provider.Dependencies.IsDefault)
            {
                continue;
            }

            foreach (var dependency in provider.DependencyIdentities)
            {
                if (IsUnmapped(dependency))
                {
                    // The argument that asks for the dependency is where the author has to act, so
                    // the diagnostic points at it and not at the provider that declared it.
                    var dependencyLocation = provider.GetDependencyLocation(dependency);
                    AddMissingDependency(
                        missingDependencies,
                        dependency,
                        dependencyLocation.IsInSource ? dependencyLocation : provider.Location);
                }
            }
        }

        foreach (var dependencySet in referencedConsumerDependencies)
        {
            propertyNameDependencySets.Add(dependencySet);
            foreach (var dependency in dependencySet)
            {
                if (IsUnmapped(dependency))
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
                static _ => { },
                semanticModel);
            if (!consumerModel.HasValue)
            {
                continue;
            }

            var consumerDependencies = consumerModel.Value.Dependencies
                .Select(dependency => (
                    Dependency: dependency,
                    Identity: dependency.Identity ??
                        new ServiceTypeIdentity(
                            dependency.FullyQualifiedName,
                            compilation.Assembly.Identity.ToString())))
                .ToImmutableArray();
            propertyNameDependencySets.Add(
                consumerDependencies.Select(item => item.Identity).ToImmutableArray());
            foreach (var (dependency, dependencyIdentity) in consumerDependencies)
            {
                if (IsUnmapped(dependencyIdentity))
                {
                    // The unmapped type is named in the attribute, so the attribute argument is where
                    // the author has to act.
                    var declarationLocation = consumerDeclaration.Identifier.GetLocation();
                    AddMissingDependency(
                        missingDependencies,
                        dependencyIdentity,
                        dependency.DeclarationLocation is { IsInSource: true } declaredLocation
                            ? declaredLocation
                            : declarationLocation);
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
            var cycle = TopologicalSorter.TryFindCycle(
                allProviders.ToImmutableArray(),
                serviceTypeMapResult.IdentityMap);
            if (!cycle.IsEmpty)
            {
                hasInvalidGraph = true;
                sourceProductionContext.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.CircularDependency,
                    GetCycleLocation(cycle, allProviders),
                    FormatCycle(cycle)));
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
        bool isExecutable,
        SourceProductionContext sourceProductionContext)
    {
        var providerModels = GetProviderModels(providerCandidates);
        var localProviderIdentities = GetLocalProviderIdentities(providerCandidates);
        var serviceTypeMapResult = ServiceTypeResolver.BuildServiceTypeMap(providerModels);
        var consumerModels = new List<ConsumerModel>();

        // A dependency this executable cannot satisfy is named in the consumer's own attribute, so
        // both the message and the anchor are taken from the same first matching dependency.
        ServiceReferenceModel? FindUnmappedDependency(ConsumerModel consumer)
        {
            foreach (var dependency in consumer.Dependencies)
            {
                if (dependency.Identity is { } identity &&
                    !serviceTypeMapResult.IdentityMap.ContainsKey(identity) &&
                    (dependency.IsContract || !localProviderIdentities.Contains(identity)))
                {
                    return dependency;
                }
            }

            return null;
        }

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
                var unmappedDependency = FindUnmappedDependency(model);
                var dependencyLocation = unmappedDependency?.DeclarationLocation;
                sourceProductionContext.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.MissingCompositionRoot,
                    dependencyLocation is { IsInSource: true } declaredLocation
                        ? declaredLocation
                        : candidate.DeclarationLocation,
                    unmappedDependency is null
                        ? model.FullyQualifiedName
                        : unmappedDependency.Value.Identity is { } identity
                            ? FormatServiceTypeIdentity(identity)
                            : unmappedDependency.Value.FullyQualifiedName));
            }

            ReportConsumerPropertyNameConflicts(
                model,
                candidate.DeclarationLocation,
                sourceProductionContext.ReportDiagnostic);
            consumerModels.Add(MarkExistingConsumerMemberConflicts(
                model,
                candidate.ExistingMemberLocations,
                candidate.DeclarationLocation,
                sourceProductionContext.ReportDiagnostic));
        }

        var consumerSources = ConsumerEmitter.GenerateByIdentity(consumerModels);
        foreach (var generatedSource in consumerSources)
        {
            sourceProductionContext.AddSource(
                generatedSource.Key,
                SourceText.From(generatedSource.Value, Encoding.UTF8));
        }
    }

    private static void EmitReferencedConsumerValidation(
        ImmutableArray<ProviderCandidate> providerCandidates,
        ReferencedCompositionSnapshot referencedComposition,
        SourceProductionContext sourceProductionContext)
    {
        var localProviders = GetProviderModels(providerCandidates);
        var allProviders = localProviders
            .Concat(referencedComposition.Providers)
            .ToImmutableArray();
        var serviceTypeMapResult = ServiceTypeResolver.BuildServiceTypeMap(allProviders);
        var missingDependencies = new HashSet<ServiceTypeIdentity>();

        // The map is built from local and referenced providers together, so a contract both of them
        // export is a conflict here too. Reading only the missing dependencies bound the contract to
        // whichever provider sorted first and reported nothing.
        foreach (var conflict in serviceTypeMapResult.Conflicts)
        {
            ReportServiceTypeConflict(conflict, allProviders, sourceProductionContext.ReportDiagnostic);
        }

        foreach (var dependencySet in referencedComposition.ConsumerDependencySets)
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

    private static void ReportSnapshotDiagnostics(
        ReferencedCompositionSnapshot snapshot,
        Action<Diagnostic> reportDiagnostic)
    {
        foreach (var diagnostic in snapshot.Diagnostics)
        {
            reportDiagnostic(diagnostic);
        }
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

    /// <summary>
    /// Anchors a cycle diagnostic at the name of the provider the reported cycle starts from, so that
    /// the diagnostic points at a declaration the reader can navigate to.
    /// </summary>
    /// <param name="cycle">The identities the cycle walks through, starting at its first entry.</param>
    /// <param name="providers">Every provider in the graph the cycle was found in.</param>
    private static Location GetCycleLocation(
        ImmutableArray<ServiceTypeIdentity> cycle,
        IEnumerable<ProviderModel> providers)
    {
        foreach (var identity in cycle)
        {
            foreach (var provider in providers)
            {
                if (provider.TypeIdentity == identity && provider.Location.IsInSource)
                {
                    return provider.Location;
                }
            }
        }

        return Location.None;
    }

    private static string FormatServiceTypeIdentity(ServiceTypeIdentity identity)
    {
        return identity.ToDiagnosticString();
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

    private static bool ShouldCollectReferencedComposition(
        Compilation compilation,
        GeneratorOptions options)
    {
        if (options.IsCompositionRoot)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(options.OutputType))
        {
            return IsExecutable(options);
        }

        return compilation.Options.OutputKind is OutputKind.ConsoleApplication or OutputKind.WindowsApplication;
    }

    private static bool IsExecutable(GeneratorOptions options)
    {
        return !string.IsNullOrWhiteSpace(options.OutputType) &&
               (string.Equals(options.OutputType, "Exe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(options.OutputType, "WinExe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(options.OutputType, "ConsoleApplication", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(options.OutputType, "WindowsApplication", StringComparison.OrdinalIgnoreCase));
    }

    private static void ReportLanguageDiagnostics(
        LanguageScan scan,
        GeneratorOptions options,
        Action<Diagnostic> reportDiagnostic)
    {
        var inputLocation = scan.Usage.ConsumeAttributeLocation ?? scan.Usage.ProvideAttributeLocation;
        if (inputLocation is null && !options.IsCompositionRoot)
        {
            return;
        }

        var effectiveVersion = scan.Support.EffectiveVersion;
        if (effectiveVersion.CompareTo(LanguageVersion.CSharp9) < 0)
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.GeneratedLanguageVersionNotSupported,
                inputLocation ?? Location.None,
                effectiveVersion));
        }

        if (scan.Usage.FileScopedConsumerLocation is not null &&
            effectiveVersion.CompareTo(LanguageVersion.CSharp10) < 0)
        {
            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.FileScopedConsumerLanguageVersionNotSupported,
                scan.Usage.FileScopedConsumerLocation,
                effectiveVersion));
        }
    }

    private readonly record struct LanguageSupport(
        LanguageVersion EffectiveVersion,
        bool HasFileScopedConsumer,
        bool IsExecutable)
    {
        public bool CanEmit =>
            EffectiveVersion.CompareTo(LanguageVersion.CSharp9) >= 0 &&
            (!HasFileScopedConsumer || EffectiveVersion.CompareTo(LanguageVersion.CSharp10) >= 0);
    }

    private readonly record struct LanguageScan(LanguageSupport Support, SingletonDIUsage Usage);

    /// <summary>
    /// Produces the language facts and the attribute locations from one pass over the syntax trees.
    /// </summary>
    /// <remarks>
    /// The two lookups need the same walk, and every output in the pipeline needs the result, so
    /// deriving them separately meant repeating the walk once per consumer. In a project that does
    /// not use SingletonDI the walk is the only work the generator does.
    /// </remarks>
    private static LanguageScan GetLanguageScan(Compilation compilation)
    {
        var usage = ScanSingletonDIUsage(compilation);
        return new LanguageScan(
            new LanguageSupport(
                GetEffectiveLanguageVersion(compilation),
                usage.FileScopedConsumerLocation is not null,
                compilation.Options.OutputKind is OutputKind.ConsoleApplication or OutputKind.WindowsApplication),
            usage);
    }

    private static LanguageVersion GetEffectiveLanguageVersion(Compilation compilation)
    {
        var effective = LanguageVersion.Default;
        var found = false;
        foreach (var tree in compilation.SyntaxTrees)
        {
            if (tree.Options is not CSharpParseOptions parseOptions)
            {
                continue;
            }

            var version = LanguageVersionFacts.MapSpecifiedToEffectiveVersion(parseOptions.LanguageVersion);
            if (!found || version.CompareTo(effective) < 0)
            {
                effective = version;
                found = true;
            }
        }

        return effective;
    }

    private readonly record struct SingletonDIUsage(
        Location? ConsumeAttributeLocation,
        Location? ProvideAttributeLocation,
        Location? FileScopedConsumerLocation);

    /// <summary>
    /// Locates the attributes the language diagnostics anchor to in a single pass over the
    /// compilation's syntax trees.
    /// </summary>
    private static SingletonDIUsage ScanSingletonDIUsage(Compilation compilation)
    {
        Location? consumeAttributeLocation = null;
        Location? provideAttributeLocation = null;
        Location? fileScopedConsumerLocation = null;

        foreach (var tree in compilation.SyntaxTrees)
        {
            foreach (var typeDeclaration in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                foreach (var attribute in typeDeclaration.AttributeLists
                             .SelectMany(attributeList => attributeList.Attributes))
                {
                    if (consumeAttributeLocation is null && IsAttribute(attribute, "SingletonDIConsume"))
                    {
                        consumeAttributeLocation = attribute.GetLocation();
                    }
                    else if (provideAttributeLocation is null && IsAttribute(attribute, "SingletonDIProvide"))
                    {
                        provideAttributeLocation = attribute.GetLocation();
                    }
                }

                if (fileScopedConsumerLocation is not null || !HasSingletonDIConsumeAttribute(typeDeclaration))
                {
                    continue;
                }

                var fileScopedNamespace = typeDeclaration.Ancestors()
                    .OfType<FileScopedNamespaceDeclarationSyntax>()
                    .FirstOrDefault();
                if (fileScopedNamespace is not null)
                {
                    // The namespace name, not the declaration: a file-scoped namespace spans the rest
                    // of the file, and a diagnostic over it would underline everything below it.
                    fileScopedConsumerLocation = fileScopedNamespace.Name.GetLocation();
                }
            }
        }

        return new SingletonDIUsage(
            consumeAttributeLocation,
            provideAttributeLocation,
            fileScopedConsumerLocation);
    }

    private static bool HasSingletonDIConsumeAttribute(TypeDeclarationSyntax typeDeclaration)
    {
        foreach (var attribute in typeDeclaration.AttributeLists
                     .SelectMany(attributeList => attributeList.Attributes))
        {
            if (IsAttribute(attribute, "SingletonDIConsume"))
            {
                return true;
            }
        }

        return false;
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

    private static string CreateProviderCandidateKey(
        ProviderModel? model,
        ImmutableArray<Diagnostic> diagnostics,
        ServiceTypeIdentity? typeIdentity)
    {
        var key = new StringBuilder();
        key.Append(model.HasValue ? "model" : "none");
        if (model is { } provider)
        {
            key.Append('\u001f').Append(provider.FullyQualifiedName);
            key.Append('\u001f').Append(provider.ShortName);
            key.Append('\u001f').Append(provider.Namespace);
            key.Append('\u001f').Append(provider.AssemblyIdentity);
            key.Append('\u001f').Append(provider.HasInitializeAsyncMethod);
            key.Append('\u001f').Append(provider.IsDisposable);
            key.Append('\u001f').Append(provider.IsAsyncDisposable);
            key.Append('\u001f').Append(provider.ServiceTypeFullyQualifiedName);
            key.Append('\u001f').Append(provider.ServiceTypeShortName);
            key.Append('\u001f').Append(provider.ServiceTypeNamespace);
            key.Append('\u001f').Append(provider.ServiceTypeIdentity?.CanonicalIdentity);
            key.Append('\u001f').Append(provider.PropertyName);
            if (!provider.Dependencies.IsDefault)
            {
                foreach (var dependency in provider.Dependencies)
                {
                    key.Append('\u001f').Append("dependency").Append('\u001f').Append(dependency);
                }
            }

            foreach (var dependency in provider.DependencyIdentities)
            {
                key.Append('\u001f').Append("identity").Append('\u001f').Append(dependency.CanonicalIdentity);
            }

            foreach (var dependencyLocation in provider.DependencyLocations.OrderBy(
                         pair => pair.Key,
                         StringComparer.Ordinal))
            {
                key.Append('\u001f').Append("dependencyLocation").Append('\u001f').Append(dependencyLocation.Key);
                AppendLocationKey(key, dependencyLocation.Value);
            }

            AppendLocationKey(key, provider.Location);
            if (provider.PropertyNameLocation is { } propertyNameLocation)
            {
                AppendLocationKey(key, propertyNameLocation);
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

        key.Append('\u001f').Append("identity");
        key.Append('\u001f').Append(typeIdentity?.CanonicalIdentity);
        return key.ToString();
    }

    /// <summary>
    /// Keeps one entry per consumer declaration.
    /// </summary>
    /// <remarks>
    /// <c>ForAttributeWithMetadataName</c> invokes its transform once for every matching attribute,
    /// so a consumer that spreads its dependencies over several consume attributes arrives here
    /// repeated. Every copy names the same declaration, and emitting one generated member set per
    /// copy would put the same properties into the compilation more than once.
    /// </remarks>
    private static ImmutableArray<TypeDeclarationSyntax> DistinctDeclarations(
        ImmutableArray<TypeDeclarationSyntax> declarations)
    {
        var seen = new HashSet<(string FilePath, TextSpan Span)>();
        return declarations
            .Where(declaration => seen.Add((declaration.SyntaxTree.FilePath, declaration.Span)))
            .ToImmutableArray();
    }

    /// <summary>
    /// Names a consumer and the dependencies it declared, for DM0036 to judge against a compilation
    /// read back later. Both members are independent of the compilation that produced them, so a
    /// cached hint stays valid and never carries a location from a superseded run.
    /// </summary>
    private static ConsumerUsageHint? CreateConsumerUsageHint(
        GeneratorAttributeSyntaxContext context,
        CancellationToken cancellationToken)
    {
        var typeDeclaration = (TypeDeclarationSyntax)context.TargetNode;
        if (context.SemanticModel.GetDeclaredSymbol(typeDeclaration, cancellationToken) is not
            INamedTypeSymbol consumer)
        {
            return null;
        }

        var model = ConsumerValidator.Validate(
            typeDeclaration,
            consumer,
            static _ => { },
            context.SemanticModel);
        return model is { Dependencies.IsEmpty: false }
            ? new ConsumerUsageHint(GetMetadataName(consumer), model.Value.Dependencies)
            : null;
    }

    /// <summary>
    /// Builds the name <see cref="Compilation.GetTypeByMetadataName"/> accepts, which is the whole
    /// namespace chain in front of the type name and a <c>+</c> between the names of nested types.
    /// <see cref="INamedTypeSymbol.MetadataName"/> on its own carries neither.
    /// </summary>
    private static string GetMetadataName(INamedTypeSymbol type)
    {
        var parts = new Stack<string>();
        for (var current = type; current is not null; current = current.ContainingType)
        {
            parts.Push(current.MetadataName);
        }

        for (var containing = type.ContainingNamespace;
             containing is { IsGlobalNamespace: false };
             containing = containing.ContainingNamespace)
        {
            parts.Push(containing.Name);
        }

        return string.Join(".", parts);
    }

    private static void ReportConsumerUsage(
        ImmutableArray<ConsumerUsageHint> hints,
        Compilation compilation,
        Action<Diagnostic> reportDiagnostic)
    {
        foreach (var hint in hints)
        {
            if (compilation.GetTypeByMetadataName(hint.MetadataName) is not INamedTypeSymbol consumer)
            {
                continue;
            }

            ConsumerUsageAnalyzer.ReportUnusedDependencies(
                compilation,
                consumer,
                hint.Dependencies,
                reportDiagnostic);
        }
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
                if (dependency.DeclarationLocation is { } dependencyDeclarationLocation)
                {
                    key.Append('\u001f').Append("dependencyLocation");
                    AppendLocationKey(key, dependencyDeclarationLocation);
                }
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

        // The declaration location anchors the diagnostics reported from the consumer model, so it
        // is part of what the output depends on and cannot be left out of the key conditionally. A
        // cached candidate kept a Location whose SyntaxTree belonged to the compilation that produced
        // it, which pointed the diagnostics at the previous span and made reading the run result
        // fail outright.
        key.Append('\u001f').Append("declaration");
        AppendLocationKey(key, declarationLocation);

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
        key.Append('\u001f').Append(shape.IsStatic);
        foreach (var containingType in shape.ContainingTypes)
        {
            // The type parameter list and the type parameter names are both in the key, so the arity
            // they imply is covered, and a containing type of a validated consumer is always partial.
            key.Append('\u001f').Append("containing");
            key.Append('\u001f').Append(containingType.Name);
            key.Append('\u001f').Append(containingType.DeclarationKind);
            foreach (var typeParameter in containingType.TypeParameters)
            {
                key.Append('\u001f').Append(typeParameter);
            }

            key.Append('\u001f').Append(containingType.TypeParameterList);
            key.Append('\u001f').Append(containingType.ConstraintClauses);
        }

        key.Append('\u001f').Append(shape.IsFileScoped);
    }

    private static void AppendLocationKey(StringBuilder key, Location location)
    {
        key.Append('\u001f').Append(location.IsInSource ? "source" : "none");
        if (location.IsInSource)
        {
            key.Append(location.SourceTree?.FilePath ?? string.Empty)
                .Append(':')
                .Append(location.SourceSpan.Start)
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
            AddTypeParameterLocations(current);
            AddMemberLocations(current);
        }

        foreach (var interfaceType in typeSymbol.AllInterfaces)
        {
            AddMemberLocations(interfaceType);
        }

        // A containing type contributes its own type parameters to the scope of the nested type,
        // and it is not a base type, so it is walked separately.
        for (INamedTypeSymbol? containing = typeSymbol.ContainingType;
             containing is not null;
             containing = containing.ContainingType)
        {
            AddTypeParameterLocations(containing);
        }

        return locations.ToImmutable();

        // A type parameter is not a member, so GetMembers never reports it, but it is in scope for
        // the whole type body and a property that takes its name does not compile.
        void AddTypeParameterLocations(INamedTypeSymbol type)
        {
            foreach (var typeParameter in type.TypeParameters)
            {
                if (possibleNames.Contains(typeParameter.Name) && !locations.ContainsKey(typeParameter.Name))
                {
                    locations.Add(typeParameter.Name, typeParameter.Locations.FirstOrDefault() ?? Location.None);
                }
            }
        }

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
        Action<Diagnostic> reportDiagnostic)
    {
        var resolvedNames = PropertyNameResolver.ResolveConsumerPropertyNamesByIdentity(
            consumer.Dependencies,
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
        Action<Diagnostic> reportDiagnostic)
    {
        _ = PropertyNameResolver.ResolveConsumerPropertyNamesByIdentity(
            consumer.Dependencies,
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
                    existingProvider.PropertyNameLocation ?? existingProvider.Location,
                    provider.PropertyName,
                    FormatProviderName(existingProvider),
                    FormatProviderName(provider)));
                reportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.PropertyNameConflict,
                    provider.PropertyNameLocation ?? provider.Location,
                    provider.PropertyName,
                    FormatProviderName(existingProvider),
                    FormatProviderName(provider)));
                return false;
            }

            propertyNameToProvider[provider.PropertyName] = provider;
        }

        // Index the names that providers without an explicit name will generate, so a custom name is
        // looked up once instead of compared against every other provider. The nested loop built a
        // fresh list of two interpolated strings per pair, which is quadratic in the provider count
        // on a path that runs on every keystroke.
        var generatedNameToProvider = new Dictionary<string, ProviderModel>(StringComparer.Ordinal);
        foreach (var provider in providers)
        {
            if (provider.PropertyName is not null)
            {
                continue;
            }

            foreach (var possibleName in PropertyNameResolver.GetAllPossibleGeneratedNames(provider))
            {
                if (!generatedNameToProvider.ContainsKey(possibleName))
                {
                    generatedNameToProvider[possibleName] = provider;
                }
            }
        }

        foreach (var provider in providers)
        {
            if (provider.PropertyName is null)
            {
                continue;
            }

            // A provider that carries its own name never generates the candidate names, so only a
            // provider without one can collide. Comparing the provider against its own candidates
            // separately is what keeps "a custom name that matches one of its own generated names"
            // an error.
            if (!generatedNameToProvider.TryGetValue(provider.PropertyName, out var conflictingProvider))
            {
                conflictingProvider = provider;
                var selfConflict = false;
                foreach (var possibleName in PropertyNameResolver.GetAllPossibleGeneratedNames(provider))
                {
                    if (!string.Equals(possibleName, provider.PropertyName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    selfConflict = true;
                    break;
                }

                if (!selfConflict)
                {
                    continue;
                }
            }

            reportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.PropertyNameConflictsWithGenerated,
                provider.PropertyNameLocation ?? provider.Location,
                provider.PropertyName,
                FormatProviderName(conflictingProvider)));
            return false;
        }

        return true;
    }

    private readonly record struct GeneratorOptions(bool IsCompositionRoot, string? OutputType);

    private readonly struct ProviderCandidate : IEquatable<ProviderCandidate>
    {
        public ProviderCandidate(
            ProviderModel? model,
            ImmutableArray<Diagnostic> diagnostics,
            ServiceTypeIdentity? typeIdentity,
            string cacheKey)
        {
            Model = model;
            Diagnostics = diagnostics;
            TypeIdentity = typeIdentity;
            CacheKey = cacheKey;
        }

        public ProviderModel? Model { get; }

        public ImmutableArray<Diagnostic> Diagnostics { get; }

        public ServiceTypeIdentity? TypeIdentity { get; }

        public string CacheKey { get; }

        public bool Equals(ProviderCandidate other)
        {
            return string.Equals(CacheKey, other.CacheKey, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return obj is ProviderCandidate other && Equals(other);
        }

        public override int GetHashCode()
        {
            return CacheKey is null ? 0 : StringComparer.Ordinal.GetHashCode(CacheKey);
        }
    }
}
