# JellyPics Plugin

Plugin Jellyfin pour l'application **JellyPics** (Android / Windows).

## Fonctionnalités

- **Import avec métadonnées** : conserve la date originale (EXIF `DateTimeOriginal`) lors de l'import. Jellyfin indexe la bonne date.
- **Browse bibliothèques** : l'app peut lister toutes les médiathèques photo/vidéo et naviguer dans les sous-dossiers.
- **Sélection du dossier de destination** : l'utilisateur choisit dans quel (sous-)dossier importer lors d'un import sélectif.
- **Dossier de synchronisation** : configurable dans le Dashboard, utilisé pour la sync automatique.

## Installation

### Via dépôt (recommandé)

1. Dashboard Jellyfin → **Plugins** → **Repositories** → **+**
2. Nom : `JellyPics`, URL :
   ```
   https://raw.githubusercontent.com/VOTRE_USERNAME/jellypics-plugin/main/manifest.json
   ```
3. **Catalog** → chercher **JellyPics Upload** → **Install**
4. Redémarrer Jellyfin

### Manuelle

1. Télécharger `JellyPics.Plugin.zip` depuis les [Releases](../../releases)
2. Extraire dans le dossier `plugins/JellyPics.Plugin/` de Jellyfin
3. Redémarrer Jellyfin

## Configuration

Dashboard → Plugins → **JellyPics Upload** :

| Paramètre | Description |
|---|---|
| **Sync Target Path** | Dossier de destination pour la synchronisation automatique. Ex: `/media/photos/sync`. Doit être inclus dans une médiathèque. |

## API

| Méthode | Endpoint | Description |
|---|---|---|
| GET | `/Plugins/JellyPics/Config` | Configuration courante |
| PUT | `/Plugins/JellyPics/Config` | Modifier la configuration |
| GET | `/Plugins/JellyPics/Libraries` | Médiathèques photo/vidéo |
| GET | `/Plugins/JellyPics/Folders?path=...` | Sous-dossiers d'un chemin |
| POST | `/Plugins/JellyPics/Upload` | Uploader un fichier |

### Upload — détails

```
POST /Plugins/JellyPics/Upload
Authorization: MediaBrowser Token="..."
Content-Type: multipart/form-data
X-Original-Date: 2024-10-15T14:32:00Z   ← date originale ISO 8601

Form:
  file:       <binaire>
  targetPath: /media/photos/2024/octobre  ← optionnel, écrase SyncTargetPath
```

**Conservation de la date** :
1. Le client envoie `X-Original-Date` avec la date EXIF originale
2. Le plugin écrit la date sur le fichier (`LastWriteTime`, `CreationTime`)
3. Jellyfin indexe la bonne date lors du scan automatique

## Développement

```bash
# Compiler
cd src/JellyPics.Plugin
dotnet build -c Release

# Créer le ZIP pour installation manuelle
dotnet publish -c Release -o artifacts
cd artifacts && zip ../JellyPics.Plugin.zip JellyPics.Plugin.dll MetadataExtractor.dll
```

## Compatibilité

| Jellyfin | Plugin |
|---|---|
| 10.9.x | 1.x |
