using Khrenkov.top.Services;
using Microsoft.AspNetCore.Mvc;

namespace Khrenkov.top.Controllers
{
    [Route("share")]
    public class ShareController : Controller
    {
        private readonly ShareLinkService _shareService;
        private readonly IWebHostEnvironment _env;

        public ShareController(ShareLinkService shareService, IWebHostEnvironment env)
        {
            _shareService = shareService;
            _env = env;
        }

        [HttpGet("{token}")]
        public IActionResult Download(string token)
        {
            var (success, fileName) = _shareService.TryConsume(token);
            if (!success || fileName == null)
                return Content("Ссылка недействительна или лимит скачиваний исчерпан.", "text/plain; charset=utf-8");

            var filePath = Path.Combine(_env.ContentRootPath, "Uploads", Path.GetFileName(fileName));
            if (!System.IO.File.Exists(filePath))
                return NotFound("Файл не найден.");

            return PhysicalFile(filePath, "application/octet-stream", Path.GetFileName(fileName));
        }
    }
}
