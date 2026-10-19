using Khrenkov.top.Data;
using Khrenkov.top.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Khrenkov.top.Controllers;

[Route("share")]
public sealed class ShareController(ApplicationDbContext database, FileStorage storage) : ControllerBase
{
    [HttpGet("{token}")]
    public async Task<IActionResult> Download(string token)
    {
        var cancellationToken = HttpContext.RequestAborted;
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var link = await database.ShareLinks.AsNoTracking().Include(link => link.File)
            .SingleOrDefaultAsync(link => link.Token == token && link.RemainingDownloads > 0, cancellationToken);
        if (link is null || !System.IO.File.Exists(Path.Combine(storage.UploadsPath, link.File.Name)))
            return InvalidLink();
        var changed = await database.ShareLinks.Where(link => link.Token == token && link.RemainingDownloads > 0)
            .ExecuteUpdateAsync(update => update.SetProperty(link => link.RemainingDownloads, link => link.RemainingDownloads - 1), cancellationToken);
        if (changed != 1)
            return InvalidLink();
        await database.ShareLinks.Where(link => link.Token == token && link.RemainingDownloads == 0).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return PhysicalFile(Path.Combine(storage.UploadsPath, link.File.Name), "application/octet-stream", link.File.Name);
    }

    private ContentResult InvalidLink() => Content("Ссылка недействительна или лимит скачиваний исчерпан.", "text/plain; charset=utf-8");
}
