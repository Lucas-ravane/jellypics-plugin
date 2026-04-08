# Changelog

## [1.0.0] - 2025-04-08

### Ajouté
- Import de fichiers avec conservation de la date originale (EXIF / header `X-Original-Date`)
- API de listing des médiathèques photo/vidéo (`GET /Libraries`)
- API de navigation dans les dossiers (`GET /Folders`)
- API de configuration (`GET/PUT /Config`)
- Dossier de synchronisation configurable dans le Dashboard
- Sélection du dossier de destination par requête (`targetPath`)
- Support jusqu'à 500 MB par fichier
- Gestion de la déduplication (suffixe timestamp si fichier existant)
