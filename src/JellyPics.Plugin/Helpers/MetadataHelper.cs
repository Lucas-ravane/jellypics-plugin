using System;
using System.IO;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace JellyPics.Plugin.Helpers;

/// <summary>
/// Utilitaires de lecture et écriture des métadonnées temporelles.
/// </summary>
public static class MetadataHelper
{
    /// <summary>
    /// Extrait la date originale d'un fichier (EXIF DateTimeOriginal,
    /// ou à défaut DateCreated, ou à défaut lastWriteTime du fichier).
    /// </summary>
    public static DateTime? ExtractOriginalDate(string filePath)
    {
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(filePath);
            foreach (var dir in directories)
            {
                if (dir is ExifSubIfdDirectory exif)
                {
                    if (exif.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dt))
                        return DateTime.SpecifyKind(dt, DateTimeKind.Local).ToUniversalTime();
                    if (exif.TryGetDateTime(ExifDirectoryBase.TagDateTimeDigitized, out var dtd))
                        return DateTime.SpecifyKind(dtd, DateTimeKind.Local).ToUniversalTime();
                }
            }
        }
        catch { /* Fichier non-JPEG ou EXIF absent */ }

        // Fallback: date de modification du fichier
        try
        {
            var info = new FileInfo(filePath);
            if (info.Exists && info.LastWriteTimeUtc.Year > 1970)
                return info.LastWriteTimeUtc;
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Applique la date originale aux timestamps du fichier sur le disque.
    /// Utilisé après écriture pour que Jellyfin indexe la bonne date.
    /// </summary>
    public static void ApplyDateToFile(string filePath, DateTime originalDateUtc)
    {
        try
        {
            File.SetLastWriteTimeUtc(filePath, originalDateUtc);
            File.SetCreationTimeUtc(filePath, originalDateUtc);
        }
        catch { }
    }

    /// <summary>
    /// Tente de parser une date ISO8601 envoyée par le client (header X-Original-Date).
    /// </summary>
    public static DateTime? ParseClientDate(string? headerValue)
    {
        if (string.IsNullOrEmpty(headerValue)) return null;
        if (DateTime.TryParse(headerValue,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out var dt))
        {
            return dt.ToUniversalTime();
        }
        return null;
    }
}
