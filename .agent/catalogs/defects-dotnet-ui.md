# MAUI / WinForms / WPF Defect Catalog

For the reviewer agent. These stacks share a defining property: **the dominant
defect class is object retention**, and it is the class an LLM is least able to
verify. Every leak entry below must route to a tool.

## MAUI

| # | Defect | Why missed | Rule |
|---|---|---|---|
| M1 | Page/View subscribes to a ViewModel event, never unsubscribes | Navigation stack retains every visited page | CA2213 partial, `manual` |
| M2 | `PropertyChanged` handler attached directly instead of via `WeakEventManager` | Classic .NET leak: the publisher outlives the subscriber | `manual` |
| M3 | `BindingContext` set to a long-lived singleton holding a page reference | Whole visual tree retained | `manual` + `leak-dotnet` |
| M4 | `MessagingCenter`/`WeakReferenceMessenger` subscription not unsubscribed | `MessagingCenter` holds strong refs | `manual` |
| M5 | Handler/platform view not disconnected (`DisconnectHandler`) | Native peer retained | `manual` + `leak-dotnet` |
| M6 | UI touched off the main thread without `MainThread.BeginInvokeOnMainThread` | Crashes on device, fine in some emulators | `manual` + VSTHRD001 |
| M7 | `async void` event handler swallowing exceptions | Silent failure | VSTHRD100 |
| M8 | `Task.Result`/`.Wait()` on the UI thread | Deadlock | VSTHRD002 |
| M9 | Timer/`IDispatcherTimer` not stopped on disappearing | Battery + leak | CA2213 |
| M10 | Image source loaded at full resolution into a list cell | Memory spike | `manual` |
| M11 | `CollectionView` without `ItemsUpdatingScrollMode`/recycling on a large set | | `manual` |

## WinForms

| # | Defect | Why missed | Rule |
|---|---|---|---|
| W1 | `Graphics`, `Font`, `Brush`, `Pen`, `Bitmap` not disposed | GDI handle exhaustion — app dies after hours | **CA2000** |
| W2 | Control created and added but never disposed when removed | | CA2000, CA2213 |
| W3 | Event handler on a long-lived control referencing a short-lived form | Form never collected | `manual` |
| W4 | Cross-thread control access without `Invoke`/`BeginInvoke` | Intermittent `InvalidOperationException` | `manual` + VSTHRD001 |
| W5 | `components` container not disposed in `Dispose(bool)` | Designer-generated, easy to break by hand | CA2213 |
| W6 | `DataGridView` bound to a large `DataTable` with no paging | | `manual` |
| W7 | `async void` handler with no try/catch | Process crash | VSTHRD100 |
| W8 | Static event subscription from an instance | Permanent retention | `manual` |
| W9 | `Application.DoEvents()` used to "fix" responsiveness | Reentrancy bugs | Banned-API gate |
| W10 | Owner-drawn control creating a new `Font` per paint | Handle churn | CA2000 |

## WPF

| # | Defect | Rule |
|---|---|---|
| P1 | `INotifyPropertyChanged` handler not detached (strong ref from the binding source) | `manual` |
| P2 | `CollectionChanged` on a shared `ObservableCollection` not detached | `manual` |
| P3 | `DependencyProperty` registered with the wrong owner type | `manual` |
| P4 | Static/`CommandManager` event subscription retaining a view | `manual` |
| P5 | `Freezable` not frozen, so it is cloned per use | `manual` |
| P6 | `Dispatcher.Invoke` used where `BeginInvoke` is needed → deadlock | VSTHRD002 |
| P7 | Binding to a method result rather than a property | `manual` |

## Syncfusion (MAUI / WinForms / WPF)

| # | Defect | Rule |
|---|---|---|
| S1 | `SyncfusionLicenseProvider.RegisterLicense` not called before the first control is created | `syncfusion-license` gate |
| S2 | License key in committed source rather than config | `secrets` gate |
| S3 | `SfDataGrid` control instance not disposed when its host closes | CA2213 + `leak-dotnet` |
| S4 | `SfDataGrid.DataSource` bound to an unbounded in-memory collection | `manual` |
| S5 | Theme/style resources loaded per form instead of once | `manual` |
| S6 | Mismatched `Syncfusion.*` package versions across projects | `version-consistency` gate |
| S7 | Document library (`XlsIO`/`DocIO`/`PdfIO`/`Presentation`) object used without `using`/`Dispose` | **CA2000** — highest-yield rule for the document teams |
| S8 | Large document generated fully in memory instead of streaming to a target | Memory blowup on big documents | `manual` + `leak-dotnet` |

## The leak-verification test pattern

The reviewer must recommend this pattern for every leak finding, because it turns
"I inspected the code" into a deterministic test that rides the existing `trx`
gate with no new tooling:

```csharp
[Fact]
public void Grid_IsCollected_AfterHostDisposed()
{
    var reference = CreateAndDispose();

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    Assert.False(reference.IsAlive,
        "the grid was retained after its host was disposed - a handler or " +
        "static subscription is still holding it");

    // Local so the strong ref cannot survive on the caller's stack frame.
    [MethodImpl(MethodImplOptions.NoInlining)]
    static WeakReference CreateAndDispose()
    {
        var host = new HostForm();
        var grid = new SfDataGrid();
        host.Controls.Add(grid);
        host.Show();
        host.Dispose();
        return new WeakReference(grid);
    }
}
```

Two details that make or break it: the object must be created inside a
**non-inlined local function** so no stack slot keeps it alive, and the
double-`Collect` around `WaitForPendingFinalizers` is required because the first
pass only queues finalizers.

## Reviewer instructions

1. **Never assert a leak is absent.** Report retention *candidates* (a subscription
   with no matching unsubscribe, a disposable with no `using`), then require
   either a `WeakReference` test as above or the `leak-dotnet` gate. State
   explicitly which one verifies the finding.
2. **CA2000 is the load-bearing rule** for WinForms and the document libraries.
   If it is not at `error`, that missing configuration is the top finding — it
   covers W1, W2, W10, and S7 permanently.
3. **Threading findings must name the thread.** "UI updated off-thread" is only
   actionable with the calling context identified.
