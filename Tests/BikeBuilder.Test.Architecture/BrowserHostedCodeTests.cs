namespace BikeBuilder.Test.Architecture;

// Web.Admin runs as WebAssembly in the browser; the shared storefront runs there, on the
// public site's server circuits and inside the MAUI shell. Server-only stacks must never
// leak into either, whatever a transitive NuGet closure happens to let compile.
public class BrowserHostedCodeTests
{
  // The ASP.NET Core part is anchored to the exact server namespaces: the SignalR and
  // HTTP-connection *client* namespaces underneath them are what browser code legitimately uses.
  const string ServerOnlyNamespaces =
      @"^(Microsoft\.EntityFrameworkCore|Microsoft\.Azure\.Cosmos|Azure\.Messaging|Azure\.Storage)(\.|$)" +
      @"|^Microsoft\.AspNetCore\.(Builder|Hosting|Http|Mvc|Routing|Server|SignalR)$";

  [Fact]
  public void Browser_hosted_code_uses_no_server_only_stacks()
  {
    OwnTypesOf(Assemblies.WebAdmin, Assemblies.Storefront)
        .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching(ServerOnlyNamespaces)
        .AndShould().NotDependOnAny(Types().That().ResideInAssembly(Assemblies.ServiceDefaults))
        .Because("EF Core, Cosmos, the Azure SDKs, the Aspire service defaults and server-side ASP.NET Core have no place in code shipped to a browser or a phone")
        .Check(ArchitectureUnderTest);
  }

  [Fact]
  public void The_shared_storefront_is_host_agnostic()
  {
    OwnTypesOf(Assemblies.Storefront)
        .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching(
            @"^(Microsoft\.AspNetCore\.Components\.WebAssembly|Microsoft\.AspNetCore\.Components\.Server|Microsoft\.Maui)(\.|$)")
        .Because("the storefront renders on server circuits, in WebAssembly and in the MAUI shell; host specifics arrive through the IOrderIdStorage-style abstractions each host registers")
        .Check(ArchitectureUnderTest);
  }
}
