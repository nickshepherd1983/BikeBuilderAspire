using Grpc.Core;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace BikeBuilder.Test.Architecture;

// An ArchUnitNET rule whose selector matches nothing passes. These pin down that the
// selectors the other classes build on still find what they are meant to find, so a renamed
// namespace or attribute turns a rule into a failure here instead of a silent no-op.
public class RuleCoverageTests
{
  [Fact]
  public void Every_assembly_contributes_types_to_the_rules()
  {
    foreach (var assembly in Assemblies.All)
    {
      var own = OwnTypesOf(assembly).GetObjects(ArchitectureUnderTest).ToList();
      Assert.True(own.Count > 0, $"{assembly.GetName().Name} loaded no types of its own");
    }
  }

  [Fact]
  public void Dependencies_between_assemblies_are_seen()
  {
    // Every service depends on Contracts, so the isolation rule's shape must fail here. If it
    // passes, the dependency analysis is blind and all the bounded-context rules are vacuous.
    Assert.Throws<FailedArchRuleException>(() =>
        OwnTypesOf(Assemblies.Api).Should().NotDependOnAny(OwnTypesOf(Assemblies.Contracts)).Check(ArchitectureUnderTest));
  }

  [Fact]
  public void The_convention_selectors_find_their_targets()
  {
    Assert.Equal(5, Classes().That().HaveNameEndingWith("Event").GetObjects(ArchitectureUnderTest).Count());
    Assert.Equal(4, Classes().That().HaveAnyAttributes(typeof(McpServerToolTypeAttribute)).GetObjects(ArchitectureUnderTest).Count());
    Assert.True(MethodMembers().That().HaveAnyAttributes(typeof(McpServerToolAttribute)).GetObjects(ArchitectureUnderTest).Any());
    Assert.True(MethodMembers().That().HaveAnyAttributes(typeof(FunctionAttribute)).GetObjects(ArchitectureUnderTest).Count() >= 9);
    Assert.Equal(2, Classes().That().AreAssignableTo(Classes().That().HaveAnyAttributes(typeof(BindServiceMethodAttribute)))
        .And().AreNotAbstract().GetObjects(ArchitectureUnderTest).Count());
    Assert.Equal(2, Classes().That().AreAssignableTo(typeof(DbContext)).And().AreNotAbstract().GetObjects(ArchitectureUnderTest).Count());
    Assert.True(Types().That().ResideInNamespaceMatching(@"^BikeBuilder\.API(\.\w+)?\.Data\.Entities$").GetObjects(ArchitectureUnderTest).Count() >= 10);
    Assert.Equal(3, Classes().That().ResideInNamespaceMatching(@"\.Endpoints$").And().AreNotNested().GetObjects(ArchitectureUnderTest).Count());

    // The email publisher holds only the client (it creates its own sender), which is why
    // the messaging rule looks for the client as well as the sender.
    Assert.Equal(
    [
      "BikeBuilder.API.Orders.Services.OrderConfirmationEmailPublisher",
      "BikeBuilder.API.Orders.Services.ServiceBusEventPublisher",
      "BikeBuilder.API.Ratings.Services.ServiceBusEventPublisher",
      "BikeBuilder.API.Services.ServiceBusEventPublisher"
    ], MessagingTests.ServiceBusSendingClasses.GetObjects(ArchitectureUnderTest).Select(type => type.FullName).Order());
  }
}
