namespace _20strike_ui.Data;
using System.Net.Http;

public sealed class DataFetcher
{
    private readonly HttpClient Http;
    private readonly string _backend;
    public DataFetcher(HttpClient http, string backend)
    {
        Http = http;
        _backend = backend;
    }

    public async Task<Dictionary<string, string>[]> GetData(string computer, string class_)
    {
        Dictionary<string, string> req = new Dictionary<string, string> {
            {"action", "read"}, {"target", "info"}, { "pc", computer }, { "class", class_ }
        };
        var response = await Http.PostAsJsonAsync<Dictionary<string, string>>(_backend, req);
        Dictionary<string, string>[]? data;
        data = await response.Content.ReadFromJsonAsync<Dictionary<string, string>[]>();
        return data ?? Array.Empty<Dictionary<string, string>>();
    }

    public async Task<Dictionary<string, string>[]> GetClassData(string class_)
    {
        var req = new Dictionary<string, string> {
            {"action", "read"}, {"target","info"}, { "class", class_ } };
        var response = await Http.PostAsJsonAsync<Dictionary<string, string>>(_backend, req);
        var data = await response.Content.ReadFromJsonAsync<Dictionary<string, string>[]>();
        return data ?? Array.Empty<Dictionary<string, string>>();
    }

    public async Task<Dictionary<string, string>> GetUsers()
    {
        Dictionary<string, string> req = new Dictionary<string, string> {
            {"action", "read"}, {"target","users"} };
        var response = await Http.PostAsJsonAsync<Dictionary<string, string>>(_backend, req);
        var users = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        return users ?? new Dictionary<string, string> { };
    }

    public async Task<List<string>> GetComputers()
    {
        Dictionary<string, string> req = new Dictionary<string, string> {
            { "action", "read" }, { "target", "computers" } };
        var response = await Http.PostAsJsonAsync<Dictionary<string, string>>(_backend, req);
        var data = await response.Content.ReadFromJsonAsync<List<string>>();
        return data ?? new List<string> { };
    }

    public async Task<List<string>> GetClasses()
    {
        Dictionary<string, string> req = new Dictionary<string, string> {
            {"action", "read"}, {"target","classes"} };
        var response = await Http.PostAsJsonAsync<Dictionary<string, string>>(_backend, req);
        var data = await response.Content.ReadFromJsonAsync<List<string>>();
        return data ?? new List<string> { };
    }

    public async Task<string> RequestUpdate(string computer)
    {
        Dictionary<string, string> req = new Dictionary<string, string> {
                {"action", "update"}, {"target", computer} };
        var response = await Http.PostAsJsonAsync<Dictionary<string, string>>(_backend, req);
        string text = await response.Content.ReadAsStringAsync();
        return text;
    }

    public async Task<string> RequestInvoke(string target, string class_, string method, string object_)
    {
        Dictionary<string, string> req = new()
        {
            {"action", "invoke"},
            {"target", target},
            {"class", class_},
            {"method", method},
            {"object", object_}
        };
        var response = await Http.PostAsJsonAsync<Dictionary<string, string>>(_backend, req);
        string data = await response.Content.ReadAsStringAsync();
        return data;
    }
}
