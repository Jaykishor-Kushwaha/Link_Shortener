# URL Shortener

A URL shortening service built with .NET 8 Web API, Angular 18, MongoDB, and Redis.

## Quick Start

```bash
# Clone the repository
git clone <repo-url>
cd url_shortner

# Start everything with Docker Compose
docker compose up --build
```

The application will be available at:
- **Frontend:** http://localhost:4200
- **API:** http://localhost:5000
- **Swagger:** http://localhost:5000/swagger (development only)
- **Health Check:** http://localhost:5000/health

## Environment Variables

Copy `.env.example` to `.env` and configure:

| Variable | Default | Description |
|----------|---------|-------------|
| `MONGO_URI` | `mongodb://mongo:27017/url_shortener` | MongoDB connection string |
| `REDIS_URL` | `redis://redis:6379` | Redis connection string |
| `PORT` | `5000` | API server port |
| `BASE_URL` | `http://localhost:5000` | Base URL for generated short links |
| `JWT_SECRET` | — | Secret key for signing access tokens (min 32 chars) |
| `JWT_REFRESH_SECRET` | — | Secret key for refresh tokens (min 32 chars) |
| `JWT_ACCESS_EXPIRY` | `15m` | Access token lifetime (e.g., `15m`, `1h`) |
| `JWT_REFRESH_EXPIRY` | `7d` | Refresh token lifetime (e.g., `7d`, `30d`) |
| `RATE_LIMIT_REDIRECT_MAX` | `100` | Max redirects per IP per window |
| `RATE_LIMIT_REDIRECT_WINDOW_SECONDS` | `60` | Redirect rate limit window |
| `RATE_LIMIT_CREATE_MAX` | `20` | Max URL creations per user per window |
| `RATE_LIMIT_CREATE_WINDOW_SECONDS` | `60` | Creation rate limit window |
| `CACHE_TTL_SECONDS` | `300` | Redis cache TTL for URL mappings |

## Running Tests

Tests use [Testcontainers](https://testcontainers.com/) to spin up real MongoDB and Redis instances in Docker. **Docker must be running.**

```bash
# Run all tests
dotnet test

# Run with verbose output
dotnet test --verbosity normal

# Run a specific test class
dotnet test --filter "FullyQualifiedName~ConcurrencyTests"
```

### Test Coverage

| Test Suite | Part | What It Validates |
|------------|------|-------------------|
| `ConcurrencyTests` | 2 | 200 concurrent creates → 200 unique codes; 500 concurrent clicks → exact count |
| `CacheInvalidationTests` | 3 | PATCH then redirect → new destination; DELETE then redirect → 410 |
| `AuthorizationTests` | 5 | User A cannot PATCH/DELETE/read stats of user B's links |
| `SecurityTests` | 5 | Refresh replay detection; `javascript:` URLs rejected; SSRF blocked |

## API Endpoints

### Authentication
- `POST /auth/signup` — Create account (email + password)
- `POST /auth/login` — Get access + refresh tokens
- `POST /auth/refresh` — Rotate refresh token

### URLs (authenticated)
- `POST /urls` — Shorten a URL
- `GET /urls` — List your links (paginated)
- `PATCH /urls/:id` — Update destination
- `DELETE /urls/:id` — Soft-delete a link

### Redirect (public)
- `GET /:code` — Redirect to destination (302)

### Analytics (authenticated, owner-only)
- `GET /urls/:id/stats?from=&to=&bucket=hour|day` — Click time series
- `GET /urls/:id/referrers?limit=10` — Top referrers

### Operations
- `GET /health` — MongoDB + Redis reachability

## Project Structure

```
url_shortner/
├── url_shortner/           # .NET 8 Web API
│   ├── Configuration/      # AppSettings
│   ├── Models/             # MongoDB documents
│   ├── DTOs/               # Request/response DTOs
│   ├── Services/           # Business logic
│   ├── Controllers/        # API endpoints
│   ├── Middleware/          # Exception handler, rate limiter
│   ├── Infrastructure/     # MongoDB context, Redis service
│   ├── Validators/         # URL validation, SSRF protection
│   └── Program.cs          # DI wiring + middleware pipeline
├── url_shortner.tests/     # Integration tests (xUnit + Testcontainers)
├── client/                 # Angular 18 SPA
├── docker-compose.yml
├── DESIGN.md               # Engineering design decisions
└── .env.example
```

## Architecture

See [DESIGN.md](DESIGN.md) for detailed engineering decisions covering:
- Short code generation and collision prevention
- Cache invalidation strategy
- Click event schema and analytics indexes
- What breaks first at 100× traffic
