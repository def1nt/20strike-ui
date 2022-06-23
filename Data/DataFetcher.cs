namespace _20strike_ui.Data;

public class DataFetcher {
    public Task<string> GetSomeData(string computername, string classname) {
        Task<string> data = Task.FromResult<string>(computername+":"+classname);

        return data;
    }
}