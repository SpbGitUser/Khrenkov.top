using System.Text.Json;
using Khrenkov.top.Models;

namespace Khrenkov.top.Services
{
    public class ShareLinkService
    {
        private readonly string _storePath;
        private List<ShareLink> _links = new();
        private readonly object _lock = new();

        public ShareLinkService(IWebHostEnvironment env)
        {
            _storePath = Path.Combine(env.ContentRootPath, "Uploads", "_shares.json");
            Load();
        }

        private void Load()
        {
            if (File.Exists(_storePath))
            {
                try
                {
                    var json = File.ReadAllText(_storePath);
                    _links = JsonSerializer.Deserialize<List<ShareLink>>(json) ?? new();
                }
                catch
                {
                    _links = new();
                }
            }
        }

        private void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);
            File.WriteAllText(_storePath, JsonSerializer.Serialize(_links));
        }

        public ShareLink CreateLink(string fileName, int maxDownloads)
        {
            lock (_lock)
            {
                var link = new ShareLink { FileName = fileName, RemainingDownloads = maxDownloads };
                _links.Add(link);
                Save();
                return link;
            }
        }

        public (bool Success, string? FileName) TryConsume(string token)
        {
            lock (_lock)
            {
                var link = _links.FirstOrDefault(l => l.Token == token);
                if (link == null || link.RemainingDownloads <= 0)
                    return (false, null);
                link.RemainingDownloads--;
                if (link.RemainingDownloads == 0)
                    _links.Remove(link);
                Save();
                return (true, link.FileName);
            }
        }

        public List<ShareLink> GetAll()
        {
            lock (_lock) return _links.ToList();
        }
    }
}
