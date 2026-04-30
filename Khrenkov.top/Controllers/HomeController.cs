using System.Diagnostics;
using Khrenkov.top.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Khrenkov.top.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IConfiguration _configuration;
        private readonly string _uploadsPath;

        public HomeController(ILogger<HomeController> logger, IConfiguration configuration, IWebHostEnvironment env)
        {
            _logger = logger;
            _configuration = configuration;
            _uploadsPath = Path.Combine(env.ContentRootPath, "Uploads");
            Directory.CreateDirectory(_uploadsPath);
        }

        public IActionResult Index()
        {
            var files = Directory.GetFiles(_uploadsPath)
                .Select(f => Path.GetFileName(f))
                .OrderBy(f => f)
                .ToList();
            return View(files);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            if (file != null && file.Length > 0)
            {
                var fileName = Path.GetFileName(file.FileName);
                var destPath = Path.Combine(_uploadsPath, fileName);
                using var stream = System.IO.File.Create(destPath);
                await file.CopyToAsync(stream);
            }
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Delete(string fileName)
        {
            if (!string.IsNullOrEmpty(fileName))
            {
                var filePath = Path.Combine(_uploadsPath, Path.GetFileName(fileName));
                if (System.IO.File.Exists(filePath))
                    System.IO.File.Delete(filePath);
            }
            return RedirectToAction("Index");
        }

        public IActionResult Download(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return NotFound();

            var filePath = Path.Combine(_uploadsPath, Path.GetFileName(fileName));
            if (!System.IO.File.Exists(filePath))
                return NotFound();

            var contentType = "application/octet-stream";
            return PhysicalFile(filePath, contentType, Path.GetFileName(filePath));
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
