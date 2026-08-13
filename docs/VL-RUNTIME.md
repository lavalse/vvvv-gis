# How a VL node is evaluated — field notes

[VL-PACKAGING.md](VL-PACKAGING.md) covers getting a node to **exist**. This covers what happens
once it **runs**, which turns out to be the other half and the more expensive one.

Two incidents in this project came from the same gap, five months apart:

| | cost |
|---|---|
| `0.1.0-alpha` deadlocked vvvv on the first tile fetch | a published release that had to be unlisted |
| A Mapsui spike opened 17,000 TCP connections in 13 minutes | **took down the author's home network** |

Both were written by someone who knew the packaging rules cold and had never written down the
evaluation model. Hence this document.

---

## Contents

- [The one sentence that matters](#the-one-sentence-that-matters)
- [Before writing any node — four questions](#before-writing-any-node--four-questions)
- [Stateless operations vs process nodes](#stateless-operations-vs-process-nodes)
- [Incident 1 — blocking on a task](#incident-1--blocking-on-a-task)
- [Incident 2 — allocating per frame](#incident-2--allocating-per-frame)
- [Manners on someone else's infrastructure](#manners-on-someone-elses-infrastructure)
- [Proving it before opening the GUI](#proving-it-before-opening-the-gui)

---

## The one sentence that matters

**A `public static` method becomes a stateless operation, and vvvv evaluates it on every
frame — sixty times a second, forever, from the moment the document is opened.**

Everything below follows from that. Opening a `.vl` *is* running it; there is no "loaded but
idle" state. A node that allocates something on each call allocates sixty of them per second.

## Before writing any node — four questions

Ask these before the first line, not after the first bug:

1. **Does it acquire anything that is not plain memory?** A network connection, a file handle,
   a GPU resource, a cache, a thread, a subscription. If yes it **must be a process node** —
   see below — because sixty of them per second is the default outcome, not an edge case.
2. **Does it touch the network?** Then it also needs a gate that is **off by default**, a local
   cache, and a User-Agent that identifies the package.
3. **Does it block?** `.Result`, `.Wait()`, `.GetAwaiter().GetResult()` on the calling thread
   deadlock vvvv. Return `IObservable`, or wrap in `Task.Run` so the caller's
   `SynchronizationContext` is not in play.
4. **Who disposes it, and when?** If the answer is "nobody", it leaks for as long as vvvv runs.

## Stateless operations vs process nodes

| | becomes | evaluated |
|---|---|---|
| `public static` method | a stateless operation | **every frame** |
| class with `[ProcessNode]` | a stateful process node | constructed **once**, `Update` every frame |

`VL.Core.Import.ProcessNodeAttribute` is the mechanism, and it is what vvvv itself uses:
`VL.Skia`, `VL.CoreLib`, `VL.Stride.Runtime`, `VL.Buffers`, `VL.TPL.Dataflow` and — the most
relevant precedent for anything networked — **`VL.IO.Redis`**.

```csharp
[ProcessNode(Name = "OpenStreetMap", Category = "Mapsui")]
public class OpenStreetMapNode : IDisposable
{
    Map? _map;
    double _lon = double.NaN;          // cannot equal any first input

    public ILayer? Update(double centerLongitude, /* ... */ bool enabled = false)
    {
        if (!enabled) { Release(); return null; }
        if (_map is null || centerLongitude != _lon) { Release(); _map = Build(...); _lon = ...; }
        return _layer;
    }

    public void Dispose() => Release();
}
```

Three details that are easy to leave out and all matter:

- **Rebuild only when an input actually changed.** Rebuilding on every frame through a process
  node is the same bug wearing a different hat.
- **Release the old one first**, and abort work in flight before disposing — a request that
  outlives the object that asked for it is exactly how connections pile up.
- **Not every input belongs in the identity.** Toggling a diagnostics overlay must not throw
  away every tile already fetched.

The rule generalises past this repository: **a node whose misuse melts something is a badly
designed node.** Do not rely on the patch author remembering to add a `Cache` region; make the
shape of the node make it impossible.

## Incident 1 — blocking on a task

`FetchTileBytes` in `0.1.0-alpha`:

```csharp
return tileSource.GetTileAsync(client, info).Result;   // deadlock
```

vvvv's runtime thread owns a `SynchronizationContext`. The continuation is posted back to that
thread, which is blocked waiting for it. vvvv's window closed without the process exiting.

```csharp
return Task.Run(() => tileSource.GetTileAsync(client, info)).GetAwaiter().GetResult();
```

**And note how it was almost missed**: running the same node from PowerShell returned a perfect
tile every time, because PowerShell installs no `SynchronizationContext`, so the bug was
physically impossible to reproduce there. See
[False proofs](VL-PACKAGING.md#false-proofs--verification-that-could-not-have-failed).

## Incident 2 — allocating per frame

A map node written as `public static ILayer OpenStreetMapLayer(...)`, which builds a `Map` and a
tile layer. Measured after 13 minutes:

```
17,085 TCP connections     87,202 handles     1,294 threads     3.1 GB
```

The machine's ephemeral port range is 49152–65535 — **16,384 ports for the whole system**. Once
those were gone every program on the machine lost DNS, and it read as "the internet is down".

**The same bug is why nothing ever rendered.** Tiles requested on frame N arrived after frame
N's map had already become garbage, so the layer was permanently busy and permanently empty. A
blank window and a network outage, one cause. It is worth expecting that shape: *a resource bug
usually breaks the feature too, so a mysterious blank is a reason to look at lifetime.*

**The evidence was in hand an hour before it was read.** The exception's stack said:

```
at VL.Mapsui.MapNodes.OpenStreetMapLayer(...)
at _Spike_.Main.SpikeApplication_PProgram_.Update__TRACE__(...)
```

`Update__TRACE__` is the evaluation context, not decoration. **Read a vvvv stack trace for
*which method it was called from*, not only for where it threw** — `Create` means once,
`Update` means every frame.

## Manners on someone else's infrastructure

OpenStreetMap's tiles run on donated hardware and its
[usage policy](https://operations.osmfoundation.org/policies/tiles/) forbids bulk downloading
and requires a User-Agent identifying the application. This project violated all of it by
accident. Defaults for anything pointing at a free public service:

- **The gate is off by default.** Opening a document must produce zero requests. The person
  opening it has not agreed to anything yet.
- **Identify yourself.** `OpenStreetMap.CreateTileLayer(userAgent)` takes one; the default is
  not acceptable beyond a first trial.
- **Cache to disk**, so a development session does not refetch the same tiles every restart.
- **Show a counter of what was allocated**, on screen. This failure was discovered when a home
  network died; a number that climbs instead of stopping at 1 would have shown it in seconds.
  Put the number where it is impossible to miss, and colour it when it is wrong.

## Proving it before opening the GUI

`vvvvc.exe <document> --output-directory <dir>` writes the C# it generated. Read it. It answers
lifetime questions exactly, with no GUI and no network:

```csharp
// in Create(...)  -> built once, held as state. Correct.
OpenStreetMap_9 = new n13.OpenStreetMapNode();
public n13.OpenStreetMapNode __p_LknvD7RmGmp8nkXIRmQcAd;

// in Update(...)  -> called per frame on the existing instance. Correct.
var Result_22 = OpenStreetMap_21.Update(centerLongitude: ..., enabled: ...);
```

Compare with the broken version, where the constructor call itself sat in `Update`.

The same file answers other questions the GUI only hints at. When two nodes appeared greyed out,
it showed `Renderer_20.Update(Input_In: Input_21, ...)` — a default local where the layer should
have been, because VL had silently built no node for a signature mentioning a type it had not
imported, and dropped every link to it.

**Then, in the GUI, in this order:**

1. Launch with the gate **off**. Confirm zero connections:
   `Get-NetTCPConnection | Where-Object { $_.OwningProcess -eq <pid> }`
2. Turn the gate on. Watch the allocation counter reach its expected value and **stop**.
3. Close vvvv as soon as the reading is taken.

**Never leave vvvv running unattended, and never start it in the background.** Launching it runs
the patch. Repeated launches accumulate: this project's leak grew across several sessions
because each one was left open.
