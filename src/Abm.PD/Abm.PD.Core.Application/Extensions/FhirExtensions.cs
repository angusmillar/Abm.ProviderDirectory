using Hl7.Fhir.Model;

namespace Abm.PD.Core.Application.Extensions;

public static class FhirExtensions
{
    /// <summary>
    /// Returns every <see cref="ResourceReference"/> held anywhere within the provided resource, in document
    /// order, including those carried by extensions, those inside <c>contained</c> resources and those nested
    /// inside another reference (<c>Reference.identifier.assigner</c>).
    /// </summary>
    /// <remarks>
    /// Local references (<c>#id</c>) to a contained resource are excluded: they point inside the resource, so
    /// there is no server id to rewrite. The returned objects are the instances the resource holds, so a caller
    /// may update them in place.
    /// </remarks>
    /// <param name="resource">The resource to search.</param>
    /// <returns>The references found, in document order; empty when the resource holds none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resource"/> is <see langword="null"/>.</exception>
    public static List<ResourceReference> GetAllResourceReferences(this Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return Descendants(resource)
            .OfType<ResourceReference>()
            .Where(reference => !reference.IsContainedReference)
            .ToList();
    }

    //Depth-first, so the result is in document order; the POCO tree is acyclic, so no visited set is needed.
    private static IEnumerable<Base> Descendants(Base element)
    {
        foreach (Base child in element.Children)
        {
            yield return child;

            foreach (Base descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
}
