using Khrenkov.top.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Khrenkov.top.Controllers.Api
{
    [ApiController]
    [Authorize]
    [Route("api/files")]
    [Produces("application/json")]
    public class FilesApiController : ControllerBase
    {
        private readonly string _uploadsPath;
        private readonly ShareLinkService _shareService;

        public FilesApiController(IWebHostEnvironment env, ShareLinkService shareService)
        {
            _uploadsPath = Path.Combine(env.ContentRootPath, "Uploads");
            _shareService = shareService;
            Directory.CreateDirectory(_uploadsPath);
        }

        [HttpGet]
        public IActionResult List()
        {
            var files = Directory.GetFiles(_uploadsPath)
                .Where(f => Path.GetFileName(f) != "_shares.json")
                .Select(f =>
                {
                    var info = new FileInfo(f);
                    return new
                    {
                        name = info.Name,
                        size = info.Length,
                        modified = info.LastWriteTimeUtc
                    };
                })
                .OrderBy(f => f.name)
                .ToList();

            return Ok(files);
        }

        [HttpPost("upload")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { error = "Файл не выбран или пустой" });

            var fileName = Path.GetFileName(file.FileName);
            var destPath = Path.Combine(_uploadsPath, fileName);
            await using var stream = System.IO.File.Create(destPath);
            await file.CopyToAsync(stream);

            return Ok(new { name = fileName, size = file.Length, modified = DateTime.UtcNow });
        }

        [HttpGet("download/{fileName}")]
        public IActionResult Download(string fileName)
        {
            var filePath = Path.Combine(_uploadsPath, Path.GetFileName(fileName));
            if (!System.IO.File.Exists(filePath))
                return NotFound(new { error = "Файл не найден" });

            return PhysicalFile(filePath, "application/octet-stream", Path.GetFileName(fileName));
        }

        [HttpDelete("{fileName}")]
        public IActionResult Delete(string fileName)
        {
            var filePath = Path.Combine(_uploadsPath, Path.GetFileName(fileName));
            if (!System.IO.File.Exists(filePath))
                return NotFound(new { error = "Файл не найден" });

            System.IO.File.Delete(filePath);
            return NoContent();
        }

        [HttpPost("share")]
        public IActionResult CreateShare([FromBody] CreateShareRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.FileName) || request.MaxDownloads < 1)
                return BadRequest(new { error = "Некорректные параметры" });

            var fileName = Path.GetFileName(request.FileName);
            var filePath = Path.Combine(_uploadsPath, fileName);
            if (!System.IO.File.Exists(filePath))
                return NotFound(new { error = "Файл не найден" });

            var link = _shareService.CreateLink(fileName, request.MaxDownloads);
            return Ok(new { token = link.Token });
        }
    }

    public record CreateShareRequest(string FileName, int MaxDownloads);
}
