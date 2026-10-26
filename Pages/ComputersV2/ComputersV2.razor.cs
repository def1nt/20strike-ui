using _20strike_ui.Data.ComputersV2;
using _20strike_ui.Infrastructure;
using Microsoft.JSInterop;

namespace _20strike_ui.Pages.ComputersV2;

/// <summary>
/// Code-behind for the ComputersV2 page: list loading, search, filtering, ping subscription and
/// scroll-to-top JS interop. The markup lives in ComputersV2.razor.
/// </summary>
public partial class ComputersV2
{
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<string, PingInfo> _pings = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _expanded = new(StringComparer.OrdinalIgnoreCase);

    private List<ComputerSummary> _computers = new();
    private string _nameFilter = "";
    // When set, computers whose last successful probe reported status: false are hidden.
    // Off by default. Unknown-status computers (no ping data) are never hidden.
    private bool _hideOffline;
    private bool _initialLoading = true;
    private string? _initialError;
    private bool _pingFailed;

    // Search panel state. A non-null _searchResults means search mode is active: the API response
    // becomes the base for both rendering and the quick filter, replacing the full computer list.
    private bool _searchPanelOpen;
    private ComputerSearchType _searchType = ComputerSearchType.Username;
    private string _searchQuery = "";
    private bool _searchLoading;
    private string? _searchError;
    private List<ComputerSummary>? _searchResults;
    private CancellationTokenSource? _searchCts;

    // Scroll-to-top floating button: tracks the window scroll position via JS interop and
    // becomes visible once the user scrolls down past the threshold.
    private const int ScrollTopThreshold = 200;
    private bool _showScrollTop;
    private DotNetObjectReference<ComputersV2>? _scrollRef;

    private bool _searchActive => _searchResults is not null;

    private string SearchPlaceholder => _searchType switch
    {
        ComputerSearchType.Username => "например, Иванов или ivanov",
        ComputerSearchType.Hardware => "например, Intel Core i5",
        ComputerSearchType.Software => "например, Adobe Photoshop",
        ComputerSearchType.Mac => "например, 00:1A:2B:3C:4D:5E",
        _ => "",
    };

    // The list the quick filter operates on: search results while a search is active,
    // otherwise the full computer list. The quick filter itself is unchanged.
    private IReadOnlyList<ComputerSummary> BaseComputers => _searchResults ?? _computers;

    private IReadOnlyList<ComputerSummary> FilteredComputers =>
        ComputerListFilter.Apply(BaseComputers, GetPing, _nameFilter, _hideOffline);

    // Initialization is deferred to OnAfterRenderAsync(firstRender): with ServerPrerendered the
    // page renders once statically and again when the interactive circuit starts. Starting the load
    // from OnInitialized would fire a second /ping (and /computers) request at the exact moment the
    // first one lands, tripping the backend rate limiter. OnAfterRenderAsync runs only in the
    // interactive circuit, exactly once per navigation.
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _scrollRef = DotNetObjectReference.Create(this);
            await JS.InvokeVoidAsync("cv2InitScrollTracking", _scrollRef, ScrollTopThreshold);
            _ = LoadInitialAsync();
            PingMonitor.PingsUpdated += OnPingsUpdated;
            PingMonitor.PingFailed += OnPingFailed;
            PingMonitor.Start();
        }
    }

    [JSInvokable]
    public void OnWindowScroll(bool scrolledDown)
    {
        if (_showScrollTop != scrolledDown)
        {
            _showScrollTop = scrolledDown;
            StateHasChanged();
        }
    }

    private void ClearNameFilter() => _nameFilter = "";

    private async Task ScrollToTopAsync()
    {
        await JS.InvokeVoidAsync("cv2ScrollToTop");
    }

    private void ToggleSearchPanel() => _searchPanelOpen = !_searchPanelOpen;

    private void DismissSearchError() => _searchError = null;

    private async Task RunSearchAsync()
    {
        var query = _searchQuery.Trim();
        if (query.Length == 0)
        {
            return;
        }

        // Supersede any in-flight search: a stale response must never overwrite a newer one.
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var cts = _searchCts;

        _searchLoading = true;
        _searchError = null;

        IReadOnlyList<ComputerSummary> results;
        try
        {
            results = await Client.SearchComputersAsync(_searchType, query, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return; // superseded by a newer search or component disposal
        }
        catch (Exception ex)
        {
            // Keep the currently displayed list intact; surface a dismissible error above it.
            await InvokeAsync(() =>
            {
                _searchLoading = false;
                _searchError = ex.Message;
                StateHasChanged();
            });
            return;
        }

        await InvokeAsync(() =>
        {
            if (_searchCts != cts || _cts.IsCancellationRequested)
            {
                return; // a newer search started or the component was disposed
            }

            _searchLoading = false;
            _searchResults = results.ToList();
            StateHasChanged();
        });
    }

    private void ResetSearch()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;

        _searchQuery = "";
        _searchLoading = false;
        _searchError = null;
        _searchResults = null;
        _searchPanelOpen = false;
    }

    private async Task LoadInitialAsync()
    {
        _initialLoading = true;
        _initialError = null;

        // The computer list is the only hard dependency. Transient failures (throttling,
        // restarts) are retried with a short linear back-off before giving up.
        var computers = new List<ComputerSummary>();
        string? lastError = null;
        try
        {
            lastError = await RetryHelper.RunWithBackoffAsync(
                async () => computers = (await Client.GetComputersAsync(_cts.Token).ConfigureAwait(false)).ToList(),
                maxAttempts: 3,
                _cts.Token);
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            return; // component disposed while loading
        }

        // Ping statuses are best-effort: a throttled /ping must never hide the computer list.
        // The poll loop retries every few seconds until the endpoint recovers.
        IReadOnlyList<PingInfo>? pings = null;
        var pingFailed = false;
        if (!_cts.IsCancellationRequested)
        {
            try
            {
                pings = await Client.GetPingAsync(_cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                return; // component disposed while loading
            }
            catch
            {
                pingFailed = true;
            }
        }

        // Continuations above ran on thread-pool threads because of ConfigureAwait(false); switch
        // back to the Dispatcher before touching component state or rendering.
        await InvokeAsync(() =>
        {
            if (_cts.IsCancellationRequested)
            {
                return;
            }

            _computers = computers;
            _initialError = lastError;
            if (pings is not null)
            {
                ApplyPing(pings);
                _pingFailed = Client.IsPingThrottled;
            }
            else if (pingFailed)
            {
                _pingFailed = true;
            }

            _initialLoading = false;
            StateHasChanged();
        });
    }

    private async Task ReloadAsync()
    {
        await LoadInitialAsync();
    }

    private void ApplyPing(IEnumerable<PingInfo> pings)
    {
        _pings.Clear();
        foreach (var ping in pings)
        {
            _pings[ping.Name] = ping;
        }
    }

    private PingInfo? GetPing(string name) => _pings.TryGetValue(name, out var ping) ? ping : null;

    private void ToggleExpanded(string name)
    {
        if (!_expanded.Add(name))
        {
            _expanded.Remove(name);
        }
    }

    // Ping updates arrive from ComputerPingMonitor on thread-pool threads; marshal back to the
    // Dispatcher before touching component state.
    private void OnPingsUpdated(IReadOnlyList<PingInfo> pings)
    {
        _ = InvokeAsync(() =>
        {
            ApplyPing(pings);
            _pingFailed = PingMonitor.IsThrottled;
            StateHasChanged();
        });
    }

    private void OnPingFailed()
    {
        _ = InvokeAsync(() =>
        {
            _pingFailed = true;
            StateHasChanged();
        });
    }

    public void Dispose()
    {
        PingMonitor.PingsUpdated -= OnPingsUpdated;
        PingMonitor.PingFailed -= OnPingFailed;
        PingMonitor.Dispose();

        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _cts.Cancel();
        _cts.Dispose();

        try
        {
            _ = JS.InvokeVoidAsync("cv2DisposeScrollTracking");
        }
        catch
        {
            // JS interop is unavailable during prerendering teardown; the listener simply stays inert.
        }

        _scrollRef?.Dispose();
    }
}