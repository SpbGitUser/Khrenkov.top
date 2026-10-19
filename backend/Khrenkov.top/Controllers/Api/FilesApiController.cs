using Khrenkov.top.Data;
using Khrenkov.top.Data.Entities;
using Khrenkov.top.Models;
using Khrenkov.top.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Khrenkov.top.Controllers.Api;

[ApiController]
[Authorize]
[Route("api/files")]
[Produces("application/json")]
public sealed class FilesApiController(ApplicationDbContext database, FileStorage storage) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List() => Ok(await database.Files.AsNoTracking().OrderBy(file => file.Name)
        .Select(file => new StoredFile(file.Name, file.Size, file.ModifiedUtc)).ToListAsync(HttpContext.RequestAborted));

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "Файл не выбран или пустой" });
        var fileName = Path.GetFileName(file.FileName);
        if (!FileStorage.IsUserFileName(fileName))
            return BadRequest(new { error = "Недопустимое имя файла" });
        var stagingPath = Path.Combine(storage.UploadsPath, $".upload-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingPath);
        var temporaryPath = Path.Combine(stagingPath, Guid.NewGuid().ToString("N"));
        var cancellationToken = HttpContext.RequestAborted;
        try
        {
            await using (var stream = System.IO.File.Create(temporaryPath))
                await file.CopyToAsync(stream, cancellationToken);
            await storage.MutationLock.WaitAsync(cancellationToken);
            try
            {
                fileName = storage.ResolveExistingFileName(fileName) ?? fileName;
                var path = Path.Combine(storage.UploadsPath, fileName);
                System.IO.File.Move(temporaryPath, path, overwrite: true);
                var info = new FileInfo(path);
                // After the disk change, finalize metadata even if the HTTP client disconnects.
                var entity = await database.Files.SingleOrDefaultAsync(existing => existing.Name == fileName, CancellationToken.None);
                if (entity is null)
                {
                    entity = new FileEntity { Name = fileName };
                    database.Files.Add(entity);
                }
                entity.Size = info.Length;
                entity.ModifiedUtc = info.LastWriteTimeUtc;
                await database.SaveChangesAsync(CancellationToken.None);
                return Ok(new StoredFile(entity.Name, entity.Size, entity.ModifiedUtc));
            }
            finally
            {
                storage.MutationLock.Release();
            }
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
        var actualFileName = storage.ResolveExistingFileName(fileName);
        if (actualFileName is null)
            return NotFound(new { error = "Файл не найден" });
        return PhysicalFile(Path.Combine(storage.UploadsPath, actualFileName), "application/octet-stream", Path.GetFileName(fileName));
    }

    [HttpDelete("{fileName}")]
    public async Task<IActionResult> Delete(string fileName)
    {
        var cancellationToken = HttpContext.RequestAborted;
        await storage.MutationLock.WaitAsync(cancellationToken);
        try
        {
            var actualFileName = storage.ResolveExistingFileName(fileName);
            if (actualFileName is null)
                return NotFound(new { error = "Файл не найден" });
            System.IO.File.Delete(Path.Combine(storage.UploadsPath, actualFileName));
            await database.Files.Where(file => file.Name == actualFileName).ExecuteDeleteAsync(CancellationToken.None);
            return NoContent();
        }
        finally
        {
            storage.MutationLock.Release();
        }
    }

    [HttpPost("share")]
    public async Task<IActionResult> CreateShare([FromBody] CreateShareRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FileName) || request.MaxDownloads < 1)
            return BadRequest(new { error = "Некорректные параметры" });
        var cancellationToken = HttpContext.RequestAborted;
        await storage.MutationLock.WaitAsync(cancellationToken);
        try
        {
            var fileName = storage.ResolveExistingFileName(Path.GetFileName(request.FileName));
            if (fileName is null)
                return NotFound(new { error = "Файл не найден" });
            var file = await database.Files.SingleOrDefaultAsync(existing => existing.Name == fileName, cancellationToken);
            if (file is null)
                return NotFound(new { error = "Файл не найден" });
            var link = new ShareLinkEntity { File = file, RemainingDownloads = request.MaxDownloads };
            database.ShareLinks.Add(link);
            await database.SaveChangesAsync(cancellationToken);
            return Ok(new { token = link.Token });
        }
        finally
        {
            storage.MutationLock.Release();
        }
    }
}

public sealed record CreateShareRequest(string FileName, int MaxDownloads);
