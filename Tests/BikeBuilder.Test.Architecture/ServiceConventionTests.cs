using Grpc.Core;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace BikeBuilder.Test.Architecture;

// Where each kind of entry point and data type lives, so a reader who knows one service can
// find their way around the others.
public class ServiceConventionTests
{
  [Fact]
  public void Function_entry_points_are_public_methods_of_Functions_classes()
  {
    MethodMembers().That().HaveAnyAttributes(typeof(FunctionAttribute))
        .Should().BeDeclaredIn(Classes().That().HaveNameEndingWith("Function").Or().HaveNameEndingWith("Functions"))
        .AndShould().BePublic()
        .Check(ArchitectureUnderTest);
  }

  [Fact]
  public void Functions_classes_live_in_a_Functions_namespace()
  {
    Classes().That().HaveNameEndingWith("Function").Or().HaveNameEndingWith("Functions")
        .And().ResideInAssembly(Assemblies.Ratings, Assemblies.Notifications)
        .Should().ResideInNamespaceMatching(@"^BikeBuilder\.API\.\w+\.Functions$")
        .Check(ArchitectureUnderTest);
  }

  [Fact]
  public void Mcp_tool_classes_are_Tools_classes_in_MCP_Tools()
  {
    Classes().That().HaveAnyAttributes(typeof(McpServerToolTypeAttribute))
        .Should().ResideInNamespace("BikeBuilder.MCP.Tools")
        .AndShould().HaveNameEndingWith("Tools")
        .Check(ArchitectureUnderTest);
  }

  [Fact]
  public void Mcp_tool_methods_are_declared_in_tool_type_classes()
  {
    MethodMembers().That().HaveAnyAttributes(typeof(McpServerToolAttribute))
        .Should().BeDeclaredIn(Classes().That().HaveAnyAttributes(typeof(McpServerToolTypeAttribute)))
        .Because("WithToolsFromAssembly only registers tools whose class carries [McpServerToolType]; a tool method anywhere else is silently missing from the server")
        .Check(ArchitectureUnderTest);
  }

  [Fact]
  public void Grpc_service_implementations_are_GrpcService_classes_in_API_Services()
  {
    // The generated *ServiceBase classes carry [BindServiceMethod]; their concrete subclasses
    // are the implementations.
    Classes().That().AreAssignableTo(Classes().That().HaveAnyAttributes(typeof(BindServiceMethodAttribute)))
        .And().AreNotAbstract()
        .Should().ResideInNamespace("BikeBuilder.API.Services")
        .AndShould().HaveNameEndingWith("GrpcService")
        .Check(ArchitectureUnderTest);
  }

  [Fact]
  public void DbContexts_are_DbContext_classes_in_a_Data_namespace()
  {
    Classes().That().AreAssignableTo(typeof(DbContext)).And().AreNotAbstract()
        .Should().HaveNameEndingWith("DbContext")
        .AndShould().ResideInNamespaceMatching(@"^BikeBuilder\.API(\.\w+)?\.Data$")
        .Check(ArchitectureUnderTest);
  }

  [Fact]
  public void Entities_are_persistence_models_not_transport_types()
  {
    Types().That().ResideInNamespaceMatching(@"^BikeBuilder\.API(\.\w+)?\.Data\.Entities$")
        .Should().NotDependOnAnyTypesThat().ResideInNamespaceMatching(
            @"^(Grpc|Google\.Protobuf|BikeBuilder\.API\.Protos|Microsoft\.AspNetCore|Azure|Microsoft\.Azure)(\.|$)")
        .Because("the services map entities into their gRPC responses and Service Bus events; the entities themselves know nothing about the wire (HotChocolate's projection attributes on the orders entities are the deliberate exception)")
        .Check(ArchitectureUnderTest);
  }

  [Fact]
  public void Endpoint_classes_are_static_mapping_classes()
  {
    // A static class is abstract and sealed in IL.
    Classes().That().ResideInNamespaceMatching(@"\.Endpoints$").And().AreNotNested()
        .Should().BeAbstract()
        .AndShould().BeSealed()
        .Because("a *Endpoints class is a set of Map* extension methods over the minimal-API route builder, not a service")
        .Check(ArchitectureUnderTest);
  }
}
