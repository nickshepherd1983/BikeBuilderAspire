namespace BikeBuilder.Test.Architecture;

// Cross-service messages are Contracts: every service serialises the same records, and only
// the publishers hold a Service Bus sender.
public class MessagingTests
{
  [Fact]
  public void Events_are_sealed_records_in_the_Contracts_Events_namespace()
  {
    Classes().That().HaveNameEndingWith("Event")
        .Should().ResideInNamespace("BikeBuilder.Contracts.Events")
        .AndShould().BeSealed()
        .Because("an event published by one service and consumed by another is a shared contract, defined once")
        .Check(ArchitectureUnderTest);
  }

  // The Service Bus SDK types through which a message can be sent. The client counts: a
  // class that creates its own sender from an injected client is a publisher too.
  public static readonly IObjectProvider<IType> ServiceBusSendingTypes = Types(true).That()
      .HaveFullName("Azure.Messaging.ServiceBus.ServiceBusClient")
      .Or().HaveFullName("Azure.Messaging.ServiceBus.ServiceBusSender")
      .Or().HaveFullName("Azure.Messaging.ServiceBus.ServiceBusMessage")
      .As("the Service Bus client, sender and message types");

  // Outer classes only: the async state machines nested in a publisher carry its name
  // mangled, and each host's Program registers the client and sender it hands to its publisher.
  public static GivenClassesConjunction ServiceBusSendingClasses => Classes().That().AreNotNested()
      .And().DoNotHaveFullNameMatching("^Program(/|$)")
      .And().DependOnAny(ServiceBusSendingTypes);

  [Fact]
  public void Only_publishers_hold_a_Service_Bus_client_or_sender()
  {
    ServiceBusSendingClasses
        .Should().HaveNameEndingWith("Publisher")
        .Because("messages leave a service through its IEventPublisher implementation, which is where tracing and message-type stamping live")
        .Check(ArchitectureUnderTest);
  }
}
