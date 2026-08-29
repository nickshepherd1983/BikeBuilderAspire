using System.Diagnostics;
using BikeBuilder.Contracts.Events;
using BikeBuilder.Contracts.Messaging;

namespace BikeBuilder.Web.Public.Services;

public class ServiceBusListenerBackgroundService(
    ServiceBusClient client,
    IHubContext<NotificationHub> hubContext,
    ILogger<ServiceBusListenerBackgroundService> logger) : BackgroundService
{
  // IHubContext broadcasts produce no built-in span, so this makes the trace visibly end at
  // "broadcast to SignalR clients", parented under the SDK's ambient ProcessMessage activity.
  static readonly ActivitySource _traceSource = new("BikeBuilder.Web.Public");

  ServiceBusProcessor? _processor;

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    _processor = client.CreateProcessor(ServiceBusQueueNames.Notifications, new ServiceBusProcessorOptions());
    _processor.ProcessMessageAsync += OnMessageReceivedAsync;
    _processor.ProcessErrorAsync += args =>
    {
      logger.LogError(args.Exception, "Service Bus error while processing notifications");
      return Task.CompletedTask;
    };

    await _processor.StartProcessingAsync(stoppingToken);
  }

  async Task OnMessageReceivedAsync(ProcessMessageEventArgs args)
  {
    var messageType = args.Message.ApplicationProperties.GetValueOrDefault("MessageType") as string;

    var text = messageType switch
    {
      ServiceBusMessageTypes.ComponentCreated =>
          $"New component added: {args.Message.Body.ToObjectFromJson<ComponentCreatedEvent>()!.Name}",
      ServiceBusMessageTypes.BikeBuildCreated =>
          $"New bike build created: {args.Message.Body.ToObjectFromJson<BikeBuildCreatedEvent>()!.Name}",
      ServiceBusMessageTypes.RatingCreated =>
          FormatRatingCreated(args.Message.Body.ToObjectFromJson<RatingCreatedEvent>()!),
      ServiceBusMessageTypes.OrderPlaced =>
          FormatOrderPlaced(args.Message.Body.ToObjectFromJson<OrderPlacedEvent>()!),
      _ => null
    };

    if (text is not null)
    {
      using var activity = _traceSource.StartActivity("NotificationHub broadcast");
      activity?.SetTag("bikebuilder.message_type", messageType);
      await hubContext.Clients.All.SendAsync("ReceiveNotification", text, args.CancellationToken);

      // Order events additionally go out on a dedicated method so clients that only care
      // about orders (the authenticated WASM app) don't have to string-match the feed.
      if (messageType == ServiceBusMessageTypes.OrderPlaced)
        await hubContext.Clients.All.SendAsync("ReceiveOrderNotification", text, args.CancellationToken);
    }

    await args.CompleteMessageAsync(args.Message, args.CancellationToken);
  }

  static string FormatRatingCreated(RatingCreatedEvent rating) =>
      $"New {rating.Stars}-star rating for {rating.BikeBuildName}";

  // Invariant "$" formatting keeps the toast text machine-independent (the integration
  // test asserts on it).
  static string FormatOrderPlaced(OrderPlacedEvent order) =>
      $"New order placed by {order.CustomerName}: {order.ItemCount} item(s), ${order.Total.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}";

  public override async Task StopAsync(CancellationToken cancellationToken)
  {
    if (_processor is not null)
      await _processor.StopProcessingAsync(cancellationToken);

    await base.StopAsync(cancellationToken);
  }
}
