namespace BikeBuilder.MCP.Tools;

// A one-call orientation for the model: what the four data sets are, how they relate, and
// which tool answers which kind of question. Small models pick the right tool far more
// reliably with this in context than from the tool descriptions alone.
[McpServerToolType]
public sealed class DataGuideTools
{
  const string Guide = """
      Bike Builder data model (all tools are read-only):

      COMPONENTS - the parts catalog. Each has id, name, cost, SKU, manufacturer (Sram, Shimano, Hope
      or Other), a description, and optional typed information (fork travel, tire width, stem length ...).
      Tools: search_components (filter/sort/page), get_component (full details incl. typed information).

      BIKE BUILDS - named bikes assembled from components, each line with a quantity. A build's total
      is the sum of component cost x quantity. Tools: search_bike_builds (sort by total to find the
      most/least expensive), get_bike_build (the component list).

      ORDERS - customer purchases from the public storefront. A placed order has an integer id, customer
      name/email/phone, status, timestamps, a shipping address (shipTo), shipping method and cost, a
      subtotal (the items), a total (subtotal + shipping), the card used (brand and last four digits
      only) and line items; each item snapshots the product name and unit price at purchase time and
      refers to a catalog product by productType (COMPONENT or BIKE_BUILD) and productId. Draft orders
      are carts still being filled in (Guid ids, an expiry) and have none of the checkout details yet.
      Only the 100 most recent placed orders are available. Tools: list_orders, get_order,
      list_draft_orders, orders_summary (revenue, counts, top products and customers - use this for
      any totals or "best selling" question). Order tools need the caller to be signed in with the
      OrderViewer or Admin role.

      RATINGS - 1 to 5 star reviews of bike builds, keyed by bike build id, with an optional comment
      (the review text) and the reviewer's name. Tools: list_ratings (one build's reviews with their
      text), search_rating_comments (review text across all builds, optionally containing a phrase
      or within a star range - use it for "what do customers say about ..."), get_rating_summaries
      (count and average for several builds), top_rated_bike_builds (ranked list; set lowestFirst
      for the worst rated).

      Formats: money values come as US dollar strings with two decimals ("$1,234.56"); dates and times
      come as "MM/dd/yyyy HH:mm" in UTC. Repeat both exactly as given. Ids are integers except draft
      order ids.
      """;

  [McpServerTool(Name = "describe_data", ReadOnly = true, Idempotent = true),
   Description("Explains the Bike Builder data sets (components, bike builds, orders, ratings), how they relate, and which tool answers which question. Call this first when unsure which tool to use.")]
  public string DescribeData() => Guide;
}
