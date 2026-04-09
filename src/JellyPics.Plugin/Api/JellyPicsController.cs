using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using JellyPics.Plugin.Helpers;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace JellyPics.Plugin.Api;

[ApiController]
[Route("Plugins/JellyPics")]
[Authorize(Policy = "RequiresElevation")]
public class JellyPicsController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<JellyPicsController> _logger;

    public JellyPicsController(ILibraryManager libraryManager, ILogger<JellyPicsController> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    [HttpGet("Config")]
    public ActionResult<object> GetConfig() =>
        Ok(new { syncTargetPath = Plugin.Instance.Configuration.SyncTargetPath });

    [HttpPut("Config")]
    public ActionResult UpdateConfig([FromBody] UpdateConfigRequest? request)
    {
        if (request is null)
            return BadRequest(new { error = "Corps de requete null." });

        var path = (request.SyncTargetPath ?? string.Empty).Trim();
        _logger.LogInformation("JellyPics: UpdateConfig path=\"{Path}\"", path);

        if (!string.IsNullOrEmpty(path) && !Directory.Exists(path))
        {
            try { Directory.CreateDirectory(path); }
            catch (Exception ex)
            {
                return BadRequest(new { error = "Impossible de creer le dossier : " + ex.Message });
            }
        }

        Plugin.Instance.Configuration.SyncTargetPath = path;
        Plugin.Instance.SaveConfiguration();
        return NoContent();
    }

    [HttpGet("Libraries")]
    public ActionResult<object> GetLibraries()
    {
        var libraries = _libraryManager
            .GetVirtualFolders()
            .Select(f => new
            {
                id = f.ItemId,
                name = f.Name,
                collectionType = f.CollectionType == null ? string.Empty : f.CollectionType.ToString(),
                paths = f.Locations,
            })
            .ToList();
        return Ok(libraries);
    }

    [HttpGet("Folders")]
    public ActionResult<object> GetFolders([FromQuery] string path)
    {
        if (string.IsNullOrEmpty(path)) return BadRequest(new { error = "Chemin manquant." });
        if (path.Contains("..", StringComparison.Ordinal))
            return BadRequest(new { error = "Chemin non autorise." });
        if (!Directory.Exists(path))
            return BadRequest(new { error = "Chemin introuvable." });

        var dirs = Directory.GetDirectories(path)
            .Select(d => new
            {
                name = Path.GetFileName(d),
                fullPath = d,
                hasChildren = Directory.GetDirectories(d).Length > 0,
            })
            .OrderBy(d => d.name)
            .ToList();
        return Ok(dirs);
    }

    [HttpPost("Upload")]
    [RequestSizeLimit(500_000_000)]
    public async Task<ActionResult<object>> Upload(
        [FromForm] IFormFile? file,
        [FromForm] string? targetPath,
        [FromHeader(Name = "X-Original-Date")] string? originalDateHeader)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Aucun fichier recu." });

        if (!string.IsNullOrEmpty(targetPath) &&
            targetPath.Contains("..", StringComparison.Ordinal))
            return BadRequest(new { error = "Chemin non autorise." });

        var destination = targetPath ?? Plugin.Instance.Configuration.SyncTargetPath;
        if (string.IsNullOrEmpty(destination))
            return BadRequest(new { error = "Dossier de destination non configure." });

        if (!Directory.Exists(destination))
        {
            try { Directory.CreateDirectory(destination); }
            catch (Exception ex) { return BadRequest(new { error = "Impossible de creer: " + ex.Message }); }
        }

        DateTime? originalDate = MetadataHelper.ParseClientDate(originalDateHeader);

        var safeName = Path.GetFileName(file.FileName)
            .Replace("..", "_", StringComparison.Ordinal).Trim();
        if (string.IsNullOrEmpty(safeName)) safeName = Guid.NewGuid().ToString() + ".bin";

        var destPath = Path.Combine(destination, safeName);
        if (System.IO.File.Exists(destPath))
        {
            var ext = Path.GetExtension(safeName);
            var stem = Path.GetFileNameWithoutExtension(safeName);
            destPath = Path.Combine(destination,
                stem + "_" + DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString() + ext);
        }

        try
        {
            await using var stream = new FileStream(destPath, FileMode.Create,
                FileAccess.Write, FileShare.None, 65536, useAsync: true);
            await file.CopyToAsync(stream);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Erreur ecriture: " + ex.Message });
        }

        originalDate ??= MetadataHelper.ExtractOriginalDate(destPath);

        if (originalDate.HasValue)
        {
            var extUpper = Path.GetExtension(safeName).ToUpperInvariant();
            var isVideo = extUpper == ".MP4" || extUpper == ".M4V" || extUpper == ".MOV"
                       || extUpper == ".MKV" || extUpper == ".WEBM" || extUpper == ".AVI"
                       || extUpper == ".3GP" || extUpper == ".TS" || extUpper == ".MTS";
            if (isVideo)
            {
                var ok = VideoMetadataHelper.ApplyDateToVideoContainer(
                    destPath, originalDate.Value, _logger);
                if (!ok) MetadataHelper.ApplyDateToFile(destPath, originalDate.Value);
            }
            else
            {
                MetadataHelper.ApplyDateToFile(destPath, originalDate.Value);
            }
        }

        return Ok(new
        {
            fileName = safeName,
            path = destPath,
            originalDate = originalDate.HasValue
                ? originalDate.Value.ToString("o", System.Globalization.CultureInfo.InvariantCulture)
                : null,
            message = "Import reussi.",
        });
    }
}

public record UpdateConfigRequest(string? SyncTargetPath);
