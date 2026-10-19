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
        private readonly FileCatalogService _catalog;

        public FilesApiController(IWebHostEnvironment env, ShareLinkService shareService, FileCatalogService catalog)
        {
            _uploadsPath = Path.Combine(env.ContentRootPath, "Uploads");
            _shareService = shareService;
            _catalog = catalog;
            Directory.CreateDirectory(_uploadsPath);
        }

        [HttpGet]
        public IActionResult List()
        {
            return Ok(_catalog.List());
        }

        [HttpPost("upload")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { error = "Файл не выбран или пустой" });

            var fileName = Path.GetFileName(file.FileName);
            if (!FileCatalogService.IsUserFileName(fileName))
                return BadRequest(new { error = "Недопустимое имя файла" });
            // A unique directory avoids reserving a name previously available to user files.
            var stagingPath = Path.Combine(_uploadsPath, $".upload-{Guid.NewGuid():N}");
            Directory.CreateDirectory(stagingPath);
            var temporaryPath = Path.Combine(stagingPath, Guid.NewGuid().ToString("N"));
            try
            {
                await using (var stream = System.IO.File.Create(temporaryPath))
                    await file.CopyToAsync(stream, HttpContext.RequestAborted);
                return Ok(await _catalog.CompleteUploadAsync(temporaryPath, fileName, HttpContext.RequestAborted));
            }
            finally
            {
                if (System.IO.File.Exists(temporaryPath))
                    System.IO.File.Delete(temporaryPath);
                Directory.Delete(stagingPath);
            }
        }

        [HttpGet("download/{fileName}")]
        public IActionResult Download(string fileName)
        {
            var actualFileName = _catalog.ResolveExistingFileName(fileName);
            if (actualFileName is null)
                return NotFound(new { error = "Файл не найден" });
            var filePath = Path.Combine(_uploadsPath, actualFileName);
            if (!System.IO.File.Exists(filePath))
                return NotFound(new { error = "Файл не найден" });

            return PhysicalFile(filePath, "application/octet-stream", Path.GetFileName(fileName));
        }

        [HttpDelete("{fileName}")]
        public async Task<IActionResult> Delete(string fileName)
        {
            if (!FileCatalogService.IsUserFileName(fileName))
                return NotFound(new { error = "Файл не найден" });
            if (!await _catalog.DeleteFileAsync(fileName, HttpContext.RequestAborted))
                return NotFound(new { error = "Файл не найден" });

            return NoContent();
        }

        [HttpPost("share")]
        public IActionResult CreateShare([FromBody] CreateShareRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.FileName) || request.MaxDownloads < 1)
                return BadRequest(new { error = "Некорректные параметры" });

            var fileName = _catalog.ResolveExistingFileName(Path.GetFileName(request.FileName));
            if (fileName is null)
                return NotFound(new { error = "Файл не найден" });
            var filePath = Path.Combine(_uploadsPath, fileName);
            if (!System.IO.File.Exists(filePath))
                return NotFound(new { error = "Файл не найден" });

            try
            {
                var link = _shareService.CreateLink(fileName, request.MaxDownloads);
                return Ok(new { token = link.Token });
            }
            catch (FileNotFoundException)
            {
                return NotFound(new { error = "Файл не найден" });
            }
        }
    }

    public record CreateShareRequest(string FileName, int MaxDownloads);
}
