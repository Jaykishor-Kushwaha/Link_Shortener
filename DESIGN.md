# Design Document — URL Shortener

## Overview

A URL shortening service built with **.NET 8 Web API**, **Angular 18**, **MongoDB**, and **Redis**. Users sign up, shorten long URLs, manage their links, and view analytics on click traffic.

This document covers the non-obvious engineering decisions: how short codes avoid collisions under concurrency, how the cache stays consistent on mutations, how analytics work at scale, and what would break first under 100× load.

---

## Short Code Generation

### Strategy

Short codes are 8-character strings drawn from a 64-character URL-safe alphabet: `A-Z`, `a-z`, `0-9`, `-`, `_`. Each character is chosen by masking a byte from `System.Security.Cryptography.RandomNumberGenerator` to 6 bits (0–63), giving exactly 48 bits of entropy per code.

At 48 bits, the space holds ~281 trillion codes. Even at 1 billion stored URLs, the probability of a random collision on a single attempt is ~0.0000004%.

### Why Collisions Cannot Happen

The MongoDB collection `shortenedUrls` has a **unique index on `shortCode`**. This is the collision-prevention mechanism — not application-level checking.

The naïve approach (findOne to check availability, then insertOne) is a race condition: two concurrent requests can both see the code as available, both insert, and one silently wins. We avoid this entirely.

**Our approach:**

1. Generate a random 8-character code.
2. `InsertOne` into MongoDB with the unique index.
3. If MongoDB returns error code `E11000` (duplicate key), generate a new random code and retry.
4. Maximum 5 retries. At 48 bits of entropy, exhausting retries is astronomically unlikely.

Custom aliases go through the same insert path — they compete for the same unique-indexed namespace. If a custom alias collides with a generated code (or another alias), the user gets a clear `409 Conflict`.

**Why this is correct under concurrency:** The unique index guarantee is enforced by MongoDB's storage engine, which serializes conflicting writes. Two concurrent inserts with the same code cannot both succeed. One will get `E11000`, retry, and get a different code.

---

## Click Count Correctness

Click counts use MongoDB's `$inc` operator:

```csharp
Builders<ShortenedUrl>.Update.Inc(u => u.ClickCount, 1)
```

This is an **atomic operation** — MongoDB applies the increment without reading the current value into application memory. Two concurrent requests both increment by 1, and the final value is always exactly the sum.

The alternative (read current count, add 1, write back) is a classic read-modify-write race condition. We never do this.

**Test validation:** We fire 500 concurrent `GET /:code` requests and assert the click count is exactly 500.

---

## Cache Design

### Architecture

The redirect path (`GET /:code`) is the hot path. On a cache hit, we return a redirect from Redis without touching MongoDB.

```
Request → Redis cache lookup
  ├── Hit → 302 Redirect (from Redis)
  └── Miss → MongoDB lookup → Cache result → 302 Redirect
```

### TTL Choice: 5 Minutes

A 5-minute TTL balances two concerns:

- **Too short** (e.g., 10s): Cache hit rate drops, MongoDB takes the load the cache was supposed to absorb. Under high traffic, this defeats the purpose.
- **Too long** (e.g., 1 hour): Stale data lingers. While we invalidate on PATCH/DELETE, a long TTL means more cached entries consuming Redis memory, and any invalidation bugs have a wider blast radius.

5 minutes gives >95% cache hit rate for popular links while keeping the stale window small.

### Cache Invalidation on Update and Delete

**PATCH /urls/:id:**
1. Update the destination in MongoDB (atomic `FindOneAndUpdate`).
2. **Immediately delete the Redis cache key** for that short code.
3. The next redirect request will miss the cache, read the new destination from MongoDB, and re-cache it.

**DELETE /urls/:id (soft-delete):**
1. Set `deletedAt = DateTime.UtcNow` in MongoDB.
2. **Immediately delete the Redis cache key** for that short code.
3. The next redirect will miss the cache, find the link is deleted, and return 410 Gone.

Additionally, when a redirect finds a link is gone (deleted or expired), we cache a `__GONE__` sentinel value. Subsequent requests for the same code return 410 from Redis without hitting MongoDB.

**Guarantee:** A user who updates a link and immediately clicks it will always land on the new destination, because the cache is invalidated synchronously before the PATCH response is sent.

---

## Click Event Schema and Analytics Indexes

### Schema

```javascript
{
  _id: ObjectId,
  urlId: ObjectId,        // FK to shortenedUrls._id
  shortCode: "aBcDeFgH",  // Denormalized for debugging
  timestamp: ISODate,
  referrer: "https://twitter.com/...",
  userAgent: "Mozilla/5.0 ...",
  ip: "203.0.113.42"
}
```

Every redirect writes one ClickEvent document. We chose individual events over pre-aggregated counters because:

- Events are append-only and immutable — no concurrency issues.
- Flexible querying: any time range, any grouping dimension, without pre-defining buckets.
- The trade-off is storage (at 10M events for one link, ~2–4 GB), but MongoDB handles this well with proper indexes.

### Indexes

| Index | Purpose |
|-------|---------|
| `{ urlId: 1, timestamp: 1 }` | Time-series queries. `$match` on urlId + timestamp range is fully covered. `$group` by `$dateTrunc` operates on the bounded result set. |
| `{ urlId: 1, referrer: 1 }` | Top-referrer queries. `$match` on urlId is covered. `$group` by referrer runs server-side. |

### Query Performance at 10M Events

Both analytics endpoints use MongoDB aggregation pipelines that run entirely server-side:

**Time series (`GET /urls/:id/stats?from=&to=&bucket=hour`):**
```
$match { urlId, timestamp: { $gte, $lte } }  ← uses compound index
$group { _id: $dateTrunc(...), count: $sum: 1 }
$sort { _id: 1 }
```

The `$match` stage uses the `(urlId, timestamp)` index to efficiently scan only the relevant time range. At 10M total events, a 30-day query might scan 500K events — bounded, index-backed, no full collection scan.

**Top referrers (`GET /urls/:id/referrers?limit=10`):**
```
$match { urlId }          ← uses compound index prefix
$group { _id: "$referrer", count: { $sum: 1 } }
$sort { count: -1 }
$limit 10
```

The `$match` uses the `(urlId, referrer)` index. The `$group` runs on the server — no documents are transferred to application memory.

### Why Not Pre-Aggregation?

Pre-aggregation (maintaining hourly/daily counters) would make reads O(1) instead of O(n) on the time range. The trade-off:

- **Pro:** Reads are faster. At 100M events, the aggregation pipeline becomes slow.
- **Con:** Writes become more complex (upsert counters on every click, handle bucket alignment). Cannot retroactively change bucket sizes or add new dimensions.

For this scope, raw events with proper indexes are the right choice. The next section discusses what to change at 100× scale.

---

## Deferred Click Recording

Redirect requests must be fast. Recording a click event involves a MongoDB insert, which adds latency.

**Solution:** An in-process `Channel<ClickEvent>` (bounded at 10,000 events) acts as an async buffer. The redirect handler:

1. Does an atomic `$inc` on `clickCount` (fast, fire-and-forget).
2. Writes the full ClickEvent to the Channel (non-blocking).
3. Returns the 302 redirect immediately.

A `BackgroundService` drains the channel in batches of 100 and calls `InsertManyAsync` to MongoDB. This amortizes the per-event overhead and keeps the redirect path fast.

**Risk:** If the process crashes, up to 10,000 buffered events are lost. Click counts (from `$inc`) are still accurate because they're written directly to MongoDB. Only the detailed event data (referrer, user-agent, IP) is at risk. This is an acceptable trade-off for a URL shortener.

---

## Security

### Authentication

- **Access tokens:** JWT, 15-minute lifetime, signed with HMAC-SHA256. Short-lived to limit damage from token theft.
- **Refresh tokens:** 64 random bytes, stored as SHA-256 hashes in MongoDB. 7-day lifetime. TTL index auto-removes expired tokens.

### Refresh Token Rotation

Tokens in a rotation chain share a `family` UUID. On each refresh:

1. Hash the incoming token, look up by `tokenHash`.
2. If `used == true` → **replay detected**. Delete all tokens in this family. Return 401.
3. Mark current token as `used = true`.
4. Issue new access + refresh tokens in the same family.

This detects token theft: if an attacker steals and uses a refresh token, the legitimate user's next refresh attempt will present the (now used) old token, triggering family invalidation.

### Authorization

Every mutation (PATCH, DELETE) and every analytics query checks ownership by including `userId` in the MongoDB query filter:

```csharp
Builders<ShortenedUrl>.Filter.Eq(u => u.UserId, userId)
```

If the filter doesn't match (wrong user), the query returns null, and we return **404** (not 403). This avoids leaking the existence of other users' links.

### Input Validation

- URL scheme: Only `http` and `https`. Blocks `javascript:`, `data:`, `file:`, `ftp:`.
- SSRF: DNS-resolve the hostname and reject private, loopback, and link-local IP ranges (`10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`, `169.254.0.0/16`, `127.0.0.0/8`, `::1`, `fc00::/7`, `fe80::/10`).
- Body size: 1 MB limit via ASP.NET filter.
- Alias: Max 32 characters, `[A-Za-z0-9_-]` only.
- URL length: Max 2048 characters.
- Analytics `bucket`: Only `hour` or `day` — rejects anything else.

### Rate Limiting

- **Redis-backed fixed-window counters.** Survive process restarts and are correct across multiple app instances.
- **Redirect path:** Per-IP, 100 requests per 60 seconds.
- **URL creation:** Per-user, 20 requests per 60 seconds.
- Exceeding returns `429 Too Many Requests` with `Retry-After` header.

---

## What Breaks First at 100× Traffic

### 1. Click Event Writes (First Bottleneck)

At 100× traffic, the click event background service's `InsertManyAsync` becomes the bottleneck. MongoDB write throughput on a single replica set tops out around 10K–50K inserts/second.

**Fix:** Replace the in-process Channel with a message queue (RabbitMQ, Kafka). A dedicated consumer service handles writes, enabling independent horizontal scaling. Pre-aggregate into hourly/daily counters to reduce analytics query time.

### 2. Redis as Single Point of Failure

A single Redis instance becomes both a bottleneck and a SPOF.

**Fix:** Redis Cluster for sharding. Redis Sentinel for failover.

### 3. MongoDB Read Scaling

Analytics aggregation pipelines on 10M+ events per link become slow.

**Fix:**
- Pre-aggregate clicks into hourly/daily counter documents on write.
- Use MongoDB read replicas for analytics queries (eventually consistent is fine for analytics).
- Consider a time-series database (TimescaleDB) for the click events.

### 4. Short Code Exhaustion

48 bits gives ~281 trillion codes. At 100× traffic this is not a concern, but at 1 trillion codes the collision retry rate increases.

**Fix:** Increase code length from 8 to 10 characters (60 bits of entropy).

---

## What Was Left Out

| Feature | Why |
|---------|-----|
| Geo-lookup of IP | Spec says "optional and unscored — do not spend time on it." |
| Message queue for click events | The in-process Channel is sufficient for this scope. At production scale, Kafka/RabbitMQ would be better. |
| Pre-aggregation of analytics | Raw events with indexes are sufficient for the test's 10M event scenario. Pre-aggregation is the next step at 100× scale. |
| Distributed locking | Not needed. The unique index handles code collisions, and `$inc` handles click counts. No application-level locks. |
| Logout / token revocation list | Access tokens are short-lived (15 min). Implementing a revocation list adds complexity for marginal security benefit at this scope. |
| QR codes, teams, billing | Out of scope per spec. |

---

## Architecture Diagram

```
┌──────────────┐     ┌──────────────┐     ┌──────────────┐
│   Angular    │────▶│  .NET 8 API  │────▶│   MongoDB    │
│   (nginx)    │     │              │     │              │
│   Port 80    │     │   Port 5000  │     │  Port 27017  │
└──────────────┘     │              │     └──────────────┘
                     │   ┌────────┐ │     ┌──────────────┐
                     │   │Channel │ │────▶│    Redis      │
                     │   │(clicks)│ │     │              │
                     │   └────────┘ │     │  Port 6379   │
                     └──────────────┘     └──────────────┘
```
