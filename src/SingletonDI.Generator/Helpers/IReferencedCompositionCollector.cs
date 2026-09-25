using Microsoft.CodeAnalysis;
using SingletonDI.Generator.Models;

namespace SingletonDI.Generator.Helpers;

internal interface IReferencedCompositionCollector
{
    ReferencedCompositionSnapshot Collect(
        Compilation compilation,
        CancellationToken cancellationToken);
}
