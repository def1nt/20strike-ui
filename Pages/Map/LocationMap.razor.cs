using System.Globalization;
using _20strike_ui.Data.ComputersV2;
using _20strike_ui.Infrastructure;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace _20strike_ui.Pages.Map;

/// <summary>
/// Code-behind for the location map page. Loads every computer with its optional location from
/// GET /v2/computers, renders markers over the map image and manages: adding a marker (click to
/// place, then confirm), deleting (confirm + DELETE), moving (drag through JS interop, optimistic
/// save, silent on success; Escape undoes the last movement), editing the description (full
/// MapData is always sent back) and searching by name with panning to the result.
/// The optional route parameter pans/highlights the named computer's marker.
/// </summary>
public partial class LocationMap : IDisposable
{
    private const string MapViewportId = "location-map-viewport";
    private const string MapImageUrl = "/images/terra.jpg";

    /// <summary>Optional computer name from the route (/v2/map/{ComputerName}); pans to its marker on load.</summary>
    [Parameter]
    public string? ComputerName { get; set; }

    // -------------------------------------------------------------------------------------------
    // Data
    // -------------------------------------------------------------------------------------------

    private bool _loading = true;
    private string? _loadError;
    private List<ComputerLocationSummary> _computers = new();

    /// <summary>Computer name → marker position. The single source of truth for rendered markers.</summary>
    private readonly Dictionary<string, MapPoint> _markers = new(StringComparer.OrdinalIgnoreCase);

    // -------------------------------------------------------------------------------------------
    // Interaction state
    // -------------------------------------------------------------------------------------------

    private string _searchQuery = "";

    /// <summary>Computer chosen in the "add marker" picker; non-empty arms click-to-place mode.</summary>
    private string? _pendingAddComputer;

    /// <summary>The computer currently shown in the side panel (existing marker or a new draft).</summary>
    private string? _editorName;

    /// <summary>Position of the drafted/edited marker.</summary>
    private MapPoint _editorPoint = new(0, 0, "", "");

    /// <summary>True while the panel edits a marker that has not been saved yet (new marker flow).</summary>
    private bool _editorIsNew;

    private string _draftDescription = "";
    private string _draftPhone = "";
    private bool _saving;
    private string? _actionError;
    private string? _actionInfo;
    private string? _routeMessage;

    /// <summary>
    /// The last saved drag movement (marker name + position before the drag). Escape reverts it.
    /// Only drags of already-saved markers are tracked; draft repositioning needs no undo.
    /// </summary>
    private (string Name, MapPoint Previous)? _lastMove;

    private string? _lastComputerName;

    private readonly CancellationTokenSource _cts = new();
    private DotNetObjectReference<LocationMap>? _jsRef;

    // -------------------------------------------------------------------------------------------
    // Computed
    // -------------------------------------------------------------------------------------------

    private IReadOnlyList<ComputerLocationSummary> ComputersWithoutMarkers =>
        _computers
            .Where(c => c.Location is null
                        && !string.Equals(c.Name, _editorName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private IReadOnlyList<ComputerLocationSummary> SearchResults =>
        MapComputerFilter.Apply(_computers, _searchQuery);

    // -------------------------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------------------------

    // Initialization is deferred to OnAfterRenderAsync(firstRender): with ServerPrerendered the
    // page renders once statically and again when the interactive circuit starts, so starting the
    // load from OnInitialized would duplicate the /computers request. The viewport element is
    // always rendered, so mapInit can bind listeners right here.
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            // The page content is hidden behind AuthorizeView; skip map init and the backend
            // request for anonymous users (the viewport element is not even rendered for them).
            // Login/logout force a full navigation, so a one-time check here is enough.
            var authState = await AuthStateProvider.GetAuthenticationStateAsync();
            if (authState.User.Identity?.IsAuthenticated != true)
            {
                return;
            }
            _jsRef = DotNetObjectReference.Create(this);
            await JS.InvokeVoidAsync("mapInit", MapViewportId, _jsRef);
            _ = LoadInitialAsync();
        }
    }

    protected override void OnParametersSet()
    {
        if (!string.Equals(ComputerName, _lastComputerName, StringComparison.Ordinal))
        {
            _lastComputerName = ComputerName;
            if (!_loading && _loadError is null)
            {
                _ = PanToRouteComputerAsync();
            }
        }
    }

    // -------------------------------------------------------------------------------------------
    // Loading
    // -------------------------------------------------------------------------------------------

    private async Task LoadInitialAsync()
    {
        _loading = true;
        _loadError = null;

        string? lastError = null;
        try
        {
            lastError = await RetryHelper.RunWithBackoffAsync(async () =>
            {
                _computers = (await Client.GetComputersWithLocationsAsync(_cts.Token).ConfigureAwait(false)).ToList();
            }, maxAttempts: 3, _cts.Token);
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            return; // component disposed while loading
        }
        catch (Exception ex)
        {
            lastError = ex.Message;
        }

        await InvokeAsync(() =>
        {
            if (_cts.IsCancellationRequested)
            {
                return;
            }

            _loadError = lastError;
            RebuildMarkers();
            _loading = false;
            StateHasChanged();
        });

        if (!_cts.IsCancellationRequested)
        {
            await PanToRouteComputerAsync();
        }
    }

    private async Task ReloadAsync()
    {
        await LoadInitialAsync();
    }

    private void RebuildMarkers()
    {
        _markers.Clear();
        foreach (var computer in _computers)
        {
            if (computer.Location is { } loc)
            {
                _markers[computer.Name] = loc;
            }
        }
    }

    // -------------------------------------------------------------------------------------------
    // JS interop callbacks (marker drag / click / map click)
    // -------------------------------------------------------------------------------------------

    [JSInvokable]
    public void OnMarkerClick(string name)
    {
        _ = InvokeAsync(() =>
        {
            if (!_markers.TryGetValue(name, out var point))
            {
                return;
            }

            // Clicking the placeholder draft does nothing — its panel is already open.
            if (_editorIsNew && string.Equals(name, _editorName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            OpenEditor(name, point);
            StateHasChanged();
        });
    }

    [JSInvokable]
    public void OnMarkerDragEnd(string name, double xPct, double yPct)
    {
        _ = InvokeAsync(async () =>
        {
            var x = MapGeometry.ToCoordinate(xPct);
            var y = MapGeometry.ToCoordinate(yPct);

            // Dragging the draft marker only repositions it; nothing is saved until the panel is confirmed.
            if (_editorIsNew && string.Equals(name, _editorName, StringComparison.OrdinalIgnoreCase))
            {
                _editorPoint = new MapPoint(x, y, _draftDescription, _draftPhone);
                StateHasChanged();
                return;
            }

            if (!_markers.TryGetValue(name, out var previous))
            {
                return;
            }

            var isEdited = string.Equals(name, _editorName, StringComparison.OrdinalIgnoreCase);
            var description = isEdited ? _draftDescription : previous.Description;
            var phone = isEdited ? _draftPhone : previous.Phone;
            var moved = new MapPoint(x, y, description, phone);

            // Optimistic update; revert on failure.
            _markers[name] = moved;
            if (isEdited)
            {
                _editorPoint = moved;
            }

            _actionError = null;
            _actionInfo = null;
            StateHasChanged();

            try
            {
                await Client.SaveLocationAsync(name, moved, _cts.Token).ConfigureAwait(false);
                // Drag saves silently; the movement stays undoable via Escape until the next drag.
                _lastMove = (name, previous);
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _markers[name] = previous;
                if (isEdited)
                {
                    _editorPoint = previous;
                }

                _actionError = $"Не удалось сохранить позицию: {ex.Message}";

                // Blazor diffs against its own last render, so it will not restore the style that
                // JS moved during the drag; reset the element position explicitly.
                try
                {
                    await JS.InvokeVoidAsync(
                        "mapSetMarkerPosition",
                        name,
                        MapGeometry.ToPercentage(previous.X),
                        MapGeometry.ToPercentage(previous.Y)).ConfigureAwait(false);
                }
                catch
                {
                    // JS interop unavailable during teardown; the next full reload resyncs.
                }
            }

            StateHasChanged();
        });
    }

    /// <summary>
    /// Escape handler bound from JS: undoes the last drag-saved marker movement by saving the
    /// previous position back. Ignored while a save is in flight or when there is nothing to undo.
    /// </summary>
    [JSInvokable]
    public void OnMapEscape()
    {
        _ = InvokeAsync(async () =>
        {
            if (_lastMove is not { } move || _saving)
            {
                return;
            }

            if (!_markers.TryGetValue(move.Name, out var current))
            {
                _lastMove = null; // the marker was deleted after the move; nothing to restore
                return;
            }

            _lastMove = null;

            var isEdited = string.Equals(move.Name, _editorName, StringComparison.OrdinalIgnoreCase);
            _markers[move.Name] = move.Previous;
            if (isEdited)
            {
                _editorPoint = move.Previous;
            }

            _actionError = null;
            _actionInfo = null;
            StateHasChanged();

            try
            {
                await Client.SaveLocationAsync(move.Name, move.Previous, _cts.Token).ConfigureAwait(false);
                _actionInfo = "Перемещение отменено.";
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Undo failed: restore the post-drag position so the UI matches the server.
                _markers[move.Name] = current;
                if (isEdited)
                {
                    _editorPoint = current;
                }

                _actionError = $"Не удалось отменить перемещение: {ex.Message}";
            }

            // Blazor diffs against its own last render, so it may not restore the style that JS
            // moved during the drag; reset the element position explicitly.
            try
            {
                var point = _markers[move.Name];
                await JS.InvokeVoidAsync(
                    "mapSetMarkerPosition",
                    move.Name,
                    MapGeometry.ToPercentage(point.X),
                    MapGeometry.ToPercentage(point.Y)).ConfigureAwait(false);
            }
            catch
            {
                // JS interop unavailable during teardown; the next full reload resyncs.
            }

            StateHasChanged();
        });
    }

    [JSInvokable]
    public void OnMapClick(double xPct, double yPct)
    {
        _ = InvokeAsync(() =>
        {
            var coordinate = new MapPoint(
                MapGeometry.ToCoordinate(xPct),
                MapGeometry.ToCoordinate(yPct),
                "",
                "");

            if (_editorIsNew && string.Equals(_editorName, _pendingAddComputer, StringComparison.OrdinalIgnoreCase))
            {
                // The draft is already placed; a click elsewhere moves it.
                _editorPoint = new MapPoint(coordinate.X, coordinate.Y, _draftDescription, _draftPhone);
            }
            else if (_pendingAddComputer is { Length: > 0 } pending)
            {
                // First click after choosing a computer in the picker: create the draft.
                _editorName = pending;
                _editorPoint = coordinate;
                _editorIsNew = true;
                _draftDescription = "";
                _draftPhone = "";
            }
            else
            {
                // No placement pending: clicking the empty map dismisses the panel.
                ClosePanel();
                return;
            }

            _actionError = null;
            _actionInfo = null;
            StateHasChanged();
        });
    }

    // -------------------------------------------------------------------------------------------
    // Editor panel actions
    // -------------------------------------------------------------------------------------------

    private void OpenEditor(string name, MapPoint point)
    {
        _pendingAddComputer = null;
        _editorName = name;
        _editorPoint = point;
        _editorIsNew = false;
        _draftDescription = point.Description;
        _draftPhone = point.Phone;
        _routeMessage = null;
    }

    private void ClosePanel()
    {
        _pendingAddComputer = null;
        _editorName = null;
        _editorIsNew = false;
        _draftDescription = "";
        _draftPhone = "";
    }

    private async Task SaveEditorAsync()
    {
        if (_editorName is not { Length: > 0 } name)
        {
            return;
        }

        _saving = true;
        _actionError = null;
        _actionInfo = null;
        var point = new MapPoint(_editorPoint.X, _editorPoint.Y, _draftDescription.Trim(), _draftPhone.Trim());
        try
        {
            await Client.SaveLocationAsync(name, point, _cts.Token).ConfigureAwait(false);
            _markers[name] = point;
            _editorPoint = point;
            _pendingAddComputer = null;
            _actionInfo = _editorIsNew ? "Маркер добавлен." : "Изменения сохранены.";
            _editorIsNew = false;
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _actionError = $"Не удалось сохранить: {ex.Message}";
        }
        finally
        {
            _saving = false;
        }

        await InvokeAsync(StateHasChanged);
    }

    private async Task DeleteMarkerAsync()
    {
        if (_editorName is not { Length: > 0 } name || _editorIsNew)
        {
            return;
        }

        bool confirmed;
        try
        {
            confirmed = await JS.InvokeAsync<bool>("mapConfirm", $"Удалить маркер компьютера «{name}» с карты?");
        }
        catch
        {
            confirmed = true; // JS unavailable; proceed (rare, e.g. teardown)
        }

        if (!confirmed)
        {
            return;
        }

        _saving = true;
        _actionError = null;
        _actionInfo = null;
        try
        {
            await Client.DeleteLocationAsync(name, _cts.Token).ConfigureAwait(false);
            _markers.Remove(name);
            if (_lastMove is { } move && string.Equals(move.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                _lastMove = null;
            }

            ClosePanel();
            _actionInfo = "Маркер удалён.";
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _actionError = $"Не удалось удалить маркер: {ex.Message}";
        }
        finally
        {
            _saving = false;
        }

        await InvokeAsync(StateHasChanged);
    }

    private void DismissActionError() => _actionError = null;

    private void DismissActionInfo() => _actionInfo = null;

    // -------------------------------------------------------------------------------------------
    // Search / picker / route navigation
    // -------------------------------------------------------------------------------------------

    private async Task OnAddComputerChanged()
    {
        // Choosing a computer arms click-to-place mode; the draft appears on the first map click.
        _actionError = null;
        _actionInfo = null;
        await InvokeAsync(StateHasChanged);
    }

    private async Task GoToComputerAsync(ComputerLocationSummary computer)
    {
        _searchQuery = "";
        _actionError = null;
        _actionInfo = null;

        if (computer.Location is { } loc)
        {
            OpenEditor(computer.Name, loc);
            await CenterOnAsync(computer.Name);
            await InvokeAsync(StateHasChanged);
        }
        else
        {
            ClosePanel();
            _pendingAddComputer = computer.Name;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task PanToRouteComputerAsync()
    {
        if (string.IsNullOrWhiteSpace(ComputerName))
        {
            _routeMessage = null;
            return;
        }

        var match = _computers.FirstOrDefault(c =>
            string.Equals(c.Name, ComputerName, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            _routeMessage = $"Компьютер \"{ComputerName}\" не найден в списке.";
            await InvokeAsync(StateHasChanged);
            return;
        }

        if (match.Location is { } loc)
        {
            OpenEditor(match.Name, loc);
            await CenterOnAsync(match.Name);
            await InvokeAsync(StateHasChanged);
        }
        else
        {
            ClosePanel();
            _pendingAddComputer = match.Name;
            _routeMessage = $"У компьютера \"{match.Name}\" нет маркера. Нажмите на карту, чтобы установить его.";
            await InvokeAsync(StateHasChanged);
        }
    }

    // -------------------------------------------------------------------------------------------
    // Viewport JS helpers
    // -------------------------------------------------------------------------------------------

    private async Task CenterOnAsync(string name)
    {
        if (!_markers.TryGetValue(name, out var point))
        {
            return;
        }

        try
        {
            // Wait for the image so the centering math has real layer dimensions.
            await JS.InvokeVoidAsync("mapWaitForImage", MapViewportId);
            await JS.InvokeVoidAsync(
                "mapCenterOn",
                MapGeometry.ToPercentage(point.X),
                MapGeometry.ToPercentage(point.Y),
                true);
        }
        catch
        {
            // JS interop unavailable during prerendering/teardown; the map still renders.
        }
    }

    private async Task ZoomByAsync(int delta)
    {
        try
        {
            await JS.InvokeVoidAsync("mapZoomBy", delta);
        }
        catch
        {
            // JS interop unavailable; ignore.
        }
    }

    private async Task FitMapAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("mapFit");
        }
        catch
        {
            // JS interop unavailable; ignore.
        }
    }

    // -------------------------------------------------------------------------------------------
    // Formatting helpers
    // -------------------------------------------------------------------------------------------

    /// <summary>Renders a map coordinate as a CSS percentage using the invariant culture (CSS needs a dot).</summary>
    private static string Pct(double coordinate) =>
        MapGeometry.ToPercentage(coordinate).ToString("0.0", CultureInfo.InvariantCulture);

    private static string FormatCoord(double coordinate) => coordinate.ToString("0.0");

    // -------------------------------------------------------------------------------------------
    // IDisposable
    // -------------------------------------------------------------------------------------------

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();

        try
        {
            _ = JS.InvokeVoidAsync("mapDispose");
        }
        catch
        {
            // JS interop is unavailable during prerendering teardown; the listeners stay inert.
        }

        _jsRef?.Dispose();
    }
}