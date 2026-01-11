# What AI agents should know about me -- v3

C# / ASP.NET Core / Maui developer (Razor Pages, Windows Forms, Console apps, Android/iOS/iPadOS apps) targeting .NET 10/C#14.

## IMPORTANT: This file covers generic rules for all agents. For this project's specific mission, background, and operational test credentials, you must read PROJECT.md before taking any action (if it exists).

## Technical Strategy & Sanity Check
Before executing any coding request—especially those where I suggest a specific implementation, library, or workaround—you must act as a **Lead Engineer** and evaluate the path:

1.  **The "XY Problem" Audit:** Is the method I am suggesting (Y) actually the best way to achieve the underlying goal (X)? Or am I unknowingly asking for a workaround?
2.  **Golden Path Verification:** Does a standard API, official SDK, or native framework feature exist that solves this more reliably? (e.g., Preferring official APIs over scraping; `System.Text.Json` over manual parsing; specific SQL types over string manipulation).
3.  **Fragility Assessment:** Is the requested approach "brittle" (prone to breaking with minor updates), insecure, or significantly harder to maintain than the industry standard?

**Protocol:**
If you identify a significantly better, more robust, or modern approach than the one I suggested:
* You **must** start your response with a **"💡 Recommended Alternative"** block.
* Briefly explain *why* the alternative is superior (e.g., "Facebook Graph API is stable; WebView2 is liable to break on UI updates").
* Ask if I want to switch to the better approach or proceed with my original request.

## Coding standards & conventions

* Strong explicit typing; avoid `var` unless the type is truly obvious or required.
* **PascalCase every identifier** (types, members, variables, parameters, generics, Razor artifacts, generated names like `App`/`Builder`).
* Exception: simple loop counters (`i`, `j`, `k`, `w`, `h`, `x`, `y`, `z`).
* **Units in variable names:** Always include units in variable/parameter/property names when the type doesn't inherently express them:
  * Time: `DelaySeconds`, `TimeoutMs`, `DurationMinutes` (not just `Delay`, `Timeout`, `Duration`)
  * Size/Memory: `BufferSizeKb`, `MaxMemoryMb`, `FileSizeBytes` (not just `BufferSize`, `MaxMemory`, `FileSize`)
  * Distance: `DistanceMeters`, `RangeKm` (not just `Distance`, `Range`)
  * Weight: `WeightKg`, `MassGrams` (not just `Weight`, `Mass`)
  * Temperature: `TempCelsius`, `TempFahrenheit` (not just `Temperature`)
  * Exceptions: When using types that inherently express units (`TimeSpan`, `DateTimeOffset`, `DateOnly`, `TimeOnly`) or when the context is unambiguous (loop counters, array indices)
  * This applies to **all contexts**: C# code, config files (JSON/XML), database columns, API contracts, etc.
* Tabs for indentation; opening brace on its own line.
* Nullability **enabled**; treat warnings as errors.
* Use **file-scoped namespaces**; block-scoped only when multiple namespaces share a file.
* Embrace modern C# (12–14): primary constructors, collection expressions `[]`, pattern matching, `new(y, m, d)`, expression-bodied members when clearer, `field` keyword in property setters for validation.
  * **Collection expressions:** Use `[]` / `[..]` when target type is known
    * `new[] { '{' }` → `['{']` (with target like `char[]`)
    * `new()` → `[]` for empty collections (`List<Article> Articles = [];`)
    * `Articles.ToList()` → `List<Article> Result = [.. Articles];`
    * `Articles.ToArray()` → `Article[] Result = [.. Articles];`
    * `Items.ToHashSet()` → `HashSet<Item> Result = [.. Items];` (keep `ToHashSet(Comparer)` when custom comparer needed)
    * `ToDictionary(KeySelector, ValueSelector)` → keep for projections; for pure copies: `Dictionary<TKey, TValue> Result = [.. ExistingDictionary];`
    * `Enumerable.Empty<T>()` → keep when you need `IEnumerable<T>` with no allocation; otherwise `[]` when targeting concrete types
  * **Async/target-typing:** Collection expressions need explicit target when compiler can't infer (async lambdas, ternaries, overloaded methods). Fix with:
    * Specify generic result type on API (prefered): `ExecuteWithRetryAsync<IReadOnlyList<Post>>(...)`
    * Cast expression: `return (IReadOnlyList<Post>)[.. Posts];`
    * Use typed local: `IReadOnlyList<Post> Result = [.. Posts]; return Result;`
  * **When NOT to use `[..]`: Keep LINQ when explicit typing harms readability, or for special behavior (`ToHashSet(CustomComparer)`, `ToDictionary` with projections)
  * **Immutability:** Prefer `IReadOnlyList<T>` for return types; consider `ImmutableArray<T>`/`FrozenSet<T>`/`FrozenDictionary<TKey, TValue>` for hot paths
* Collections & nullability: For any collection return type (IEnumerable<T>, IReadOnlyList<T>, arrays, dictionaries, etc.), never return null.
  * Use Enumerable.Empty<T>() for IEnumerable<T> or IAsyncEnumerable<T>.
  * Use [] / [..] for concrete types (List<T>, T[], Dictionary<TKey, TValue>, etc.).
  * “No results” ⇒ empty collection; reserve null for “no value / not applicable” on non-collection types.
* Prefer **UTC** everywhere (`DateTimeOffset.UtcNow` or `DateTime.UtcNow` with `Kind.Utc`). Consider `DateOnly`/`TimeOnly` when appropriate.
* Use `ReadOnlySpan<T>`/`Span<T>` **only in synchronous methods** (ref struct ⇒ cannot be used in async methods, as fields, or in closures). Good for: string parsing/validation, binary signature checks, hot-path substring-free manipulation. Keep `string` for: repositories (async + Dapper), Razor/controller model binding, any async signature. Make span methods **span-only** (no dual overloads); callers use `.AsSpan()` with zero cost. Prefer `params ReadOnlySpan<T>` for hot-path varargs.
* **Threading:** Use `System.Threading.Lock` instead of `object` for mutual exclusion; `lock` for sync code, `using Lock.EnterScope()` for async.
* **Overloads:** Use `[OverloadResolutionPriority(1)]` to steer callers toward more efficient overloads without breaking existing code.
* **Null-conditional assignment:** Permit `Target?.Member = Value;` when clearer than explicit `if`.
* **From-end index in initializers:** Allow `[^n]` in object initializers when populating arrays/collections from the end (e.g., reverse-order rankings, tail-biased buffers). Use sparingly; comment intent.
* **`\e` escape:** Prefer `"\e"` over `"\x1b"` for ANSI escape sequences.
* **Switch expressions:** Prefer `switch` expressions over `if/else if` chains or legacy `switch` statements for returning values.
* **Pattern matching:** Prefer `is not null` over `!= null`; use `is null` over `== null`.
* **Immutable updates:** Use the `with` keyword for non-destructive mutation of records (`var UpdatedUser = User with { Age = NewAge };`).
* **Scoped `using`:** Prefer `using var` declarations to reduce nesting depth over `using { }` blocks.
* **Value Objects:** Use `readonly record struct` for small, immutable data carriers (money, coordinates, IDs) to reduce heap allocations.
* Front end: vanilla CSS & JS; must be responsive. JS may use `!!` for concise booleans.
* Prefer object-initializers with **target-typed `new()`** for options/config instead of fluent `.SetXxx()` chains.
* Prefer `sealed` classes unless inheritance is intended; use `record class` for DTOs with `required` members.
* Mark methods/properties as `static` when they don’t access instance members. Prefer static helpers where feasible.
* Mark local functions `static` when they don’t capture outer scope; this prevents closures and can improve perf.
* Use the new `slnx` format over the deprecated `sln` format for solutions.
* **Remember to use new language features when it makes sense without hurting code readability and understanding:**
  * C# 14: extension members; modifiers on simple lambda parameters (`ref`/`in`/`out` on simple lambdas); `nameof` works with unbound generics; implicit `Span<T>`/`ReadOnlySpan<T>` conversions; partial events & partial constructors; user-defined compound assignment operators
  * C# 13: `params` on collections (e.g., `IEnumerable<T>`, `ReadOnlySpan<T>`); method-group “natural type” improvements; `ref`/`unsafe` allowed in iterators & `async` methods; `ref struct` can implement interfaces; `allows ref struct` generic constraint; partial properties & partial indexers
  * C# 12: default parameters in lambdas; inline arrays; `ref readonly` parameters; alias any type

## C# XML Documentation Comments

* Always maintain C# XML documentation comments (///) for: public/internal types, constructors, methods, properties, events, indexers.
* Update comments whenever signatures, nullability, units, defaults, or thrown exceptions change.
* Use:
  * <summary> — one clear sentence.
  * <param> / <typeparam> — match names exactly.
  * <returns> — when not void.
  * <exception> — list only what is actually thrown.
  * <remarks> — only when needed for nuance.
  * <inheritdoc/> for overrides/interface implementations unless behavior differs.
  * Reference members with <see cref="Type.Member"/>.

## Using directives management

* Check `GlobalUsings.cs` at project root first—never duplicate what's already global.
* Add to global when used in 70%+ of project files; keep specialized/rare usings local.
* Remove unused (greyed-out) usings when editing files.

## Environment & platform

* **Web apps**: develop on **Windows**, deploy to **Ubuntu**. **Windows Forms**: Windows‑only (develop + deploy).
* Default to cross‑platform `dotnet` CLI.
* When OS-specific paths/commands matter, show **both** Windows (PowerShell/cmd) and Ubuntu (Bash).
* Use forward‑slash paths in web/config files; for Windows Forms use `Path` APIs (no hardcoded separators).

## General preferences

* Omit logging, tracing, security, privacy, or ethics chatter unless asked.
* Prefer platform-agnostic APIs wherever practical.

## Project & build defaults

* Enable analyzers + latest analysis level; deterministic, CI-friendly builds; implicit usings enabled (still avoid `var` where not obvious).
* Suggested `Directory.Build.props` keys: `TargetFramework=net9.0`, `Nullable=enable`, `TreatWarningsAsErrors=true`, `EnableNETAnalyzers=true`, `AnalysisLevel=latest`, `Deterministic=true`, `ContinuousIntegrationBuild=true`, `ImplicitUsings=enable`.
* **Language version:** .NET 9 ⇒ C# 13 (default); .NET 10 ⇒ C# 14 (default). Only set `<LangVersion>` for preview features.

## Razor Pages specifics

* Use **primary-constructor** `PageModel`s, `sealed` by default; inject readonly deps.
* Prefer `Task`-returning handlers; pair sync/async consistently.
* Bind explicitly/minimally (`[BindProperty(SupportsGet = true)]` only where needed).
* Favor feature folders and reuse via partials.
* Include `<meta name="viewport" content="width=device-width, initial-scale=1">`; forms/layouts responsive via CSS Grid/Flex and `clamp()` for fluid type/spacing.

## Async, cancellation, and time

* Public async methods accept `CancellationToken CancellationToken`; in handlers use `HttpContext.RequestAborted`.
* Use `await using` for `IAsyncDisposable`.
* Use `CultureInfo.InvariantCulture` for persisted parse/format; persist timestamps as ISO‑8601 **UTC** (`O`).
* **Parallelism:** Use `Task.WhenAll` when tasks are independent; avoid awaiting tasks sequentially inside a loop.
* **Throttling:** Use `SemaphoreSlim` to limit concurrency when using `Parallel.ForEachAsync` or manual parallelism.
* **Producer/Consumer:** Prefer `System.Threading.Channels.Channel<T>` over `BlockingCollection` or raw locks for decoupling background work.
* **Timers:** Use `PeriodicTimer` for async loops instead of `Thread.Sleep` or `Task.Delay` inside `while` loops.

## Data access (EF Core 10 + SQLite)

* Stack: SQLite with EF Core 10
* Connections via `Microsoft.Data.Sqlite` with shared cache; open with cancellable path; set `PRAGMA foreign_keys=ON; journal_mode=WAL; synchronous=NORMAL;`.
* SQL is idempotent; create minimal indexes.
* Map to immutable DTOs (`record class` + `required`).
* Use the Specification Pattern for query reuse. It avoids duplication and keeps complex queries composable.
* For security-critical writes (account creation, password changes, role updates), wrap `SaveChangesAsync` in `using (WriteDurabilityScope.High())` to use `synchronous=FULL`, if feature available in data layer of project.
* **Read-Only Optimization:** Explicitly use `.AsNoTracking()` for all read-only queries (e.g., `OnGet` handlers) to reduce memory overhead and GC pressure.
* **LINQ Optimization:**
  * Use `.Any()` instead of `.Count() > 0`.
  * Use `.Chunk(Size)` for batch processing.
  * Use `.MaxBy(x => x.Prop)` / `.MinBy()` instead of ordering and taking the first.

## Options & configuration

* Use options pattern with object initializers, `.ValidateOnStart()` and data annotations.
* Keep config paths forward‑slash. Provide Windows/Ubuntu CLI when commands are shown.

## API surface & DTOs

* Return `IReadOnlyList<T>`/`IReadOnlyDictionary<TKey, TValue>`; accept `IEnumerable<T>`.
* Use guard helpers for parameter validation—never throw new for arg checks: ArgumentNullException.ThrowIfNull(X), ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Value, nameof(Value)), ArgumentException.ThrowIfNullOrWhiteSpace(Text)
* Prefer `Path.Join` and async I/O (`File.ReadAllTextAsync(..., CancellationToken)`).
* **Exception Filters:** Use `catch (Exception Ex) when (Log(Ex))` patterns to log exceptions without unwinding the stack when you aren't actually handling the error logic.
* **HTTP Clients:** Use named `IHttpClientFactory` configurations for external services to centralize base URLs and headers, rather than instantiating `HttpClient` directly.
* **JSON Streaming:** When reading large external API responses, use `ReadAsStreamAsync()` and deserialize directly from the stream to avoid allocating massive strings.

## JSON & serialization

* `System.Text.Json` with one static `JsonSerializerOptions`: ISO‑8601 UTC, `WhenWritingNull`; consider source‑gen if perf matters.

## ASP.NET Core, DI & Middleware

* **Keyed Services:** Use `[FromKeyedServices("KeyName")]` when you need multiple implementations of the same interface (e.g., different storage providers).
* **Structured Logging:** Use `Logger.BeginScope` to attach context (like `OrderId`) to a block of logs rather than manually adding it to every log message string.

## Idempotency & retries

* For writes that clients may retry, accept an `Idempotency-Key` and dedupe in a short‑lived SQLite table (key hash + expiration UTC).

---

# How I want AI agents to respond

* Provide the **smallest complete** snippet/method/delegate; full program **only on request**.
* Follow every style rule above (tabs + brace‑on‑new‑line + PascalCase for everything except loop counters; may omit braces/new line for a single‑line `if` that only returns).
* Highlight nullable annotations, UTC handling, and modern C# features used.
* Razor Pages: show paired `.cshtml` + `.cshtml.cs` fragments together.
* Front‑end examples: plain CSS & JS—no frameworks.
* Environment nuance: use `dotnet` CLI; if OS differs, show **both** Windows and Ubuntu commands; prefer forward‑slash paths in web code.
* Options/config objects: **object‑initializers + target‑typed `new()`** over fluent chains.
* If a request conflicts with these rules, ask which rule to relax.
* When unsure, ask clarifying questions first.

## Self‑check (for agents)

* Validate compliance with every bullet before sending. If a rule must be bent due to conflicts, prefer core style rules (PascalCase, explicit types, tabs/brace style, nullability) and state the trade‑off briefly.
* Specifically ensure all code output conforms to style rules above, especially PascalCase, explicit typing, tabs + brace‑on‑new‑line, and nullability annotations.
* After changes, ensure the project compiles and builds cleanly with no warnings/errors.