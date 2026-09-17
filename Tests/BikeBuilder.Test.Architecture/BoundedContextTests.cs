namespace BikeBuilder.Test.Architecture;

// The project-reference diagram in the README, as rules: services share only the Contracts
// and ServiceDefaults libraries, talk to each other over the wire, and the front ends do
// the same.
public class BoundedContextTests
{
  public static TheoryData<string, string> ServicePairs
  {
    get
    {
      var pairs = new TheoryData<string, string>();
      foreach (var service in Assemblies.Services.Keys)
        foreach (var other in Assemblies.Services.Keys.Where(other => other != service))
          pairs.Add(service, other);
      return pairs;
    }
  }

  [Theory]
  [MemberData(nameof(ServicePairs))]
  public void Services_do_not_depend_on_each_other(string service, string other)
  {
    OwnTypesOf(Assemblies.Services[service])
        .Should().NotDependOnAny(OwnTypesOf(Assemblies.Services[other]))
        .Because($"{service} and {other} are separate bounded contexts: they share the Contracts library and reach each other over gRPC, GraphQL, HTTP or Service Bus, never in-process")
        .Check(ArchitectureUnderTest);
  }

  [Fact]
  public void Contracts_depends_on_no_other_BikeBuilder_assembly()
  {
    OwnTypesOf(Assemblies.Contracts)
        .Should().NotDependOnAny(OwnTypesOf(Assemblies.Api, Assemblies.Orders, Assemblies.Ratings, Assemblies.Notifications,
            Assemblies.Chat, Assemblies.Mcp, Assemblies.ServiceDefaults, Assemblies.DataSeeder, Assemblies.WebAdmin, Assemblies.Storefront))
        .Because("Contracts is the one library every service and front end shares, so it can depend on none of them")
        .Check(ArchitectureUnderTest);
  }

  [Fact]
  public void ServiceDefaults_depends_on_no_BikeBuilder_code()
  {
    Types().That().ResideInAssembly(Assemblies.ServiceDefaults)
        .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching(@"^BikeBuilder(\.|$)")
        .Because("the Aspire service defaults (telemetry, health checks, service discovery) are infrastructure that knows nothing about the domain")
        .Check(ArchitectureUnderTest);
  }

  [Fact]
  public void Front_ends_reach_the_services_only_over_the_wire()
  {
    OwnTypesOf(Assemblies.WebAdmin, Assemblies.Storefront)
        .Should().NotDependOnAny(OwnTypesOf(Assemblies.Api, Assemblies.Orders, Assemblies.Ratings, Assemblies.Notifications,
            Assemblies.Chat, Assemblies.Mcp, Assemblies.DataSeeder))
        .Because("the browser and mobile front ends hold only Contracts and their own gRPC-Web, GraphQL and REST clients; service types stay on the server")
        .Check(ArchitectureUnderTest);
  }

  [Fact]
  public void The_admin_app_and_the_storefront_are_independent()
  {
    OwnTypesOf(Assemblies.WebAdmin).Should().NotDependOnAny(OwnTypesOf(Assemblies.Storefront))
        .Because("the back office and the public storefront are separate front ends that share only Contracts")
        .Check(ArchitectureUnderTest);
    OwnTypesOf(Assemblies.Storefront).Should().NotDependOnAny(OwnTypesOf(Assemblies.WebAdmin))
        .Because("the back office and the public storefront are separate front ends that share only Contracts")
        .Check(ArchitectureUnderTest);
  }
}
