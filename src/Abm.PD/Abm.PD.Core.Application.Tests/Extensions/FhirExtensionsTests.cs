using Abm.PD.Core.Application.Extensions;
using Hl7.Fhir.Model;

namespace Abm.PD.Core.Application.Tests.Extensions;

public class FhirExtensionsTests
{
    [Fact]
    public void GetAllResourceReferences_ReturnsEveryReferenceInDocumentOrder()
    {
        PractitionerRole role = new()
        {
            Practitioner = new ResourceReference("Practitioner/1"),
            Organization = new ResourceReference("Organization/2"),
            Location = [new ResourceReference("Location/3"), new ResourceReference("Location/4")],
        };

        List<ResourceReference> references = role.GetAllResourceReferences();

        Assert.Equal(
            ["Practitioner/1", "Organization/2", "Location/3", "Location/4"],
            references.Select(reference => reference.Reference));
    }

    [Fact]
    public void GetAllResourceReferences_ReturnsTheInstancesHeldByTheResource()
    {
        PractitionerRole role = new() { Practitioner = new ResourceReference("Practitioner/1") };

        ResourceReference found = Assert.Single(role.GetAllResourceReferences());

        //The caller rewrites ids through the returned instances, so a copy would be useless.
        Assert.Same(role.Practitioner, found);
        found.Reference = "Practitioner/99";
        Assert.Equal("Practitioner/99", role.Practitioner.Reference);
    }

    [Fact]
    public void GetAllResourceReferences_FindsReferenceHeldInAnExtension()
    {
        Practitioner practitioner = new();
        practitioner.Extension.Add(
            new Extension("http://example.test/employer", new ResourceReference("Organization/1")));

        ResourceReference found = Assert.Single(practitioner.GetAllResourceReferences());

        Assert.Equal("Organization/1", found.Reference);
    }

    [Fact]
    public void GetAllResourceReferences_FindsReferenceNestedInsideAnotherReference()
    {
        //Reference.identifier.assigner is itself a Reference, so "all" has to descend into a found reference.
        ResourceReference assigner = new("Organization/assigner");
        PractitionerRole role = new()
        {
            Practitioner = new ResourceReference
            {
                Identifier = new Identifier { Value = "123", Assigner = assigner },
            },
        };

        List<ResourceReference> references = role.GetAllResourceReferences();

        Assert.Equal(2, references.Count);
        Assert.Same(role.Practitioner, references[0]);
        Assert.Same(assigner, references[1]);
    }

    [Fact]
    public void GetAllResourceReferences_FindsReferenceInsideAContainedResource()
    {
        ResourceReference partOf = new("Organization/parent");
        PractitionerRole role = new()
        {
            Contained = [new Organization { Id = "org", PartOf = partOf }],
        };

        ResourceReference found = Assert.Single(role.GetAllResourceReferences());

        Assert.Same(partOf, found);
    }

    [Fact]
    public void GetAllResourceReferences_ExcludesLocalReferencesToContainedResources()
    {
        //A "#id" reference points inside the resource, so there is no server id for the caller to rewrite.
        PractitionerRole role = new()
        {
            Contained = [new Organization { Id = "org" }],
            Organization = new ResourceReference("#org"),
            Practitioner = new ResourceReference("Practitioner/1"),
        };

        ResourceReference found = Assert.Single(role.GetAllResourceReferences());

        Assert.Equal("Practitioner/1", found.Reference);
    }

    [Fact]
    public void GetAllResourceReferences_ReturnsEmptyWhenResourceHasNoReferences()
    {
        Practitioner practitioner = new() { Id = "1", Active = true };

        Assert.Empty(practitioner.GetAllResourceReferences());
    }

    [Fact]
    public void GetAllResourceReferences_ThrowsWhenResourceIsNull()
    {
        Resource resource = null!;

        Assert.Throws<ArgumentNullException>(() => resource.GetAllResourceReferences());
    }
}
