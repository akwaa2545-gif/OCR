using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace OperatorCertificationRecord.Web.Services;

public class AdminService
{
    private readonly HashSet<string> _admins;
    private readonly string _storePath;
    private readonly object _lock = new object();
    private DateTime _lastLoadedUtc = DateTime.UnixEpoch;

    public AdminService(IConfiguration configuration)
    {
        // prefer persisted store in App_Data/admins.json when present
        var appData = Path.Combine(Directory.GetCurrentDirectory(), "App_Data");
        Directory.CreateDirectory(appData);
        _storePath = Path.Combine(appData, "admins.json");

        if (File.Exists(_storePath))
        {
            try
            {
                ReloadFromFile();
                return;
            }
            catch { /* fall back to configuration */ }
        }

        var cfgList = configuration.GetSection("Admins")?.Get<string[]>() ?? Array.Empty<string>();
        _admins = new HashSet<string>(cfgList.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase);
    }

    public IEnumerable<string> GetAllAdmins()
    {
        lock (_lock) { return _admins.ToList(); }
    }

    public bool IsAdmin(string? empCode)
    {
        if (string.IsNullOrWhiteSpace(empCode)) return false;
        // If the persisted store was updated externally, reload before checking.
        try
        {
            if (File.Exists(_storePath))
            {
                var last = File.GetLastWriteTimeUtc(_storePath);
                if (last > _lastLoadedUtc)
                {
                    ReloadFromFile();
                }
            }
        }
        catch { /* ignore reload errors */ }

        lock (_lock) { return _admins.Contains(empCode.Trim()); }
    }

    public bool AddAdmin(string empCode)
    {
        if (string.IsNullOrWhiteSpace(empCode)) return false;
        empCode = empCode.Trim();
        lock (_lock)
        {
            if (_admins.Contains(empCode)) return false;
            _admins.Add(empCode);
            Save();
            return true;
        }
    }

    public bool RemoveAdmin(string empCode)
    {
        if (string.IsNullOrWhiteSpace(empCode)) return false;
        empCode = empCode.Trim();
        lock (_lock)
        {
            if (!_admins.Remove(empCode)) return false;
            Save();
            return true;
        }
    }

    private void Save()
    {
        try
        {
            var list = _admins.ToList();
            var raw = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_storePath, raw);
            _lastLoadedUtc = File.GetLastWriteTimeUtc(_storePath);
        }
        catch { /* ignore write errors for now */ }
    }


    private void ReloadFromFile()
    {
        lock (_lock)
        {
            var raw = File.ReadAllText(_storePath);
            var list = JsonSerializer.Deserialize<List<string>>(raw) ?? new List<string>();
            _admins.Clear();
            foreach (var s in list.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim())) _admins.Add(s);
            _lastLoadedUtc = File.GetLastWriteTimeUtc(_storePath);
        }
    }
    
}

