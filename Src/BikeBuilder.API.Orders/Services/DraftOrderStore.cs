using System.Text.Json;
using Polly;
using StackExchange.Redis;

namespace BikeBuilder.API.Orders.Services;

// The only code in this service that talks to Redis. Draft carts are stored one JSON blob
// per key under a sliding one-hour TTL, with a sorted set alongside as the "list all drafts"
// index the back office needs (Redis can't enumerate by pattern without SCAN).
//
// Transient faults: the "redis" pipeline (Program.cs) retries connection drops and timeouts
// around the commands that are safe to repeat - GET, SET, ZADD, ZREM all converge on the same
// state however many times they run. ClaimAsync is the exception, see there.
public class DraftOrderStore(IConnectionMultiplexer _redis, [FromKeyedServices(DraftOrderStore.RetryPipelineKey)] ResiliencePipeline _retry)
{
  public const string RetryPipelineKey = "redis";

  public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

  // The index. Members are draft ids, scored by creation time so ListAsync gets its ordering
  // from Redis rather than sorting in memory.
  const string IndexKey = "order:drafts";

  // Web defaults so the payload matches the camelCase the rest of the stack speaks; a draft
  // written by one instance has to be readable by every other.
  static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

  static RedisKey KeyFor(Guid id) => $"order:draft:{id}";

  public async Task<DraftOrder?> GetAsync(Guid id)
  {
    var payload = await _retry.ExecuteAsync(async _ => await _redis.GetDatabase().StringGetAsync(KeyFor(id)));
    return payload.IsNullOrEmpty ? null : Deserialize(payload);
  }

  // Writes the cart and (re)starts its hour. Called on every mutation, which is what makes
  // the TTL sliding: an actively shopping visitor never has their cart expire under them.
  public async Task SaveAsync(DraftOrder draft)
  {
    draft.ExpiresAt = DateTimeOffset.UtcNow.Add(Lifetime);
    var payload = JsonSerializer.Serialize(draft, _jsonOptions);

    await _retry.ExecuteAsync(async _ =>
    {
      var db = _redis.GetDatabase();
      await db.StringSetAsync(KeyFor(draft.Id), payload, Lifetime);
      await db.SortedSetAddAsync(IndexKey, draft.Id.ToString(), draft.CreatedAt.ToUnixTimeMilliseconds());
    });
  }

  /// <summary>
  /// Atomically fetches a draft and removes it, so exactly one caller can ever claim a given
  /// cart for processing.
  /// </summary>
  /// <remarks>
  /// This replaces the SQL rowversion token that used to turn a concurrent double-process
  /// into a concurrency exception. GETDEL is a single round trip, so the loser of a race gets
  /// null back rather than a second copy of the cart.
  ///
  /// Deliberately NOT retried: a GETDEL that executed but whose reply was lost to a timeout
  /// would come back null on the retry, and the shopper's cart would vanish behind an
  /// ORDER_NOT_FOUND. Failing the request and leaving the cart in place is the better outcome.
  /// </remarks>
  public async Task<DraftOrder?> ClaimAsync(Guid id)
  {
    var db = _redis.GetDatabase();
    var payload = await db.StringGetDeleteAsync(KeyFor(id));
    if (payload.IsNullOrEmpty)
      return null;

    await db.SortedSetRemoveAsync(IndexKey, id.ToString());
    return Deserialize(payload);
  }

  // Newest first, matching the back office's Orders list.
  public async Task<List<DraftOrder>> ListAsync() => await _retry.ExecuteAsync<List<DraftOrder>>(async _ =>
  {
    var db = _redis.GetDatabase();
    // Fully qualified: the global using of Data.Entities puts an Order entity in scope too.
    var ids = await db.SortedSetRangeByRankAsync(IndexKey, order: StackExchange.Redis.Order.Descending);
    if (ids.Length == 0)
      return [];

    var payloads = await db.StringGetAsync([.. ids.Select(id => (RedisKey)$"order:draft:{id}")]);

    // A key's TTL doesn't reach into the sorted set, so expired carts linger as index
    // members with nothing behind them. Prune them as we notice them - cheaper and simpler
    // than a sweeper, and this is the only reader.
    var drafts = new List<DraftOrder>(payloads.Length);
    var expired = new List<RedisValue>();
    for (var i = 0; i < payloads.Length; i++)
    {
      if (payloads[i].IsNullOrEmpty)
        expired.Add(ids[i]);
      else
        drafts.Add(Deserialize(payloads[i]));
    }

    if (expired.Count > 0)
      await db.SortedSetRemoveAsync(IndexKey, [.. expired]);

    return drafts;
  });

  static DraftOrder Deserialize(RedisValue payload) =>
      JsonSerializer.Deserialize<DraftOrder>(payload.ToString(), _jsonOptions)
      ?? throw new InvalidOperationException("Stored draft order was empty.");
}
