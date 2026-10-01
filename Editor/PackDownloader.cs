using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using UnityEngine;
using UnityEngine.Networking;

namespace Zabaglione.PlateauAreaDownloader.Editor
{
    [Serializable]
    internal sealed class SelectedGml
    {
        public string cityCode;
        public string cityName;
        public string cityRoot;
        public int year;
        public string spec;
        public string type;
        public string code;
        public int maxLod;
        public string url;
        public long fileSize;
    }

    [Serializable]
    internal sealed class SavedFile
    {
        public string path;
        public long size;
        public string sha256;
    }

    [Serializable]
    internal sealed class PackManifest
    {
        public string key;
        public string packId;
        public string status;
        public string apiBase;
        public string placeName;
        public double west;
        public double south;
        public double east;
        public double north;
        public SelectedGml[] selectedGmls;
        public string[] metadataUrls;
        public long gmlBytes;
        public long zipBytes;
        public long expandedBytes;
        public SavedFile[] files;
        public string diagnostic;
    }

    internal sealed class PackLimits
    {
        public long MaxDownloadBytes = 10L * 1024 * 1024 * 1024;
        public long MaxExpandedBytes = 30L * 1024 * 1024 * 1024;
    }

    internal sealed class PackProgress
    {
        public string Stage;
        public long Bytes;
        public long TotalBytes;
        public int FilesCompleted;
        public int TotalFiles;
        public long XmlNodesScanned;
        public long ReferencesChecked;
        public string CurrentFile;
    }

    internal static class PackDownloader
    {
        private const string LockFileName = ".plateau-area-downloader.lock";
        private const string PreviousDatasetName = "dataset.previous";

        internal static string DefaultDataRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "../PLATEAUData~"));

        internal static string CityRootName(CatalogCity city)
        {
            if (city == null || !Uri.TryCreate(city.url, UriKind.Absolute, out var uri))
                throw new ArgumentException("City catalog is missing its archive URL.");
            return Path.GetFileNameWithoutExtension(uri.AbsolutePath);
        }

        internal static PackManifest CreateManifest(string placeName, double west, double south,
            double east, double north,
            IEnumerable<(CatalogCity city, string type, CatalogGml gml)> selected,
            string apiBase)
        {
            var selection = selected.ToArray();
            var entries = selection.Select(item => new SelectedGml
            {
                cityCode = item.city.cityCode,
                cityName = item.city.cityName,
                cityRoot = CityRootName(item.city),
                year = item.city.year,
                spec = item.city.spec,
                type = item.type,
                code = item.gml.code,
                maxLod = item.gml.maxLod,
                url = item.gml.url,
                fileSize = item.gml.fileSize
            }).GroupBy(item => item.url, StringComparer.Ordinal).Select(group => group.First())
                .OrderBy(item => item.url, StringComparer.Ordinal).ToArray();
            if (entries.Length == 0) throw new InvalidOperationException("No CityGML files selected.");
            var metadata = selection.SelectMany(item => item.city.metadataZipUrls ?? Array.Empty<string>())
                .Distinct(StringComparer.Ordinal).OrderBy(url => url, StringComparer.Ordinal).ToArray();
            var urls = entries.Select(entry => entry.url).Concat(metadata).ToArray();
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(apiBase + "\n" + string.Join("\n", urls)));
            var key = BitConverter.ToString(hash).Replace("-", "").Substring(0, 24).ToLowerInvariant();
            return new PackManifest
            {
                key = key,
                status = "new",
                apiBase = apiBase,
                placeName = placeName,
                west = west, south = south, east = east, north = north,
                selectedGmls = entries,
                metadataUrls = metadata,
                gmlBytes = entries.Any(entry => entry.fileSize <= 0) ? -1 : entries.Sum(entry => entry.fileSize)
            };
        }

        internal static string JobPath(string dataRoot, PackManifest manifest) =>
            Path.Combine(dataRoot, manifest.key);

        internal static PackManifest LoadManifest(string jobPath)
        {
            var path = Path.Combine(jobPath, "manifest.json");
            return File.Exists(path) ? JsonUtility.FromJson<PackManifest>(File.ReadAllText(path)) : null;
        }

        internal static async Task<PackManifest> DownloadAsync(PackManifest desired, string dataRoot,
            PackLimits limits, Action<PackProgress> progress, CancellationToken token)
        {
            if (desired == null || desired.selectedGmls == null || desired.selectedGmls.Length == 0)
                throw new ArgumentException("No selected CityGML data.");
            if (limits.MaxDownloadBytes <= 0 || limits.MaxExpandedBytes <= 0)
                throw new ArgumentException("Download limits must be positive.");
            EnsureSafeDataRoot(dataRoot);
            if (!IsJobKey(desired.key)) throw new ArgumentException("Invalid download job key.");
            using var dataRootLock = AcquireDataRootLock(dataRoot);
            EnsureDefaultDataIgnored(dataRoot);
            var jobPath = JobPath(dataRoot, desired);
            RejectReparsePointsOnPath(jobPath);
            if (Directory.Exists(jobPath) && !File.Exists(Path.Combine(jobPath, "manifest.json")) &&
                Directory.EnumerateFileSystemEntries(jobPath).Any())
                throw new IOException("Existing download job has no manifest; its contents were preserved.");
            Directory.CreateDirectory(jobPath);
            RejectReparsePointsOnPath(jobPath);
            RejectReparsePointsOnPath(Path.Combine(jobPath, "manifest.json"));
            var existing = LoadManifest(jobPath);
            if (File.Exists(Path.Combine(jobPath, "manifest.json")) && existing == null)
                throw new IOException("Download job manifest is unreadable; its contents were preserved.");
            if (existing != null && existing.key != desired.key)
                throw new IOException("Download job manifest key does not match its folder.");
            var manifest = existing ?? JsonUtility.FromJson<PackManifest>(JsonUtility.ToJson(desired));
            if (existing == null)
            {
                manifest.packId = null;
                manifest.status = "new";
                manifest.files = null;
            }
            manifest.placeName = desired.placeName;
            manifest.west = desired.west;
            manifest.south = desired.south;
            manifest.east = desired.east;
            manifest.north = desired.north;
            var datasetPath = Path.Combine(jobPath, "dataset");
            var stagingPath = Path.Combine(jobPath, "staging");
            var zipPath = Path.Combine(jobPath, "pack.zip");
            var validatedStaging = false;
            var recoveryComplete = false;
            var preserveCompleteOnCancel = existing != null && existing.status == "complete";
            try
            {
                await Task.Run(() => RecoverPreviousDataset(jobPath, manifest, token), token);
                recoveryComplete = true;
                if (existing != null && !await Task.Run(() =>
                        CleanupUnusedFiles(jobPath, manifest, false, progress)))
                    throw new IOException("Unsafe or inaccessible temporary files were preserved.");
                if (manifest.status == "complete")
                {
                    var valid = await Task.Run(() =>
                    {
                        if (!VerifySavedFiles(datasetPath, manifest.files, token, progress)) return false;
                        ValidateReferences(datasetPath, manifest.selectedGmls, token, progress);
                        return true;
                    }, token);
                    if (valid)
                    {
                        progress?.Invoke(new PackProgress { Stage = "finalizing", CurrentFile = "Saving manifest" });
                        SaveManifest(jobPath, manifest);
                        await DeletePreviousDatasetAsync(jobPath, manifest, progress);
                        progress?.Invoke(new PackProgress { Stage = "cached", Bytes = manifest.zipBytes, TotalBytes = manifest.zipBytes });
                        return manifest;
                    }
                    preserveCompleteOnCancel = false;
                }
                var urls = manifest.selectedGmls.Select(entry => entry.url)
                    .Concat(manifest.metadataUrls ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).ToArray();
                if (string.IsNullOrWhiteSpace(manifest.packId))
                {
                    manifest.packId = await PlateauApi.CreatePackAsync(urls, manifest.apiBase, token);
                    manifest.status = "pack-created";
                    SaveManifest(jobPath, manifest);
                }

                for (var downloadAttempt = 0; downloadAttempt < 2; downloadAttempt++)
                {
                    var recreated = false;
                    while (true)
                    {
                        token.ThrowIfCancellationRequested();
                        PackStatus status;
                        try
                        {
                            status = await PlateauApi.GetPackStatusAsync(manifest.packId, manifest.apiBase, token);
                        }
                        catch (System.Net.Http.HttpRequestException exception)
                            when (!recreated && (exception.Message.Contains("404") || exception.Message.Contains("410")))
                        {
                            manifest.packId = await PlateauApi.CreatePackAsync(urls, manifest.apiBase, token);
                            recreated = true;
                            SaveManifest(jobPath, manifest);
                            continue;
                        }
                        progress?.Invoke(new PackProgress { Stage = "preparing", Bytes = (long)(status.progress * 100), TotalBytes = 100 });
                        if (status.status == "succeeded") break;
                        if (status.status == "failed" || status.status == "expired")
                        {
                            if (!recreated)
                            {
                                manifest.packId = await PlateauApi.CreatePackAsync(urls, manifest.apiBase, token);
                                recreated = true;
                                SaveManifest(jobPath, manifest);
                                continue;
                            }
                            throw new IOException("Pack preparation failed: " + status.error);
                        }
                        await Task.Delay(TimeSpan.FromSeconds(3), token);
                    }
                    try
                    {
                        await DownloadZipAsync(manifest, zipPath, limits.MaxDownloadBytes, progress, token);
                        break;
                    }
                    catch (IOException exception) when (downloadAttempt == 0 &&
                        (exception.Message.Contains("HTTP 404") || exception.Message.Contains("HTTP 410")))
                    {
                        manifest.packId = await PlateauApi.CreatePackAsync(urls, manifest.apiBase, token);
                        manifest.status = "pack-created";
                        SaveManifest(jobPath, manifest);
                    }
                }
                manifest.zipBytes = new FileInfo(zipPath).Length;
                manifest.status = "downloaded";
                SaveManifest(jobPath, manifest);

                RejectReparsePointsOnPath(stagingPath);
                if (Directory.Exists(stagingPath)) throw new IOException("Uncleaned staging folder was preserved.");
                Directory.CreateDirectory(stagingPath);
                var files = await Task.Run(() =>
                {
                    var extracted = ExtractSafe(zipPath, stagingPath, limits.MaxExpandedBytes, progress, token);
                    ValidateReferences(stagingPath, manifest.selectedGmls, token, progress);
                    return extracted;
                }, token);
                manifest.files = files;
                manifest.expandedBytes = files.Sum(file => file.size);
                validatedStaging = true;
                progress?.Invoke(new PackProgress
                {
                    Stage = "finalizing",
                    CurrentFile = "Replacing dataset and saving manifest"
                });
                await Task.Run(() =>
                {
                    ReplaceDataset(jobPath);
                }, token);
                manifest.status = "complete";
                manifest.diagnostic = "";
                try { SaveManifest(jobPath, manifest); }
                catch (Exception saveError)
                {
                    try { RestorePreviousDatasetAfterSaveFailure(jobPath); }
                    catch (Exception restoreError)
                    {
                        throw new IOException("Manifest save and dataset rollback failed; data was preserved.",
                            new AggregateException(saveError, restoreError));
                    }
                    throw;
                }
                await DeletePreviousDatasetAsync(jobPath, manifest, progress);
                return manifest;
            }
            catch (Exception error)
            {
                if (!(error is OperationCanceledException && preserveCompleteOnCancel))
                {
                    manifest.status = error is OperationCanceledException ? "interrupted" : "failed";
                    manifest.diagnostic = error.ToString();
                    TrySaveManifest(jobPath, manifest, progress);
                }
                throw;
            }
            finally
            {
                var previous = Path.Combine(jobPath, PreviousDatasetName);
                var preserveStaging = validatedStaging &&
                    (!Directory.Exists(datasetPath) || Directory.Exists(previous));
                if (!recoveryComplete)
                {
                    try
                    {
                        if (!await Task.Run(() => CanDiscardStagingAfterUnfinishedRecovery(jobPath)))
                            preserveStaging = true;
                    }
                    catch (Exception safetyError)
                    {
                        preserveStaging = true;
                        ReportCleanupWarning(jobPath, manifest,
                            "Staging was preserved because its safety check failed: " + safetyError.Message,
                            progress);
                    }
                }
                try
                {
                    await Task.Run(() => CleanupUnusedFiles(jobPath, manifest, preserveStaging, progress));
                }
                catch (Exception cleanupError)
                {
                    ReportCleanupWarning(jobPath, manifest,
                        "Temporary cleanup failed: " + cleanupError.Message, progress);
                }
            }
        }

        private static void EnsureSafeDataRoot(string dataRoot)
        {
            if (string.IsNullOrWhiteSpace(dataRoot)) throw new ArgumentException("Data root is empty.");
            var full = Path.GetFullPath(dataRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var assets = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var comparison = Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (full.Equals(assets, comparison) ||
                full.StartsWith(assets + Path.DirectorySeparatorChar, comparison))
                throw new ArgumentException("CityGML data root must be outside Assets.");
        }

        private static bool IsJobKey(string key) => key != null && key.Length == 24 &&
            key.All(character => character >= '0' && character <= '9' ||
                                 character >= 'a' && character <= 'f');

        private static FileStream AcquireDataRootLock(string dataRoot)
        {
            RejectReparsePointsOnPath(dataRoot);
            Directory.CreateDirectory(dataRoot);
            RejectReparsePointsOnPath(dataRoot);
            var lockPath = Path.Combine(dataRoot, LockFileName);
            RejectReparsePointsOnPath(lockPath);
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException error)
            {
                throw new IOException("Data root is busy or its lock file is unavailable.", error);
            }
        }

        private static void RejectReparsePointsOnPath(string path)
        {
            var full = Path.GetFullPath(path);
            var current = Path.GetPathRoot(full);
            var parts = full.Substring(current.Length).Split(new[]
            {
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar
            }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                current = Path.Combine(current, part);
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("Symbolic links and junctions are not allowed in download paths: " + current);
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
            }
        }

        private static void RejectReparsePointsInTree(string directory, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            RejectReparsePointsOnPath(directory);
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                token.ThrowIfCancellationRequested();
                RejectReparsePointsOnPath(entry);
                if (Directory.Exists(entry)) RejectReparsePointsInTree(entry, token);
            }
        }

        private static void RecoverPreviousDataset(string jobPath, PackManifest manifest,
            CancellationToken token)
        {
            var dataset = Path.Combine(jobPath, "dataset");
            RejectReparsePointsOnPath(dataset);
            if (Directory.Exists(dataset)) RejectReparsePointsInTree(dataset, token);
            var previous = Path.Combine(jobPath, PreviousDatasetName);
            RejectReparsePointsOnPath(previous);
            if (!Directory.Exists(previous)) return;
            if (Directory.Exists(dataset) && manifest.status == "complete" &&
                VerifySavedFiles(dataset, manifest.files, token)) return;
            RejectReparsePointsInTree(previous, token);
            if (File.Exists(dataset))
                throw new IOException("Dataset recovery is blocked by a file; previous dataset was preserved.");
            if (!Directory.Exists(dataset))
            {
                Directory.Move(previous, dataset);
                return;
            }
            throw new IOException("Dataset replacement needs manual recovery; both dataset folders were preserved.");
        }

        private static bool CanDiscardStagingAfterUnfinishedRecovery(string jobPath)
        {
            var previous = Path.Combine(jobPath, PreviousDatasetName);
            RejectReparsePointsOnPath(previous);
            if (File.Exists(previous) || Directory.Exists(previous)) return false;
            var dataset = Path.Combine(jobPath, "dataset");
            RejectReparsePointsOnPath(dataset);
            if (File.Exists(dataset)) return false;
            if (Directory.Exists(dataset)) RejectReparsePointsInTree(dataset);
            var staging = Path.Combine(jobPath, "staging");
            RejectReparsePointsOnPath(staging);
            if (File.Exists(staging)) return false;
            if (Directory.Exists(staging)) RejectReparsePointsInTree(staging);
            return true;
        }

        internal static void ReplaceDataset(string jobPath, Action beforeStagingMove = null)
        {
            var staging = Path.Combine(jobPath, "staging");
            var dataset = Path.Combine(jobPath, "dataset");
            var previous = Path.Combine(jobPath, PreviousDatasetName);
            RejectReparsePointsInTree(staging);
            RejectReparsePointsOnPath(dataset);
            RejectReparsePointsOnPath(previous);
            if (File.Exists(dataset) || File.Exists(previous) || Directory.Exists(previous))
                throw new IOException("Dataset replacement path is occupied; staged data was preserved.");
            var movedPrevious = false;
            if (Directory.Exists(dataset))
            {
                RejectReparsePointsInTree(dataset);
                Directory.Move(dataset, previous);
                movedPrevious = true;
            }
            try
            {
                beforeStagingMove?.Invoke();
                Directory.Move(staging, dataset);
            }
            catch (Exception moveError)
            {
                if (movedPrevious)
                {
                    try { Directory.Move(previous, dataset); }
                    catch (Exception restoreError)
                    {
                        throw new IOException("Dataset replacement and rollback failed; both folders were preserved.",
                            new AggregateException(moveError, restoreError));
                    }
                }
                throw;
            }
        }

        private static void RestorePreviousDatasetAfterSaveFailure(string jobPath)
        {
            var previous = Path.Combine(jobPath, PreviousDatasetName);
            RejectReparsePointsOnPath(previous);
            if (!Directory.Exists(previous)) return;
            var dataset = Path.Combine(jobPath, "dataset");
            var staging = Path.Combine(jobPath, "staging");
            RejectReparsePointsInTree(previous);
            RejectReparsePointsInTree(dataset);
            RejectReparsePointsOnPath(staging);
            if (File.Exists(staging) || Directory.Exists(staging))
                throw new IOException("Staging is occupied; both datasets were preserved.");
            Directory.Move(dataset, staging);
            try { Directory.Move(previous, dataset); }
            catch
            {
                Directory.Move(staging, dataset);
                throw;
            }
        }

        private static void DeletePreviousDataset(string jobPath, PackManifest manifest,
            Action<PackProgress> progress)
        {
            var previous = Path.Combine(jobPath, PreviousDatasetName);
            try
            {
                RejectReparsePointsOnPath(previous);
                if (!Directory.Exists(previous)) return;
                RejectReparsePointsInTree(previous);
                Directory.Delete(previous, true);
            }
            catch (Exception error)
            {
                ReportCleanupWarning(jobPath, manifest, "Could not remove previous dataset: " + error.Message,
                    progress);
            }
        }

        private static async Task DeletePreviousDatasetAsync(string jobPath, PackManifest manifest,
            Action<PackProgress> progress)
        {
            try { await Task.Run(() => DeletePreviousDataset(jobPath, manifest, progress)); }
            catch (Exception error)
            {
                ReportCleanupWarning(jobPath, manifest, "Could not remove previous dataset: " + error.Message,
                    progress);
            }
        }

        private static bool CleanupUnusedFiles(string jobPath, PackManifest manifest,
            bool preserveStaging, Action<PackProgress> progress)
        {
            try
            {
                RejectReparsePointsOnPath(jobPath);
                RejectReparsePointsOnPath(Path.Combine(jobPath, "manifest.json"));
                var saved = LoadManifest(jobPath);
                if (!IsJobKey(Path.GetFileName(jobPath)) || saved == null ||
                    saved.key != Path.GetFileName(jobPath) || saved.key != manifest.key)
                    throw new IOException("Temporary files have no matching owned manifest.");
            }
            catch (Exception error)
            {
                ReportCleanupWarning(jobPath, manifest, "Temporary files were preserved: " + error.Message,
                    progress, false);
                return false;
            }
            var cleaned = true;
            foreach (var name in new[] { "pack.zip.part", "staging", "pack.zip" })
            {
                if (name == "staging" && preserveStaging) continue;
                var path = Path.Combine(jobPath, name);
                try
                {
                    RejectReparsePointsOnPath(path);
                    if (name == "staging")
                    {
                        if (File.Exists(path)) throw new IOException("Expected a directory, found a file.");
                        if (Directory.Exists(path))
                        {
                            RejectReparsePointsInTree(path);
                            Directory.Delete(path, true);
                        }
                    }
                    else
                    {
                        if (Directory.Exists(path)) throw new IOException("Expected a file, found a directory.");
                        if (File.Exists(path)) File.Delete(path);
                    }
                }
                catch (Exception error)
                {
                    cleaned = false;
                    ReportCleanupWarning(jobPath, manifest,
                        "Temporary file was preserved (" + name + "): " + error.Message, progress);
                }
            }
            return cleaned;
        }

        private static void TrySaveManifest(string jobPath, PackManifest manifest,
            Action<PackProgress> progress)
        {
            try { SaveManifest(jobPath, manifest); }
            catch (Exception error)
            {
                var message = "Could not save download status: " + error.Message;
                try { Debug.LogWarning(message); }
                catch (Exception) { }
                try { progress?.Invoke(new PackProgress { Stage = "cleanup-warning", CurrentFile = message }); }
                catch (Exception) { }
            }
        }

        private static void ReportCleanupWarning(string jobPath, PackManifest manifest, string message,
            Action<PackProgress> progress, bool persist = true)
        {
            try { Debug.LogWarning(message); }
            catch (Exception) { }
            try { progress?.Invoke(new PackProgress { Stage = "cleanup-warning", CurrentFile = message }); }
            catch (Exception) { }
            if (!persist) return;
            manifest.diagnostic = string.IsNullOrEmpty(manifest.diagnostic)
                ? message : manifest.diagnostic + "\n" + message;
            TrySaveManifest(jobPath, manifest, progress);
        }

        private static void EnsureDefaultDataIgnored(string dataRoot)
        {
            if (!Path.GetFullPath(dataRoot).Equals(DefaultDataRoot,
                    Application.platform == RuntimePlatform.WindowsEditor
                        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return;
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            if (!Directory.Exists(Path.Combine(projectRoot, ".git"))) return;
            var ignorePath = Path.Combine(projectRoot, ".gitignore");
            var ignore = File.Exists(ignorePath) ? File.ReadAllText(ignorePath) : "";
            if (ignore.Split('\n').Any(line => line.TrimEnd('\r').Trim() == "/PLATEAUData~/")) return;
            File.AppendAllText(ignorePath,
                (ignore.Length > 0 && !ignore.EndsWith("\n", StringComparison.Ordinal) ? "\n" : "") +
                "/PLATEAUData~/\n");
        }

        private static async Task DownloadZipAsync(PackManifest manifest, string zipPath, long limit,
            Action<PackProgress> progress, CancellationToken token)
        {
            var partial = zipPath + ".part";
            RejectReparsePointsOnPath(zipPath);
            RejectReparsePointsOnPath(partial);
            if (File.Exists(partial)) File.Delete(partial);
            var url = manifest.apiBase.TrimEnd('/') + "/citygml/pack/" +
                Uri.EscapeDataString(manifest.packId) + ".zip";
            using var request = UnityWebRequest.Get(url);
            request.downloadHandler = new DownloadHandlerFile(partial);
            request.SetRequestHeader("User-Agent", "PLATEAU-Area-Downloader/0.1");
            using var cancellation = token.Register(request.Abort);
            var transfer = request.SendWebRequest();
            long total = -1;
            while (!transfer.isDone)
            {
                token.ThrowIfCancellationRequested();
                if (long.TryParse(request.GetResponseHeader("Content-Length"), out var length)) total = length;
                if (total > limit || (long)request.downloadedBytes > limit)
                {
                    request.Abort();
                    throw new IOException("Pack exceeds download limit.");
                }
                progress?.Invoke(new PackProgress
                {
                    Stage = "downloading", Bytes = (long)request.downloadedBytes, TotalBytes = total
                });
                await Task.Delay(200, token);
            }
            token.ThrowIfCancellationRequested();
            if (request.result != UnityWebRequest.Result.Success)
                throw new IOException("Pack download failed: HTTP " + request.responseCode + " " + request.error);
            var size = new FileInfo(partial).Length;
            if (size > limit) throw new IOException("Pack exceeds download limit.");
            RejectReparsePointsOnPath(zipPath);
            RejectReparsePointsOnPath(partial);
            if (File.Exists(zipPath)) File.Delete(zipPath);
            File.Move(partial, zipPath);
        }

        internal static SavedFile[] ExtractSafe(string zipPath, string root, long limit,
            Action<PackProgress> progress, CancellationToken token)
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var entries = archive.Entries.Where(entry => !entry.FullName.EndsWith("/", StringComparison.Ordinal)).ToArray();
            var declared = entries.Sum(entry => entry.Length);
            if (declared > limit) throw new IOException("Pack exceeds expanded size limit.");
            var rootFull = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
            var result = new List<SavedFile>(entries.Length);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long expanded = 0;
            long lastReportedExpanded = 0;
            progress?.Invoke(new PackProgress
            {
                Stage = "extracting", Bytes = 0, TotalBytes = declared, TotalFiles = entries.Length
            });
            foreach (var entry in entries)
            {
                token.ThrowIfCancellationRequested();
                var relative = entry.FullName;
                if (string.IsNullOrEmpty(relative) || relative.StartsWith("/", StringComparison.Ordinal) ||
                    relative.IndexOf('\\') >= 0 || relative.IndexOf(':') >= 0 ||
                    relative.Split('/').Any(part => part == ".." || part == "." || part.Length == 0) ||
                    ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                    throw new IOException("Unsafe ZIP entry path: " + relative);
                if (!seen.Add(relative)) throw new IOException("Duplicate ZIP entry path: " + relative);
                var destination = Path.GetFullPath(Path.Combine(root, relative));
                if (!destination.StartsWith(rootFull, StringComparison.Ordinal))
                    throw new IOException("ZIP entry escapes destination: " + relative);
                RejectReparsePointsOnPath(destination);
                Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? root);
                RejectReparsePointsOnPath(destination);
                using var input = entry.Open();
                using var output = File.Create(destination);
                using var sha = SHA256.Create();
                var buffer = new byte[1024 * 1024];
                int read;
                long size = 0;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    size += read;
                    expanded += read;
                    if (expanded > limit) throw new IOException("Pack exceeds expanded size limit.");
                    output.Write(buffer, 0, read);
                    sha.TransformBlock(buffer, 0, read, buffer, 0);
                    if (expanded - lastReportedExpanded >= 16L * 1024 * 1024)
                    {
                        progress?.Invoke(new PackProgress
                        {
                            Stage = "extracting", Bytes = expanded, TotalBytes = declared,
                            FilesCompleted = result.Count, TotalFiles = entries.Length,
                            CurrentFile = relative
                        });
                        lastReportedExpanded = expanded;
                    }
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                result.Add(new SavedFile
                {
                    path = relative,
                    size = size,
                    sha256 = BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant()
                });
                progress?.Invoke(new PackProgress
                {
                    Stage = "extracting", Bytes = expanded, TotalBytes = declared,
                    FilesCompleted = result.Count, TotalFiles = entries.Length, CurrentFile = relative
                });
                lastReportedExpanded = expanded;
            }
            return result.ToArray();
        }

        internal static void ValidateReferences(string dataRoot, SelectedGml[] selected, CancellationToken token,
            Action<PackProgress> progress = null)
        {
            var root = Path.GetFullPath(dataRoot) + Path.DirectorySeparatorChar;
            var missing = new HashSet<string>(StringComparer.Ordinal);
            long nodesScanned = 0;
            long referencesChecked = 0;
            progress?.Invoke(new PackProgress
            {
                Stage = "validating-references", TotalFiles = selected.Length
            });
            for (var index = 0; index < selected.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                var gml = selected[index];
                var name = Path.GetFileName(new Uri(gml.url).AbsolutePath);
                var path = Path.Combine(dataRoot, gml.cityRoot, "udx", gml.type, name);
                var relativePath = Path.Combine(gml.cityRoot, "udx", gml.type, name);
                progress?.Invoke(new PackProgress
                {
                    Stage = "validating-references", FilesCompleted = index,
                    TotalFiles = selected.Length, XmlNodesScanned = nodesScanned,
                    ReferencesChecked = referencesChecked, CurrentFile = relativePath
                });
                if (!File.Exists(path))
                {
                    missing.Add(path);
                    progress?.Invoke(new PackProgress
                    {
                        Stage = "validating-references", FilesCompleted = index + 1,
                        TotalFiles = selected.Length, XmlNodesScanned = nodesScanned,
                        ReferencesChecked = referencesChecked, CurrentFile = relativePath
                    });
                    continue;
                }
                var basePath = Path.GetDirectoryName(path) ?? dataRoot;
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
                using var reader = XmlReader.Create(path, settings);
                while (!reader.EOF)
                {
                    token.ThrowIfCancellationRequested();
                    nodesScanned++;
                    if (nodesScanned % 50000 == 0)
                    {
                        progress?.Invoke(new PackProgress
                        {
                            Stage = "validating-references", FilesCompleted = index,
                            TotalFiles = selected.Length, XmlNodesScanned = nodesScanned,
                            ReferencesChecked = referencesChecked, CurrentFile = relativePath
                        });
                    }
                    if (reader.NodeType != XmlNodeType.Element)
                    {
                        reader.Read();
                        continue;
                    }
                    if (reader.HasAttributes)
                    {
                        while (reader.MoveToNextAttribute())
                            if (reader.LocalName == "codeSpace" || reader.LocalName == "href")
                            {
                                CheckReference(reader.Value, basePath, root, missing);
                                referencesChecked++;
                            }
                        reader.MoveToElement();
                    }
                    if (reader.LocalName == "imageURI" && !reader.IsEmptyElement)
                    {
                        CheckReference(reader.ReadElementContentAsString(), basePath, root, missing);
                        referencesChecked++;
                        continue;
                    }
                    reader.Read();
                }
                progress?.Invoke(new PackProgress
                {
                    Stage = "validating-references", FilesCompleted = index + 1,
                    TotalFiles = selected.Length, XmlNodesScanned = nodesScanned,
                    ReferencesChecked = referencesChecked, CurrentFile = relativePath
                });
            }
            if (missing.Count > 0)
                throw new IOException("Missing local references (" + missing.Count + "): " +
                    string.Join(", ", missing.Take(8)));
        }

        private static void CheckReference(string reference, string basePath, string root,
            HashSet<string> missing)
        {
            if (string.IsNullOrWhiteSpace(reference) || reference[0] == '#') return;
            if (Uri.TryCreate(reference, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == "urn")) return;
            var pathPart = Uri.UnescapeDataString(reference.Split('#')[0]);
            if (string.IsNullOrWhiteSpace(pathPart)) return;
            var full = Path.GetFullPath(Path.Combine(basePath, pathPart));
            if (!full.StartsWith(root, StringComparison.Ordinal) || !File.Exists(full)) missing.Add(reference);
        }

        internal static bool VerifySavedFiles(string root, SavedFile[] files,
            CancellationToken token = default, Action<PackProgress> progress = null)
        {
            if (files == null || files.Length == 0) return false;
            var rootFull = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
            var buffer = new byte[1024 * 1024];
            var totalBytes = files.Sum(entry => entry.size);
            long verifiedBytes = 0;
            progress?.Invoke(new PackProgress
            {
                Stage = "verifying-files", TotalBytes = totalBytes, TotalFiles = files.Length
            });
            for (var index = 0; index < files.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                var entry = files[index];
                var path = Path.GetFullPath(Path.Combine(root, entry.path));
                if (!path.StartsWith(rootFull, StringComparison.Ordinal) || !File.Exists(path) ||
                    new FileInfo(path).Length != entry.size) return false;
                using var sha = SHA256.Create();
                using var stream = File.OpenRead(path);
                int read;
                var lastReportedBytes = verifiedBytes;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    sha.TransformBlock(buffer, 0, read, buffer, 0);
                    verifiedBytes += read;
                    if (verifiedBytes - lastReportedBytes >= 16L * 1024 * 1024)
                    {
                        progress?.Invoke(new PackProgress
                        {
                            Stage = "verifying-files", Bytes = verifiedBytes, TotalBytes = totalBytes,
                            FilesCompleted = index, TotalFiles = files.Length, CurrentFile = entry.path
                        });
                        lastReportedBytes = verifiedBytes;
                    }
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                var hash = BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
                if (!string.Equals(hash, entry.sha256, StringComparison.OrdinalIgnoreCase)) return false;
                progress?.Invoke(new PackProgress
                {
                    Stage = "verifying-files", Bytes = verifiedBytes, TotalBytes = totalBytes,
                    FilesCompleted = index + 1, TotalFiles = files.Length, CurrentFile = entry.path
                });
            }
            return true;
        }

        private static void SaveManifest(string jobPath, PackManifest manifest)
        {
            var path = Path.Combine(jobPath, "manifest.json");
            var pending = path + ".tmp";
            RejectReparsePointsOnPath(path);
            RejectReparsePointsOnPath(pending);
            File.WriteAllText(pending, JsonUtility.ToJson(manifest, true));
            if (File.Exists(path)) File.Replace(pending, path, null);
            else File.Move(pending, path);
        }
    }
}
