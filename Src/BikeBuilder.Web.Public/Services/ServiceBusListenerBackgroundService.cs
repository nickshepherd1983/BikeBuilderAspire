using BikeBuilder.Contracts.Events;
using BikeBuilder.Contracts.Messaging;
using BikeBuilder.Contracts.Notifications;

namespace BikeBuilder.Web.Public.Services;

public class ServiceBusListenerBackgroundService(
    ServiceBusClient client,
    IHubContext<NotificationHub> hubContext,
    ILogger<ServiceBusListenerBackgroundService> logger) : BackgroundService
{
  // IHubContext broadcasts produce no built-in span, so this makes the trace visibly end at
  // "broadcast to SignalR clients". The span is parented on the PRODUCER's context (the
  // request that raised the event) rather than the SDK's ambient ProcessMessage span, which
  // only links to it: that is what makes a checkout read as one trace from the shopper's
  // click to the toast. The SDK span is kept as a link so nothing is lost.
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
    var message = args.Message;
    var messageType = message.ApplicationProperties.GetValueOrDefault("MessageType") as string;
    var hasProducer = MessageTraceContext.TryGetProducerContext(message.ApplicationProperties, out var producer);

    // Every log line for this message carries its ids; the OTLP pipeline adds the trace ids.
    using var scope = logger.BeginScope(new Dictionary<string, object?>
    {
      ["MessageId"] = message.MessageId,
      ["CorrelationId"] = message.CorrelationId,
      ["MessageType"] = messageType
    });

    var processSpan = Activity.Current;
    if (hasProducer)
      processSpan?.SetTag("bikebuilder.producer_trace_id", producer.TraceId.ToHexString());

    var (text, orderId) = Format(messageType, message.Body);
    if (text is not null)
    {
      await BroadcastAsync(text, messageType!, orderId, hasProducer, producer, processSpan, args.CancellationToken);
      // Stopping a remote-parented activity leaves Activity.Current null (its Parent object is
      // not in this process); put the SDK's span back so the completion below stays inside it.
      Activity.Current = processSpan;
    }

    await args.CompleteMessageAsync(message, args.CancellationToken);
  }

  async Task BroadcastAsync(string text, string messageType, int? orderId, bool hasProducer, ActivityContext producer,
      Activity? processSpan, CancellationToken cancellationToken)
  {
    ActivityLink[]? links = processSpan is null ? null : [new ActivityLink(processSpan.Context)];
    using var activity = hasProducer
        ? _traceSource.StartActivity("NotificationHub broadcast", ActivityKind.Producer, producer, links: links)
        : _traceSource.StartActivity("NotificationHub broadcast");
    activity?.SetTag("bikebuilder.message_type", messageType);
    if (orderId is not null)
      activity?.SetTag("bikebuilder.order_id", orderId);

    // The toast carries the originating request's trace id, so a toast in a browser can be
    // followed back to the order or rating behind it.
    var traceId = hasProducer ? producer.TraceId.ToHexString() : activity?.TraceId.ToHexString();
    var envelope = new NotificationMessage(text, messageType, traceId);

    await hubContext.Clients.All.SendAsync("ReceiveNotification", envelope, cancellationToken);

    // Order events additionally go out on a dedicated method so clients that only care
    // about orders (the authenticated WASM app) don't have to string-match the feed.
    if (messageType == ServiceBusMessageTypes.OrderPlaced)
      await hubContext.Clients.All.SendAsync("ReceiveOrderNotification", envelope, cancellationToken);
  }

  static (string? Text, int? OrderId) Format(string? messageType, BinaryData body)
  {
    switch (messageType)
    {
      case ServiceBusMessageTypes.ComponentCreated:
        return ($"New component added: {body.ToObjectFromJson<ComponentCreatedEvent>()!.Name}", null);
      case ServiceBusMessageTypes.BikeBuildCreated:
        return ($"New bike build created: {body.ToObjectFromJson<BikeBuildCreatedEvent>()!.Name}", null);
      case ServiceBusMessageTypes.RatingCreated:
        return (FormatRatingCreated(body.ToObjectFromJson<RatingCreatedEvent>()!), null);
      case ServiceBusMessageTypes.OrderPlaced:
        var order = body.ToObjectFromJson<OrderPlacedEvent>()!;
        return (FormatOrderPlaced(order), order.OrderId);
      default:
        return (null, null);
    }
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
