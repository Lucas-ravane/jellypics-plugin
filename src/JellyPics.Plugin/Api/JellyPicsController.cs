using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using JellyPics.Plugin.Configuration;
using JellyPics.Plugin.Helpers;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace JellyPics.Plugin.Api;

/// <summary>
/// API JellyPics : upload avec métadonnées, browse bibliothèques/dossiers.
/// </summary>
[ApiController]
[Route("Plugins/JellyPics")]
[Authorize(Policy = "DefaultAuthorization")]
public class JellyPicsController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<JellyPicsController> _logger;

    public JellyPicsController(
        ILibraryManager libraryManager,
        ILogger<JellyPicsController> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    // ─── GET /Plugins/JellyPics/Config ────────────────────────────────────

    /// <summary>Retourne la configuration du plugin.</summary>
    [HttpGet("Config")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> GetConfig()
    {
        var config = Plugin.Instance.Configuration;
        return Ok(new
        {
            syncTargetPath = config.SyncTargetPath,
        });
    }

    // ─── PUT /Plugins/JellyPics/Config ────────────────────────────────────

    /// <summary>Met à jour la configuration.</summary>
    [HttpPut("Config")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult UpdateConfig([FromBody] UpdateConfigRequest request)
    {
        if (!string.IsNullOrEmpty(request.SyncTargetPath)
            && !Directory.Exists(request.SyncTargetPath))
        {
            return BadRequest(new
            {
                error = $"Dossier introuvable: {request.SyncTargetPath}"
            });
        }

        var config = Plugin.Instance.Configuration;
        config.SyncTargetPath = request.SyncTargetPath ?? config.SyncTargetPath;
        Plugin.Instance.SaveConfiguration();
        return NoContent();
    }

    // ─── GET /Plugins/JellyPics/Libraries ─────────────────────────────────

    /// <summary>
    /// Liste toutes les médiathèques photo/vidéo avec leur chemin racine.
    /// </summary>
    [HttpGet("Libraries")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> GetLibraries()
    {
        // CollectionType est une string dans les versions récentes de Jellyfin
        // "homevideos" = photos/vidéos maison, "photos" = photos uniquement
        var libraries = _libraryManager
            .GetVirtualFolders()
            .Where(f => f.CollectionType == "homevideos"
                     || f.CollectionType == "photos"
                     || string.IsNullOrEmpty(f.CollectionType))
            .Select(f => new
            {
                id     = f.ItemId,
                name   = f.Name,
                paths  = f.Locations,
            })
            .ToList();

        return Ok(libraries);
    }

    // ─── GET /Plugins/JellyPics/Folders?path=... ──────────────────────────

    /// <summary>
    /// Liste les sous-dossiers d'un dossier physique sur le serveur.
    /// path = chemin absolu côté serveur (encodé URL).
    /// </summary>
    [HttpGet("Folders")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<object> GetFolders([FromQuery] string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return BadRequest(new { error = "Chemin invalide ou introuvable." });

        var dirs = Directory
            .GetDirectories(path)
            .Select(d => new
            {
                name     = Path.GetFileName(d),
                fullPath = d,
                hasChildren = Directory.GetDirectories(d).Length > 0,
            })
            .OrderBy(d => d.name)
            .ToList();

        return Ok(dirs);
    }

    // ─── POST /Plugins/JellyPics/Upload ───────────────────────────────────

    /// <summary>
    /// Reçoit un fichier multipart et le dépose dans le dossier cible.
    ///
    /// Headers attendus :
    ///   X-Original-Date : date ISO 8601 de la photo/vidéo originale (UTC)
    ///
    /// Form fields :
    ///   file       : le fichier binaire
    ///   targetPath : chemin absolu du dossier de destination (optionnel,
    ///                écrase le SyncTargetPath si fourni)
    /// </summary>
    [HttpPost("Upload")]
    [RequestSizeLimit(500_000_000)] // 500 MB max
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<object>> Upload(
        [FromForm] IFormFile file,
        [FromForm] string? targetPath,
        [FromHeader(Name = "X-Original-Date")] string? originalDateHeader)
    {
        // ── Validation ─────────────────────────────────────────────────────
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Aucun fichier reçu." });

        var destination = targetPath ?? Plugin.Instance.Configuration.SyncTargetPath;
        if (string.IsNullOrEmpty(destination))
            return BadRequest(new
            {
                error = "Dossier de destination non configuré. " +
                        "Configurez-le dans Dashboard → Plugins → JellyPics, " +
                        "ou passez targetPath dans la requête."
            });

        if (!Directory.Exists(destination))
        {
            try { Directory.CreateDirectory(destination); }
            catch (Exception ex)
            {
                return BadRequest(new
                {
                    error = $"Impossible de créer le dossier: {ex.Message}"
                });
            }
        }

        // ── Date originale ─────────────────────────────────────────────────
        // Priorité : header X-Original-Date > EXIF du fichier reçu
        DateTime? originalDate = MetadataHelper.ParseClientDate(originalDateHeader);

        // ── Écriture du fichier ────────────────────────────────────────────
        var safeName = Path.GetFileName(file.FileName)
            .Replace("..", "_")
            .Trim();
        if (string.IsNullOrEmpty(safeName)) safeName = $"{Guid.NewGuid()}.bin";

        var destPath = Path.Combine(destination, safeName);

        // Évite d'écraser un fichier existant en ajoutant un suffixe
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
                FileAccess.Write, FileShare.None, 65536, useAsync: true);
            await file.CopyToAsync(stream);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur écriture fichier {Path}", destPath);
            return StatusCode(500, new { error = $"Erreur serveur: {ex.Message}" });
        }

        // ── Extraction EXIF si date non fournie par le client ──────────────
        originalDate ??= MetadataHelper.ExtractOriginalDate(destPath);

        // ── Application de la date ────────────────────────────────────────
        if (originalDate.HasValue)
        {
            var ext = Path.GetExtension(safeName).ToLowerInvariant();
            var isVideo = ext is ".mp4" or ".m4v" or ".mov" or ".mkv"
                              or ".webm" or ".avi" or ".3gp" or ".3g2"
                              or ".ts" or ".mts" or ".m2ts";

            if (isVideo)
            {
                // Réécriture des métadonnées dans le conteneur vidéo (sans ré-encodage)
                var ok = VideoMetadataHelper.ApplyDateToVideoContainer(
                    destPath, originalDate.Value, _logger);
                if (!ok)
                {
                    // Fallback : au moins les timestamps filesystem
                    MetadataHelper.ApplyDateToFile(destPath, originalDate.Value);
                }
            }
            else
            {
                // Photo : l'EXIF est déjà dans le fichier, on met juste les timestamps
                MetadataHelper.ApplyDateToFile(destPath, originalDate.Value);
            }

            _logger.LogInformation(
                "JellyPics: {File} ({Type}) → date {Date:s}",
                safeName, isVideo ? "vidéo" : "photo", originalDate.Value);
        }
        else
        {
            _logger.LogWarning(
                "JellyPics: {File} → aucune date originale trouvée", safeName);
        }

        return Ok(new
        {
            fileName     = safeName,
            path         = destPath,
            originalDate = originalDate?.ToString("o"),
            message      = "Import réussi. Jellyfin va indexer le fichier automatiquement.",
        });
    }
}

public record UpdateConfigRequest(string? SyncTargetPath);
