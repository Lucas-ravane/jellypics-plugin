using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using JellyPics.Plugin.Helpers;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace JellyPics.Plugin.Api;

[ApiController]
[Route("Plugins/JellyPics")]
[Authorize(Policy = "DefaultAuthorization")]
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
        if (request is null) return BadRequest(new { error = "Corps de requête manquant." });
        var path = request.SyncTargetPath ?? string.Empty;
        if (!string.IsNullOrEmpty(path) && !Directory.Exists(path))
            return BadRequest(new { error = $"Dossier introuvable: {path}" });
        Plugin.Instance.Configuration.SyncTargetPath = path;
        Plugin.Instance.SaveConfiguration();
        return NoContent();
    }

    [HttpGet("Libraries")]
    public ActionResult<object> GetLibraries()
    {
        var libraries = _libraryManager
            .GetVirtualFolders()
            .Where(f => f.CollectionType == CollectionTypeOptions.HomeVideos
                     || f.CollectionType == CollectionTypeOptions.Photos
                     || f.CollectionType is null)
            .Select(f => new { id = f.ItemId, name = f.Name, paths = f.Locations })
            .ToList();
        return Ok(libraries);
    }

    [HttpGet("Folders")]
    public ActionResult<object> GetFolders([FromQuery] string path)
    {
        if (string.IsNullOrEmpty(path)) return BadRequest(new { error = "Chemin manquant." });
        // Sécurité : on refuse les chemins avec traversal
        if (path.Contains("..", StringComparison.Ordinal))
            return BadRequest(new { error = "Chemin non autorisé." });
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
            return BadRequest(new { error = "Aucun fichier reçu." });

        // Sécurité : refuse les chemins avec traversal
        if (!string.IsNullOrEmpty(targetPath) &&
            targetPath.Contains("..", StringComparison.Ordinal))
            return BadRequest(new { error = "Chemin non autorisé." });

        var destination = targetPath ?? Plugin.Instance.Configuration.SyncTargetPath;
        if (string.IsNullOrEmpty(destination))
            return BadRequest(new { error = "Dossier de destination non configuré." });

        if (!Directory.Exists(destination))
        {
            try { Directory.CreateDirectory(destination); }
            catch (Exception ex) { return BadRequest(new { error = $"Impossible de créer le dossier: {ex.Message}" }); }
        }

        DateTime? originalDate = MetadataHelper.ParseClientDate(originalDateHeader);

        var safeName = Path.GetFileName(file.FileName)
            .Replace("..", "_", StringComparison.Ordinal).Trim();
        if (string.IsNullOrEmpty(safeName)) safeName = $"{Guid.NewGuid()}.bin";

        var destPath = Path.Combine(destination, safeName);
        if (System.IO.File.Exists(destPath))
        {
            var ext  = Path.GetExtension(safeName);
            var stem = Path.GetFileNameWithoutExtension(safeName);
            destPath = Path.Combine(destination,
                $"{stem}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}{ext}");
        }

        try
        {
            await using var stream = new FileStream(destPath, FileMode.Create,
                FileAccess.Write, FileShare.None, 65536, useAsync: true).ConfigureAwait(false);
            await file.CopyToAsync(stream).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = $"Erreur écriture: {ex.Message}" });
        }

        originalDate ??= MetadataHelper.ExtractOriginalDate(destPath);

        if (originalDate.HasValue)
        {
            var ext = Path.GetExtension(safeName).ToUpperInvariant();
            var isVideo = ext is ".MP4" or ".M4V" or ".MOV" or ".MKV"
                              or ".WEBM" or ".AVI" or ".3GP" or ".TS" or ".MTS";
            if (isVideo)
            {
                var ok = VideoMetadataHelper.ApplyDateToVideoContainer(destPath, originalDate.Value, _logger);
                if (!ok) MetadataHelper.ApplyDateToFile(destPath, originalDate.Value);
            }
            else
            {
                MetadataHelper.ApplyDateToFile(destPath, originalDate.Value);
            }
        }

        return Ok(new
        {
            fileName     = safeName,
            path         = destPath,
            originalDate = originalDate?.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
            message      = "Import réussi.",
        });
    }
}

public record UpdateConfigRequest(string? SyncTargetPath);
