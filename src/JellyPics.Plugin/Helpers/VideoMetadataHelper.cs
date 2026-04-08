using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace JellyPics.Plugin.Helpers;

/// <summary>
/// Réécriture des métadonnées temporelles dans les conteneurs vidéo
/// via ffmpeg (sans ré-encodage, codec copy).
///
/// Formats supportés : MP4, MOV, MKV, WebM, AVI, M4V, 3GP, TS.
/// </summary>
public static class VideoMetadataHelper
{
    // Extensions vidéo gérées
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".m4v", ".mov", ".mkv", ".webm",
            ".avi", ".3gp", ".3g2", ".ts", ".mts", ".m2ts"
        };

    /// <summary>
    /// Réécrit les métadonnées de date dans le conteneur vidéo.
    /// Retourne true si l'opération a réussi, false si ffmpeg absent ou erreur.
    /// </summary>
    public static bool ApplyDateToVideoContainer(
        string filePath,
        DateTime originalDateUtc,
        ILogger? logger = null)
    {
        var ext = Path.GetExtension(filePath);
        if (!SupportedExtensions.Contains(ext)) return false;

        var ffmpeg = FindFfmpeg();
        if (ffmpeg is null)
        {
            logger?.LogWarning("JellyPics: ffmpeg introuvable, métadonnées container non mises à jour pour {File}", 
                Path.GetFileName(filePath));
            return false;
        }

        var tempPath = filePath + ".tmp" + ext;
        try
        {
            var dateStr = originalDateUtc.ToString("yyyy-MM-ddTHH:mm:ssZ");

            // ── Arguments ffmpeg ───────────────────────────────────────────
            // -codec copy : aucun ré-encodage, juste réécriture du container
            // Les tags varient selon le format mais ffmpeg les mappe correctement
            // pour chaque conteneur (creation_time → MP4/MOV, DateUTC → MKV…)
            var args = ext.ToLowerInvariant() switch
            {
                ".avi" =>
                    $"-y -i \"{filePath}\" " +
                    $"-metadata ICRD=\"{dateStr}\" " +
                    $"-codec copy \"{tempPath}\"",

                ".mkv" or ".webm" =>
                    $"-y -i \"{filePath}\" " +
                    $"-metadata creation_time=\"{dateStr}\" " +
                    $"-metadata DATE_RECORDED=\"{dateStr}\" " +
                    $"-codec copy \"{tempPath}\"",

                _ =>  // MP4, MOV, M4V, TS, 3GP…
                    $"-y -i \"{filePath}\" " +
                    $"-metadata creation_time=\"{dateStr}\" " +
                    $"-movflags use_metadata_tags " +
                    $"-codec copy \"{tempPath}\""
            };

            var psi = new ProcessStartInfo
            {
                FileName               = ffmpeg,
                Arguments              = args,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Impossible de démarrer ffmpeg");

            // Timeout 5 minutes par fichier (les gros fichiers TS peuvent être lents)
            var finished = process.WaitForExit(300_000);
            if (!finished)
            {
                process.Kill();
                throw new TimeoutException("ffmpeg timeout (5 min)");
            }

            if (process.ExitCode != 0)
            {
                var stderr = process.StandardError.ReadToEnd();
                throw new InvalidOperationException(
                    $"ffmpeg exit {process.ExitCode}: {stderr[..Math.Min(200, stderr.Length)]}");
            }

            // Remplace l'original par le fichier traité
            File.Move(tempPath, filePath, overwrite: true);

            // Applique aussi les timestamps filesystem
            MetadataHelper.ApplyDateToFile(filePath, originalDateUtc);

            logger?.LogInformation(
                "JellyPics: métadonnées vidéo appliquées {File} → {Date:s}",
                Path.GetFileName(filePath), originalDateUtc);

            return true;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex,
                "JellyPics: erreur réécriture métadonnées vidéo {File}", 
                Path.GetFileName(filePath));

            // Nettoie le fichier temporaire en cas d'erreur
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }

            // Applique au moins les timestamps filesystem
            MetadataHelper.ApplyDateToFile(filePath, originalDateUtc);
            return false;
        }
    }

    /// <summary>
    /// Localise ffmpeg sur le système (PATH, chemins Jellyfin courants, chemins système).
    /// </summary>
    private static string? FindFfmpeg()
    {
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var binary    = isWindows ? "ffmpeg.exe" : "ffmpeg";

        // 1. Variable d'environnement explicite (prioritaire)
        var envPath = Environment.GetEnvironmentVariable("JELLYPICS_FFMPEG_PATH");
        if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
            return envPath;

        // 2. Chemin ffmpeg utilisé par Jellyfin (variable d'env standard)
        var jellyfinFfmpeg = Environment.GetEnvironmentVariable("JELLYFIN_FFMPEG");
        if (!string.IsNullOrEmpty(jellyfinFfmpeg) && File.Exists(jellyfinFfmpeg))
            return jellyfinFfmpeg;

        // 3. PATH système
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            var candidate = Path.Combine(dir, binary);
            if (File.Exists(candidate)) return candidate;
        }

        // 4. Chemins connus selon l'OS
        var knownPaths = isWindows
            ? new[]
              {
                  @"C:\Program Files\Jellyfin\Server\ffmpeg.exe",
                  @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
                  @"C:\ffmpeg\bin\ffmpeg.exe",
              }
            : new[]
              {
                  "/usr/bin/ffmpeg",
                  "/usr/local/bin/ffmpeg",
                  "/opt/jellyfin/bin/ffmpeg",
                  "/usr/share/jellyfin-ffmpeg/ffmpeg",  // Docker Jellyfin
              };

        foreach (var path in knownPaths)
        {
            if (File.Exists(path)) return path;
        }

        return null;
    }
}
