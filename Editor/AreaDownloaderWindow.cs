using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Zabaglione.PlateauAreaDownloader.Editor
{
    [Serializable]
    internal sealed class PhotonCacheEntry
    {
        public long savedUtcTicks;
        public PhotonFeature[] features;
    }

    internal static class MapZoomMath
    {
        internal static double ApplyWheel(float verticalTicks, double currentZoom, double minZoom,
            double maxZoom, double sensitivity = 1d)
        {
            if (float.IsNaN(verticalTicks) || float.IsInfinity(verticalTicks) || verticalTicks == 0)
                return currentZoom;
            var ticks = Math.Max(-2d, Math.Min(2d, verticalTicks));
            return Math.Max(minZoom, Math.Min(maxZoom, currentZoom - ticks * 0.125d * sensitivity));
        }

        internal static double WorldScale(double zoom) => 256d * Math.Pow(2d, zoom);

        internal static int TileZoom(double zoom) => (int)Math.Floor(zoom);

        internal static double TileSize(double zoom) => 256d * Math.Pow(2d, zoom - TileZoom(zoom));

        internal static (double x, double y) ToWorld(double latitude, double longitude, double zoom)
        {
            var scale = WorldScale(zoom);
            var sin = Math.Sin(Math.Max(-85.0511, Math.Min(85.0511, latitude)) * Math.PI / 180);
            return ((longitude + 180) / 360 * scale,
                (0.5 - Math.Log((1 + sin) / (1 - sin)) / (4 * Math.PI)) * scale);
        }

        internal static (double latitude, double longitude) FromWorld(double x, double y, double zoom, bool normalizeLongitude = true)
        {
            var scale = WorldScale(zoom);
            return (Math.Atan(Math.Sinh(Math.PI * (1 - 2 * y / scale))) * 180 / Math.PI,
                normalizeLongitude ? GeoBounds.NormalizeLongitude(x / scale * 360 - 180) : x / scale * 360 - 180);
        }
    }

    internal sealed class AreaDownloaderWindow : EditorWindow
    {
        private sealed class TileRequest
        {
            internal readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
        }

        private const string PackagePath = "Packages/com.zabaglione.plateau-area-downloader/Editor/";
        private const string Prefs = "Zabaglione.PlateauAreaDownloader.";
        private readonly ConcurrentQueue<PackProgress> progressQueue = new ConcurrentQueue<PackProgress>();
        private readonly Dictionary<string, Texture2D> tileTextures = new Dictionary<string, Texture2D>();
        private readonly Dictionary<string, TileRequest> tileRequests = new Dictionary<string, TileRequest>();
        private readonly SemaphoreSlim tileDownloadSlots = new SemaphoreSlim(4);
        private readonly Dictionary<string, PhotonFeature[]> searchCache = new Dictionary<string, PhotonFeature[]>();
        private FontAsset japaneseFont;
        private CancellationTokenSource operation;
        private GeoBounds bounds;
        private double centerLatitude = 35.6586;
        private double centerLongitude = 139.7454;
        private double zoom = 15;
        private int zoomSensitivityPercent = 100;
        private double targetZoom = 15;
        private double lastZoomFrameTime;
        private bool zoomAnimating;
        private Vector2 zoomAnchorPoint;
        private double zoomAnchorLatitude;
        private double zoomAnchorLongitude;
        private Vector2 pointerStart;
        private double pointerCenterLatitude;
        private double pointerCenterLongitude;
        private bool selecting;
        private bool pointerActive;
        private HashSet<string> neededTileKeys = new HashSet<string>();
        private string mapProvider = "gsi";
        private string googleMapType = "roadmap";
        private Task<GoogleTileSession> googleSessionTask;
        private string googleSessionIdentity;
        private DateTimeOffset googleSessionFailedAt;
        private CancellationTokenSource googleCancellation;
        private bool googleControlsInitialized;
        private IVisualElementScheduledItem attributionUpdate;
        private int attributionGeneration;
        private CancellationTokenSource attributionCancellation;
        private string attributionViewportUrl;
        private bool attributionReady;
        private bool attributionRequestPending;
        private bool attributionUpdateScheduled;
        // Per-window dependencies keep asynchronous map behavior testable without changing saved settings.
        internal Func<string, string, CancellationToken, Task<GoogleTileSession>> CreateGoogleSession;
        internal Func<string, CancellationToken, Task<string>> FetchGoogleCopyright;
        internal Func<string, CancellationToken, Task<byte[]>> FetchTile = PlateauApi.GetTileAsync;
        private int previewGeneration;
        private string placeName = "東京タワー";
        [SerializeField] private bool includeBuildings = true;
        [SerializeField] private bool includeRoads;
        [SerializeField] private bool includeTerrain;
        [SerializeField] private List<string> additionalTypes = new List<string>();
        [SerializeField] private bool hasSelectionState;
        private PackManifest desired;
        private PackManifest downloaded;
        private string downloadedDataRoot;
        private VisualElement map;
        private VisualElement tiles;
        private VisualElement googleLogo;
        private Label mapMessage;
        private Label mapAttribution;
        private VisualElement meshOverlay;
        private VisualElement boundsOverlay;
        private VisualElement candidates;
        private Label previewResult;
        private Label meshSummary;
        private Label mapLegend;
        private Label downloadSummary;
        private Label progressLabel;
        private Label handoffResult;
        private VisualElement handoffCities;
        private TextField searchField;

        [MenuItem("Tools/PLATEAU Area Downloader")]
        private static void Open()
        {
            var window = Resources.FindObjectsOfTypeAll<AreaDownloaderWindow>().FirstOrDefault();
            if (window == null)
            {
                window = GetWindow<AreaDownloaderWindow>("PLATEAU Area Downloader");
                window.Show();
            }
            window.Focus();
            window.RefreshOnMenuOpen();
        }

        private void RefreshOnMenuOpen()
        {
            if (map == null || map.panel == null) return;
            foreach (var request in tileRequests.Values) request.Cancellation.Cancel();
            tileRequests.Clear();
            if (googleSessionTask != null && googleSessionTask.IsFaulted) googleSessionTask = null;
            RefreshMap();
            UpdateDownloadSelection();
            Repaint();
        }

        private void OnEnable()
        {
            minSize = new Vector2(560, 420);
            bounds = GeoBounds.FromCenter(centerLatitude, centerLongitude);
            targetZoom = zoom;
            zoomAnimating = false;
            EditorApplication.update += DrainProgress;
            EditorApplication.update += TickZoom;
        }

        private void OnDisable()
        {
            EditorApplication.update -= DrainProgress;
            EditorApplication.update -= TickZoom;
            operation?.Cancel();
            operation?.Dispose();
            foreach (var request in tileRequests.Values) request.Cancellation.Cancel();
            tileRequests.Clear();
            CancelAttribution();
            CancelGoogleSession();
            foreach (var texture in tileTextures.Values)
                if (texture != null) DestroyImmediate(texture);
            tileTextures.Clear();
            if (japaneseFont != null) DestroyImmediate(japaneseFont);
        }

        public void CreateGUI()
        {
            CancelAttribution();
            if (!hasSelectionState)
            {
                previewGeneration++;
                desired = null;
                hasSelectionState = true;
            }
            rootVisualElement.Clear();
            googleControlsInitialized = false;
            var layout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(PackagePath + "AreaWindow.uxml");
            if (layout == null)
            {
                rootVisualElement.Add(new Label("Package UI is missing."));
                return;
            }
            layout.CloneTree(rootVisualElement);
            if (japaneseFont == null)
            {
                japaneseFont = Application.platform == RuntimePlatform.OSXEditor
                    ? FontAsset.CreateFontAsset("Hiragino Sans", "W3")
                    : FontAsset.CreateFontAsset("Yu Gothic UI", "Regular");
                if (japaneseFont == null)
                    japaneseFont = FontAsset.CreateFontAsset("Noto Sans CJK JP", "Regular");
                if (japaneseFont != null) japaneseFont.isMultiAtlasTexturesEnabled = true;
            }
            if (japaneseFont != null)
                rootVisualElement.style.unityFontDefinition = new StyleFontDefinition(japaneseFont);
            map = Q<VisualElement>("map");
            tiles = Q<VisualElement>("tiles");
            googleLogo = Q<VisualElement>("google-logo");
            mapMessage = Q<Label>("map-message");
            mapAttribution = Q<Label>("map-attribution");
            meshOverlay = Q<VisualElement>("mesh-overlay");
            boundsOverlay = Q<VisualElement>("bounds-overlay");
            candidates = Q<VisualElement>("candidates");
            previewResult = Q<Label>("preview-result");
            meshSummary = Q<Label>("mesh-summary");
            mapLegend = Q<Label>("map-legend");
            downloadSummary = Q<Label>("download-summary");
            progressLabel = Q<Label>("progress");
            handoffResult = Q<Label>("handoff-result");
            handoffCities = Q<VisualElement>("handoff-cities");
            searchField = Q<TextField>("search");
            var page = Q<ScrollView>("scroll");
            page.RegisterCallback<GeometryChangedEvent>(e =>
                page.EnableInClassList("narrow", e.newRect.width < 760));
            searchField.value = placeName;
            Q<TextField>("photon-url").value = EditorPrefs.GetString(Prefs + "photon", PlateauApi.DefaultPhotonBase);
            Q<TextField>("api-url").value = EditorPrefs.GetString(Prefs + "api", PlateauApi.DefaultApiBase);
            Q<TextField>("tiles-url").value = EditorPrefs.GetString(Prefs + "tiles", PlateauApi.DefaultGsiTiles);
            Q<TextField>("save-root").value = EditorPrefs.GetString(Prefs + "save", PackDownloader.DefaultDataRoot);
            Q<FloatField>("download-limit").value = EditorPrefs.GetFloat(Prefs + "downloadLimit", 10);
            Q<FloatField>("expanded-limit").value = EditorPrefs.GetFloat(Prefs + "expandedLimit", 30);
            zoomSensitivityPercent = Mathf.Clamp(EditorPrefs.GetInt(Prefs + "zoomSensitivity", 100), 25, 300);
            var sensitivity = Q<SliderInt>("zoom-sensitivity");
            sensitivity.lowValue = 25;
            sensitivity.highValue = 300;
            sensitivity.SetValueWithoutNotify(zoomSensitivityPercent);
            Q<Label>("zoom-sensitivity-value").text = zoomSensitivityPercent + "%";
            sensitivity.RegisterValueChangedCallback(e =>
            {
                zoomSensitivityPercent = e.newValue;
                Q<Label>("zoom-sensitivity-value").text = zoomSensitivityPercent + "%";
                EditorPrefs.SetInt(Prefs + "zoomSensitivity", zoomSensitivityPercent);
            });
            Q<Button>("map-source-link").clicked += () =>
                Application.OpenURL(UseGoogle ? GoogleMapTiles.TermsUrl : "https://maps.gsi.go.jp/development/ichiran.html");
            mapProvider = EditorPrefs.GetString(Prefs + "mapProvider", "gsi") == "google" ? "google" : "gsi";
            var provider = Q<DropdownField>("map-provider");
            provider.choices = new List<string> { "地理院タイル", "Google Maps" };
            provider.SetValueWithoutNotify(UseGoogle ? "Google Maps" : "地理院タイル");
            provider.RegisterValueChangedCallback(e =>
            {
                mapProvider = e.newValue == "Google Maps" ? "google" : "gsi";
                EditorPrefs.SetString(Prefs + "mapProvider", mapProvider);
                ResetMapTiles();
            });
            UpdateMapSource();
            Q<Button>("search-button").clicked += Search;
            Q<Button>("preview").clicked += Preview;
            Q<Button>("apply-bounds").clicked += ApplyBounds;
            Q<Button>("download").clicked += Download;
            Q<Button>("cancel").clicked += () => operation?.Cancel();
            searchField.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return) Search(); });
            map.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                StopZoomAnimation();
                RefreshMap();
            });
            map.RegisterCallback<PointerDownEvent>(OnPointerDown);
            map.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            map.RegisterCallback<PointerUpEvent>(OnPointerUp);
            map.RegisterCallback<WheelEvent>(OnWheel);
            var typeChoices = Q<ScrollView>("type-choices");
            foreach (var (name, label) in CityGmlTypes.All)
            {
                var toggle = new Toggle(label) { name = name };
                toggle.SetValueWithoutNotify(IsTypeSelected(name));
                typeChoices.Add(toggle);
                toggle.RegisterValueChangedCallback(e =>
                {
                    if (name == "bldg") includeBuildings = e.newValue;
                    else if (name == "tran") includeRoads = e.newValue;
                    else if (name == "dem") includeTerrain = e.newValue;
                    else
                    {
                        additionalTypes ??= new List<string>();
                        if (e.newValue && !additionalTypes.Contains(name)) additionalTypes.Add(name);
                        if (!e.newValue) additionalTypes.Remove(name);
                    }
                    InvalidatePreview();
                });
            }
            Q<Button>("select-all-types").clicked += () => SetAllTypes(true);
            Q<Button>("clear-types").clicked += () => SetAllTypes(false);
            foreach (var name in new[] { "photon-url", "api-url", "tiles-url", "save-root" })
            {
                var field = Q<TextField>(name);
                field.RegisterValueChangedCallback(e =>
                {
                    EditorPrefs.SetString(Prefs + name.Split('-')[0], e.newValue);
                    if (name == "api-url") InvalidatePreview();
                });
            }
            WriteBounds();
            RefreshMap();
            if (desired != null) ShowPreviewResult(desired);
            UpdateDownloadSelection();
        }

        private T Q<T>(string name) where T : VisualElement => rootVisualElement.Q<T>(name);
        private bool IsTypeSelected(string type) => type == "bldg" ? includeBuildings :
            type == "tran" ? includeRoads : type == "dem" ? includeTerrain :
            additionalTypes != null && additionalTypes.Contains(type);

        private void SetAllTypes(bool selected)
        {
            foreach (var (code, _) in CityGmlTypes.All)
                Q<Toggle>(code).SetValueWithoutNotify(selected);
            includeBuildings = includeRoads = includeTerrain = selected;
            additionalTypes = selected
                ? CityGmlTypes.All.Select(item => item.Code)
                    .Where(code => code != "bldg" && code != "tran" && code != "dem").ToList()
                : new List<string>();
            InvalidatePreview();
        }
        private string ApiBase => Q<TextField>("api-url").value.TrimEnd('/');
        private string DataRoot => Path.GetFullPath(Q<TextField>("save-root").value);

        private async void Search()
        {
            var query = searchField.value?.Trim();
            if (string.IsNullOrEmpty(query)) return;
            candidates.Clear();
            candidates.Add(new Label("検索中…"));
            try
            {
                var searchKey = Q<TextField>("photon-url").value + "\n" + query;
                if (!searchCache.TryGetValue(searchKey, out var results))
                {
                    var cacheKey = SearchCacheKey(Q<TextField>("photon-url").value, query);
                    var stored = EditorPrefs.GetString(cacheKey, "");
                    var cached = string.IsNullOrEmpty(stored) ? null : JsonUtility.FromJson<PhotonCacheEntry>(stored);
                    if (cached != null && cached.features != null &&
                        DateTime.UtcNow.Ticks - cached.savedUtcTicks < TimeSpan.FromDays(7).Ticks)
                        results = cached.features;
                    else
                    {
                        results = await PlateauApi.SearchPlacesAsync(query, Q<TextField>("photon-url").value,
                            CancellationToken.None);
                        EditorPrefs.SetString(cacheKey, JsonUtility.ToJson(new PhotonCacheEntry
                        {
                            savedUtcTicks = DateTime.UtcNow.Ticks,
                            features = results
                        }));
                    }
                    searchCache[searchKey] = results;
                }
                candidates.Clear();
                if (results.Length == 0) candidates.Add(new Label("候補なし。地図または座標入力で進められます。"));
                foreach (var item in results)
                {
                    if (item.geometry?.coordinates == null || item.geometry.coordinates.Length < 2) continue;
                    var p = item.properties;
                    var name = p?.name ?? query;
                    var location = string.Join(" / ", new[] { p?.state, p?.city, p?.district, p?.street }
                        .Where(part => !string.IsNullOrWhiteSpace(part)));
                    var button = new Button(() =>
                    {
                        StopZoomAnimation();
                        placeName = name;
                        centerLongitude = item.geometry.coordinates[0];
                        centerLatitude = item.geometry.coordinates[1];
                        bounds = GeoBounds.FromCenter(centerLatitude, centerLongitude);
                        WriteBounds();
                        InvalidatePreview();
                        RefreshMap();
                        Preview();
                    }) { text = name + "　" + location + "　[" + (p?.osm_value ?? "施設") + "]" };
                    candidates.Add(button);
                }
            }
            catch (Exception error)
            {
                candidates.Clear();
                candidates.Add(new Label("検索できませんでした: " + error.Message + "。地図または座標入力で進められます。"));
            }
        }

        private void ApplyBounds()
        {
            try
            {
                StopZoomAnimation();
                bounds = new GeoBounds(Q<DoubleField>("west").value, Q<DoubleField>("south").value,
                    Q<DoubleField>("east").value, Q<DoubleField>("north").value);
                centerLatitude = (bounds.South + bounds.North) / 2;
                centerLongitude = bounds.CenterLongitude;
                InvalidatePreview();
                RefreshMap();
            }
            catch (Exception error) { previewResult.text = error.Message; }
        }

        private void WriteBounds()
        {
            Q<DoubleField>("west").SetValueWithoutNotify(bounds.West);
            Q<DoubleField>("south").SetValueWithoutNotify(bounds.South);
            Q<DoubleField>("east").SetValueWithoutNotify(bounds.East);
            Q<DoubleField>("north").SetValueWithoutNotify(bounds.North);
        }

        private void InvalidatePreview()
        {
            previewGeneration++;
            desired = null;
            downloaded = null;
            downloadedDataRoot = null;
            handoffCities?.Clear();
            if (handoffResult != null) handoffResult.text = "対象が変わりました。取得後にフォルダを表示します。";
            previewResult.text = "CityGMLファイルを再検索してください。";
            meshSummary.text = "範囲または種類を変更しました。検索結果を更新してください。";
            UpdateDownloadSelection();
            RefreshOverlay();
        }

        private void UpdateDownloadSelection()
        {
            if (downloadSummary == null) return;
            downloadSummary.text = desired == null
                ? "CityGMLファイルを検索するとダウンロードできます。"
                : "ダウンロード対象: " + desired.selectedGmls.Length + " CityGMLファイル / GML " +
                  FormatBytes(desired.gmlBytes);
            Q<Button>("download").SetEnabled(desired != null && operation == null);
        }

        private async void Preview()
        {
            var generation = ++previewGeneration;
            var requestedBounds = bounds;
            var types = CityGmlTypes.All.Select(item => item.Code)
                .Where(type => Q<Toggle>(type).value).ToArray();
            desired = null;
            UpdateDownloadSelection();
            RefreshOverlay();
            if (types.Length == 0)
            {
                previewResult.text = "検索するデータ種別を選んでください。";
                meshSummary.text = "検索するデータ種別を選んでください。";
                return;
            }
            previewResult.text = "CityGMLファイルを検索中…";
            meshSummary.text = "対象区画とファイル数を照会中…";
            try
            {
                var cities = await PlateauApi.SearchCityGmlAsync(requestedBounds.West, requestedBounds.South,
                    requestedBounds.East, requestedBounds.North, types, ApiBase, CancellationToken.None);
                if (generation != previewGeneration) return;
                var selected = cities.SelectMany(city => types.SelectMany(type => city.FilesFor(type)
                    .Where(gml => !string.IsNullOrEmpty(gml.url))
                    .Where(gml => JapanMeshCode.GetCatalogBounds(gml.code, gml.url).Intersects(requestedBounds, false))
                    .Select(gml => (city, type, gml)))).ToArray();
                if (selected.Length == 0)
                {
                    desired = null;
                    previewResult.text = "この範囲に選択した種類のCityGMLはありません。";
                    meshSummary.text = "この範囲の取得対象はありません。";
                    RefreshOverlay();
                    return;
                }
                desired = PackDownloader.CreateManifest(placeName, requestedBounds.West, requestedBounds.South,
                    requestedBounds.East, requestedBounds.North, selected, ApiBase);
                ShowPreviewResult(desired);
                UpdateDownloadSelection();
                RefreshOverlay();
            }
            catch (Exception error)
            {
                if (generation != previewGeneration) return;
                desired = null;
                previewResult.text = "CityGMLファイルの検索に失敗しました: " + error.Message;
                meshSummary.text = "取得区画を確認できませんでした。";
                UpdateDownloadSelection();
                RefreshOverlay();
            }
        }

        private void ShowPreviewResult(PackManifest manifest)
        {
            var entries = manifest.selectedGmls;
            if (entries == null || entries.Length == 0)
            {
                desired = null;
                meshSummary.text = "この範囲の取得対象はありません。";
                previewResult.text = "CityGMLファイルを再検索してください。";
                return;
            }
            var descriptions = entries.GroupBy(item => item.cityCode)
                .Select(group => group.First().cityName + " " + group.First().year +
                    " / 仕様" + group.First().spec).ToArray();
            var lod = entries.Max(item => item.maxLod);
            meshSummary.text = BuildMeshSummary(entries);
            previewResult.text = string.Join("、", descriptions) + "\n" +
                entries.Length + "ファイル / カタログ上の最大LOD " + lod +
                "\nGML: " + FormatBytes(manifest.gmlBytes) + "、付属データを含む取得量: 不明" +
                "\n選択範囲と交差するファイル全体を取得します。";
        }

        private static string BuildMeshSummary(SelectedGml[] entries)
        {
            var parts = entries.GroupBy(entry => entry.type)
                .Select(group => (name: CityGmlTypes.LabelFor(group.Key),
                    cells: group.Select(entry => JapanMeshCode.GetCatalogBounds(entry.code, entry.url)).Distinct().Count(),
                    files: group.Select(entry => entry.url).Distinct(StringComparer.Ordinal).Count()))
                .Select(part => part.name + " " + part.cells + "区画 / " + part.files + "ファイル");
            return "取得前の確認: " + string.Join("　|　", parts) +
                "。青い指定範囲と交差する区画のファイル全体を取得します。";
        }

        private static string SearchCacheKey(string endpoint, string query)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(endpoint + "\n" + query));
            return Prefs + "search." + BitConverter.ToString(bytes).Replace("-", "");
        }

        private async void Download()
        {
            if (desired == null) { progressLabel.text = "先にCityGMLファイルを検索してください。"; return; }
            if (operation != null) { progressLabel.text = "別の処理を実行中です。"; return; }
            try
            {
                var requested = desired;
                var generation = previewGeneration;
                var dataRoot = DataRoot;
                var downloadLimit = Q<FloatField>("download-limit").value;
                var expandedLimit = Q<FloatField>("expanded-limit").value;
                if (downloadLimit <= 0 || expandedLimit <= 0) throw new ArgumentException("容量上限は正の値を指定してください。");
                EditorPrefs.SetFloat(Prefs + "downloadLimit", downloadLimit);
                EditorPrefs.SetFloat(Prefs + "expandedLimit", expandedLimit);
                operation = new CancellationTokenSource();
                UpdateDownloadSelection();
                var limits = new PackLimits
                {
                    MaxDownloadBytes = (long)(downloadLimit * 1024d * 1024 * 1024),
                    MaxExpandedBytes = (long)(expandedLimit * 1024d * 1024 * 1024)
                };
                var completed = await PackDownloader.DownloadAsync(requested, dataRoot, limits,
                    step => progressQueue.Enqueue(step), operation.Token);
                if (generation != previewGeneration || desired != requested)
                {
                    progressLabel.text = "旧条件のダウンロードが完了しました。現在の範囲は再検索してください。";
                    return;
                }
                downloaded = completed;
                downloadedDataRoot = dataRoot;
                progressLabel.text = "ダウンロード完了: " + downloaded.files.Length + "ファイル / ZIP " +
                    FormatBytes(downloaded.zipBytes) + " / 展開後 " + FormatBytes(downloaded.expandedBytes);
                ShowHandoffCities();
            }
            catch (OperationCanceledException) { progressLabel.text = "ダウンロードを中断しました。再度「CityGMLをダウンロード」で確認・再開できます。"; }
            catch (Exception error) { progressLabel.text = "ダウンロード失敗: " + error.Message; }
            finally
            {
                while (progressQueue.TryDequeue(out _)) { }
                operation?.Dispose();
                operation = null;
                UpdateDownloadSelection();
            }
        }

        private void ShowHandoffCities()
        {
            handoffCities.Clear();
            if (downloaded == null || downloaded.status != "complete") return;
            var datasetRoot = Path.Combine(PackDownloader.JobPath(downloadedDataRoot, downloaded), "dataset");
            foreach (var city in downloaded.selectedGmls.GroupBy(entry => entry.cityRoot))
            {
                var entry = city.First();
                var folder = Path.GetFullPath(Path.Combine(datasetRoot, city.Key));
                handoffCities.Add(new Label(entry.cityName + " " + entry.year + " / 仕様" + entry.spec));
                var pathLabel = new Label(folder) { tooltip = folder };
                pathLabel.AddToClassList("result");
                handoffCities.Add(pathLabel);
                handoffCities.Add(new Button(() => OpenSdkForCity(folder))
                    { text = "パスをコピーしてSDKを開く" });
            }
            handoffResult.text = "公式SDKで都市ごとに「参照...」からフォルダを選択してください。Macの選択画面では⌘⇧Gでコピーしたパスを入力できます。";
        }

        private void OpenSdkForCity(string folder)
        {
            if (!Directory.Exists(Path.Combine(folder, "udx")))
            {
                handoffResult.text = "都市フォルダが見つかりません。取得結果を確認してください: " + folder;
                return;
            }
            EditorGUIUtility.systemCopyBuffer = folder;
            handoffResult.text = EditorApplication.ExecuteMenuItem("PLATEAU/PLATEAU SDK")
                ? "パスをコピーしました。公式SDKで「都市の追加 → ローカル → 入力フォルダ → 参照...」を選んでください。"
                : "パスをコピーしました。公式SDKを「PLATEAU → PLATEAU SDK」から手動で開いてください。";
        }

        private void DrainProgress()
        {
            if (progressLabel == null) return;
            while (progressQueue.TryDequeue(out var step))
            {
                var bytes = FormatBytes(step.Bytes) +
                    (step.TotalBytes > 0 ? " / " + FormatBytes(step.TotalBytes) : "");
                var files = step.TotalFiles > 0
                    ? "、ファイル " + step.FilesCompleted + " / " + step.TotalFiles : "";
                switch (step.Stage)
                {
                    case "preparing":
                        progressLabel.text = "取得用Packを準備中: " + step.Bytes + "%";
                        break;
                    case "downloading":
                        progressLabel.text = "ZIPを取得中: " + bytes;
                        break;
                    case "extracting":
                        progressLabel.text = "ZIPを展開・ファイルを記録中: " + bytes + files;
                        break;
                    case "validating-references":
                        progressLabel.text = "展開済みCityGMLの参照先を確認中: GML " +
                            step.FilesCompleted + " / " + step.TotalFiles +
                            "、XML走査 " + step.XmlNodesScanned + "、参照 " + step.ReferencesChecked +
                            (string.IsNullOrEmpty(step.CurrentFile) ? "" : "\n確認中: " + Path.GetFileName(step.CurrentFile));
                        break;
                    case "verifying-files":
                        progressLabel.text = "保存済みファイルのサイズとSHA-256を照合中: " + bytes + files;
                        break;
                    case "finalizing":
                        progressLabel.text = "確認済みデータを保存し、取得記録を確定中…";
                        break;
                    case "cached":
                        progressLabel.text = "保存済みデータの検証が完了しました。";
                        break;
                    default:
                        progressLabel.text = step.Stage;
                        break;
                }
            }
        }

        private static string FormatBytes(long value) => value < 0 ? "不明" :
            value >= 1024L * 1024 * 1024 ? (value / (1024d * 1024 * 1024)).ToString("F2", CultureInfo.InvariantCulture) + " GiB" :
            (value / (1024d * 1024)).ToString("F1", CultureInfo.InvariantCulture) + " MiB";

        private void OnPointerDown(PointerDownEvent e)
        {
            if (e.button != 0) return;
            StopZoomAnimation();
            pointerActive = true;
            selecting = e.shiftKey;
            pointerStart = map.WorldToLocal(e.position);
            pointerCenterLatitude = centerLatitude;
            pointerCenterLongitude = centerLongitude;
            map.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent e)
        {
            if (!pointerActive) return;
            if (selecting)
            {
                ShowScreenBounds(pointerStart, map.WorldToLocal(e.position));
            }
            else
            {
                var start = ToWorld(pointerCenterLatitude, pointerCenterLongitude);
                var currentPosition = map.WorldToLocal(e.position);
                var current = FromWorld(start.x - (currentPosition.x - pointerStart.x),
                    start.y - (currentPosition.y - pointerStart.y));
                centerLatitude = current.latitude;
                centerLongitude = current.longitude;
                RefreshMap();
            }
        }

        private void OnPointerUp(PointerUpEvent e)
        {
            if (!pointerActive) return;
            pointerActive = false;
            map.ReleasePointer(e.pointerId);
            if (selecting)
            {
                var a = ScreenToGeo(pointerStart);
                var end = map.WorldToLocal(e.position);
                var b = ScreenToGeo(end);
                if (Math.Abs(end.x - pointerStart.x) > 5 && Math.Abs(end.y - pointerStart.y) > 5)
                {
                    bounds = GeoBounds.FromUnwrapped(Math.Min(a.longitude, b.longitude), Math.Min(a.latitude, b.latitude),
                        Math.Max(a.longitude, b.longitude), Math.Max(a.latitude, b.latitude));
                    WriteBounds();
                    InvalidatePreview();
                }
                selecting = false;
                RefreshOverlay();
            }
        }

        private void OnWheel(WheelEvent e)
        {
            if (pointerActive) { e.StopPropagation(); return; }
            if (Mathf.Abs(e.delta.y) <= Mathf.Abs(e.delta.x)) return;
            var next = MapZoomMath.ApplyWheel(e.delta.y / WheelEvent.scrollDeltaPerTick,
                targetZoom, 5, 18, zoomSensitivityPercent / 100d);
            next = Math.Max(zoom - 1d, Math.Min(zoom + 1d, next));
            if (Math.Abs(next - targetZoom) < 0.000001d) { e.StopPropagation(); return; }
            var point = map.WorldToLocal(e.mousePosition);
            var anchor = ScreenToGeo(point);
            zoomAnchorPoint = point;
            zoomAnchorLatitude = anchor.latitude;
            zoomAnchorLongitude = anchor.longitude;
            targetZoom = next;
            if (!zoomAnimating) lastZoomFrameTime = EditorApplication.timeSinceStartup;
            zoomAnimating = true;
            e.StopPropagation();
        }

        private void TickZoom()
        {
            if (!zoomAnimating || map == null || map.contentRect.width <= 0) return;
            var now = EditorApplication.timeSinceStartup;
            var elapsed = Math.Max(0d, Math.Min(0.05d, now - lastZoomFrameTime));
            lastZoomFrameTime = now;
            var next = zoom + (targetZoom - zoom) * (1d - Math.Exp(-elapsed / 0.065d));
            if (Math.Abs(targetZoom - next) < 0.001d)
            {
                next = targetZoom;
                zoomAnimating = false;
            }
            zoom = next;
            var anchor = ToWorld(zoomAnchorLatitude, zoomAnchorLongitude);
            var adjusted = FromWorld(anchor.x - zoomAnchorPoint.x + map.contentRect.width / 2,
                anchor.y - zoomAnchorPoint.y + map.contentRect.height / 2);
            centerLatitude = adjusted.latitude;
            centerLongitude = adjusted.longitude;
            RefreshMap();
        }

        private void StopZoomAnimation()
        {
            zoomAnimating = false;
            targetZoom = zoom;
        }

        private (double x, double y) ToWorld(double latitude, double longitude)
        {
            return MapZoomMath.ToWorld(latitude, longitude, zoom);
        }

        private (double latitude, double longitude) FromWorld(double x, double y)
        {
            return MapZoomMath.FromWorld(x, y, zoom);
        }

        private (double latitude, double longitude) ScreenToGeo(Vector2 point)
        {
            var center = ToWorld(centerLatitude, centerLongitude);
            // Keep endpoints in the same world copy while selecting a rectangle or computing a viewport.
            return MapZoomMath.FromWorld(center.x + point.x - map.contentRect.width / 2,
                center.y + point.y - map.contentRect.height / 2, zoom, normalizeLongitude: false);
        }

        private Vector2 GeoToScreen(double latitude, double longitude)
        {
            var center = ToWorld(centerLatitude, centerLongitude);
            var nearLongitude = centerLongitude + GeoBounds.NormalizeLongitude(longitude - centerLongitude);
            var point = ToWorld(latitude, nearLongitude);
            return new Vector2((float)(point.x - center.x + map.contentRect.width / 2),
                (float)(point.y - center.y + map.contentRect.height / 2));
        }

        private void RefreshMap()
        {
            if (map == null || tiles == null || map.contentRect.width <= 0) return;
            tiles.Clear();
            var needed = new HashSet<string>();
            var missing = new HashSet<string>();
            var center = ToWorld(centerLatitude, centerLongitude);
            var tileZoom = MapZoomMath.TileZoom(zoom);
            var tileSize = MapZoomMath.TileSize(zoom);
            var tileCount = 1 << tileZoom;
            int firstX = (int)Math.Floor((center.x - map.contentRect.width / 2) / tileSize);
            int lastX = (int)Math.Floor((center.x + map.contentRect.width / 2) / tileSize);
            int firstY = (int)Math.Floor((center.y - map.contentRect.height / 2) / tileSize);
            int lastY = (int)Math.Floor((center.y + map.contentRect.height / 2) / tileSize);
            for (var x = firstX; x <= lastX; x++)
            for (var y = firstY; y <= lastY; y++)
            {
                if (y < 0 || y >= tileCount) continue;
                var tileX = ((x % tileCount) + tileCount) % tileCount;
                var key = tileZoom + "/" + tileX + "/" + y;
                needed.Add(key);
                var visual = new VisualElement { pickingMode = PickingMode.Ignore };
                visual.style.position = Position.Absolute;
                visual.style.left = (float)(x * tileSize - center.x + map.contentRect.width / 2);
                visual.style.top = (float)(y * tileSize - center.y + map.contentRect.height / 2);
                visual.style.width = (float)tileSize;
                visual.style.height = (float)tileSize;
                tiles.Add(visual);
                if (tileTextures.TryGetValue(key, out var cached)) visual.style.backgroundImage = new StyleBackground(cached);
                else
                {
                    ShowFallbackTile(visual, tileZoom, tileX, y, tileSize);
                    missing.Add(key);
                }
            }
            foreach (var stale in tileRequests.Keys.Where(key => !needed.Contains(key)).ToArray())
            {
                tileRequests[stale].Cancellation.Cancel();
                tileRequests.Remove(stale);
            }
            neededTileKeys = needed;
            foreach (var key in missing) EnsureTileRequest(key);
            RefreshOverlay();
            ScheduleAttributionUpdate();
        }

        private bool UseGoogle => mapProvider == "google";
        private string GoogleApiKey => Q<TextField>("google-api-key").value.Trim();

        private void ResetMapTiles()
        {
            foreach (var request in tileRequests.Values) request.Cancellation.Cancel();
            tileRequests.Clear();
            foreach (var texture in tileTextures.Values)
                if (texture != null) DestroyImmediate(texture);
            tileTextures.Clear();
            CancelAttribution();
            // Pending Google requests belong to the previous source and must not delay the next one.
            CancelGoogleSession();
            UpdateMapSource();
            RefreshMap();
        }

        private void InitializeGoogleControls()
        {
            if (googleControlsInitialized) return;
            googleControlsInitialized = true;
            var savedMapType = EditorPrefs.GetString(Prefs + "googleMapType", "roadmap");
            googleMapType = GoogleMapTiles.MapTypes.Any(type => type.Code == savedMapType) ? savedMapType : "roadmap";
            var mapType = Q<DropdownField>("google-map-type");
            mapType.choices = GoogleMapTiles.MapTypes.Select(type => type.Label).ToList();
            mapType.SetValueWithoutNotify(GoogleMapTiles.MapTypes.First(type => type.Code == googleMapType).Label);
            mapType.RegisterValueChangedCallback(e =>
            {
                googleMapType = GoogleMapTiles.MapTypes.First(type => type.Label == e.newValue).Code;
                EditorPrefs.SetString(Prefs + "googleMapType", googleMapType);
                ResetMapTiles();
            });
            var apiKey = Q<TextField>("google-api-key");
            apiKey.SetValueWithoutNotify(EditorPrefs.GetString(Prefs + "googleApiKey", ""));
            apiKey.RegisterValueChangedCallback(e =>
            {
                EditorPrefs.SetString(Prefs + "googleApiKey", e.newValue);
                if (UseGoogle) ResetMapTiles();
            });
        }

        private void CancelGoogleSession()
        {
            googleSessionIdentity = null;
            googleSessionTask = null;
            googleCancellation?.Cancel();
            googleCancellation?.Dispose();
            googleCancellation = null;
        }

        private void UpdateMapSource()
        {
            if (UseGoogle) InitializeGoogleControls();
            Q<Label>("map-source-label").text = UseGoogle ? "地図: Google Maps" : "地図: 地理院タイル（国土地理院）";
            Q<Button>("map-source-link").text = UseGoogle ? "Google Maps 利用規約" : "地理院タイル一覧";
            Q<DropdownField>("google-map-type").style.display = UseGoogle ? DisplayStyle.Flex : DisplayStyle.None;
            googleLogo.style.display = UseGoogle ? DisplayStyle.Flex : DisplayStyle.None;
            Q<TextField>("google-api-key").style.display = UseGoogle ? DisplayStyle.Flex : DisplayStyle.None;
            if (UseGoogle)
            {
                // The outlined logo keeps contrast on busy tiles: dark outline for imagery, light outline otherwise.
                googleLogo.style.backgroundImage = new StyleBackground(AssetDatabase.LoadAssetAtPath<Texture2D>(
                    PackagePath + (googleMapType == "satellite"
                        ? "GoogleMaps_Logo_WithDarkOutline_2x.png"
                        : "GoogleMaps_Logo_WithLightOutline_2x.png")));
            }
            else googleLogo.style.backgroundImage = StyleKeyword.None;
            tiles.style.visibility = UseGoogle ? Visibility.Hidden : Visibility.Visible;
            mapAttribution.EnableInClassList("google-attribution", UseGoogle);
            SetMapAttribution(UseGoogle ? "" : "地理院タイル（国土地理院）");
            ShowMapMessage(UseGoogle && string.IsNullOrEmpty(GoogleApiKey)
                ? "Google Maps を表示するには、接続先の「Google Maps API キー」を設定してください。"
                : null);
        }

        private void SetMapAttribution(string text)
        {
            mapAttribution.text = text;
            mapAttribution.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void ShowMapMessage(string text)
        {
            mapMessage.text = text ?? "";
            mapMessage.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private Task<GoogleTileSession> EnsureGoogleSessionAsync()
        {
            var apiKey = GoogleApiKey;
            if (string.IsNullOrEmpty(apiKey)) throw new InvalidOperationException("Google Maps API key is not set.");
            var identity = googleMapType + "\n" + apiKey;
            var now = DateTimeOffset.UtcNow;
            if (googleSessionTask != null && googleSessionIdentity == identity &&
                (!googleSessionTask.IsCompleted ||
                 (googleSessionTask.IsCompletedSuccessfully
                     ? GoogleMapTiles.IsUsable(googleSessionTask.Result, now)
                     : !GoogleMapTiles.CanRetry(googleSessionFailedAt, now))))
                return googleSessionTask;
            googleSessionIdentity = identity;
            if (googleCancellation == null) googleCancellation = new CancellationTokenSource();
            googleSessionTask = CreateGoogleSessionAsync(googleMapType, apiKey, identity, googleCancellation.Token);
            return googleSessionTask;
        }

        private async Task<GoogleTileSession> CreateGoogleSessionAsync(string mapType, string apiKey, string identity,
            CancellationToken token)
        {
            try
            {
                var session = await (CreateGoogleSession ?? PlateauApi.CreateGoogleSessionAsync)(mapType, apiKey, token);
                if (googleSessionIdentity == identity && !token.IsCancellationRequested)
                {
                    ShowMapMessage(null);
                    ScheduleAttributionUpdate();
                }
                return session;
            }
            catch (Exception error)
            {
                if (googleSessionIdentity == identity && !token.IsCancellationRequested)
                {
                    googleSessionFailedAt = DateTimeOffset.UtcNow;
                    ShowMapMessage("Google Maps のタイルを取得できませんでした: " + error.Message +
                                   "。API キーと Map Tiles API の有効化を確認してください。地図を動かすと再試行します。");
                }
                throw;
            }
        }

        private void CancelAttribution()
        {
            attributionGeneration++;
            attributionUpdate?.Pause();
            attributionUpdateScheduled = false;
            attributionCancellation?.Cancel();
            attributionCancellation?.Dispose();
            attributionCancellation = null;
            attributionViewportUrl = null;
            attributionReady = false;
        }

        private void ScheduleAttributionUpdate()
        {
            if (!UseGoogle || map == null) return;
            var task = googleSessionTask;
            string url = null;
            if (task != null && task.IsCompletedSuccessfully && map.contentRect.width > 0 && map.contentRect.height > 0)
            {
                var northWest = ScreenToGeo(Vector2.zero);
                var southEast = ScreenToGeo(new Vector2(map.contentRect.width, map.contentRect.height));
                var viewport = GeoBounds.FromUnwrapped(northWest.longitude, southEast.latitude,
                    southEast.longitude, northWest.latitude);
                url = GoogleMapTiles.ViewportUrl(task.Result.session, GoogleApiKey, MapZoomMath.TileZoom(zoom),
                    viewport.West, viewport.South, viewport.East, viewport.North);
            }
            if (url != attributionViewportUrl)
            {
                CancelAttribution();
                attributionViewportUrl = url;
                if (url != null)
                    attributionCancellation = CancellationTokenSource.CreateLinkedTokenSource(googleCancellation.Token);
                SetMapAttribution("");
            }
            tiles.style.visibility = attributionReady ? Visibility.Visible : Visibility.Hidden;
            if (url == null || attributionReady || attributionRequestPending || attributionUpdateScheduled) return;
            QueueAttributionUpdate(500);
        }

        private void QueueAttributionUpdate(long delayMilliseconds)
        {
            attributionUpdateScheduled = true;
            attributionUpdate = map.schedule.Execute(() =>
            {
                attributionUpdateScheduled = false;
                UpdateAttribution();
            }).StartingIn(delayMilliseconds);
        }

        private async void UpdateAttribution()
        {
            var task = googleSessionTask;
            var url = attributionViewportUrl;
            if (!UseGoogle || task == null || !task.IsCompletedSuccessfully || url == null ||
                attributionReady || attributionRequestPending || attributionCancellation == null) return;
            var generation = attributionGeneration;
            var token = attributionCancellation.Token;
            attributionRequestPending = true;
            var retry = false;
            try
            {
                var copyright = await (FetchGoogleCopyright ?? PlateauApi.GetGoogleCopyrightAsync)(url, token);
                if (string.IsNullOrWhiteSpace(copyright)) throw new InvalidOperationException("Viewport response omitted copyright.");
                if (UseGoogle && generation == attributionGeneration && task == googleSessionTask && !token.IsCancellationRequested)
                {
                    SetMapAttribution(copyright);
                    attributionReady = true;
                    tiles.style.visibility = Visibility.Visible;
                    ShowMapMessage(null);
                }
            }
            catch (Exception)
            {
                if (UseGoogle && generation == attributionGeneration && task == googleSessionTask && !token.IsCancellationRequested)
                {
                    SetMapAttribution("");
                    tiles.style.visibility = Visibility.Hidden;
                    ShowMapMessage("Google Maps の著作権情報を取得できませんでした。地図を非表示にして再試行します。");
                    retry = true;
                }
            }
            finally
            {
                attributionRequestPending = false;
                if (UseGoogle && map != null && map.panel != null && !attributionReady)
                {
                    if (retry && generation == attributionGeneration)
                        QueueAttributionUpdate(2000);
                    else ScheduleAttributionUpdate();
                }
            }
        }

        private void ShowFallbackTile(VisualElement visual, int tileZoom, int tileX, int tileY, double tileSize)
        {
            for (var depth = 1; tileZoom - depth >= 5; depth++)
            {
                var factor = 1 << depth;
                var key = (tileZoom - depth) + "/" + (tileX / factor) + "/" + (tileY / factor);
                if (!tileTextures.TryGetValue(key, out var parent)) continue;
                visual.style.overflow = Overflow.Hidden;
                var image = new VisualElement { pickingMode = PickingMode.Ignore };
                image.style.position = Position.Absolute;
                image.style.left = (float)(-(tileX % factor) * tileSize);
                image.style.top = (float)(-(tileY % factor) * tileSize);
                image.style.width = (float)(tileSize * factor);
                image.style.height = (float)(tileSize * factor);
                image.style.backgroundImage = new StyleBackground(parent);
                visual.Add(image);
                return;
            }

            var descendants = new List<(int zoom, int x, int y, Texture2D texture)>();
            foreach (var entry in tileTextures)
            {
                var parts = entry.Key.Split('/');
                var childZoom = int.Parse(parts[0], CultureInfo.InvariantCulture);
                if (childZoom <= tileZoom) continue;
                var factor = 1 << (childZoom - tileZoom);
                var childX = int.Parse(parts[1], CultureInfo.InvariantCulture);
                var childY = int.Parse(parts[2], CultureInfo.InvariantCulture);
                if (childX / factor == tileX && childY / factor == tileY)
                    descendants.Add((childZoom, childX, childY, entry.Value));
            }
            foreach (var child in descendants.OrderBy(item => item.zoom))
            {
                var factor = 1 << (child.zoom - tileZoom);
                var childSize = tileSize / factor;
                var image = new VisualElement { pickingMode = PickingMode.Ignore };
                image.style.position = Position.Absolute;
                image.style.left = (float)((child.x % factor) * childSize);
                image.style.top = (float)((child.y % factor) * childSize);
                image.style.width = (float)childSize;
                image.style.height = (float)childSize;
                image.style.backgroundImage = new StyleBackground(child.texture);
                visual.Add(image);
            }
        }

        private void EnsureTileRequest(string key)
        {
            if (tileRequests.ContainsKey(key)) return;
            var request = new TileRequest();
            tileRequests.Add(key, request);
            _ = LoadTileAsync(key, request);
        }

        private async Task LoadTileAsync(string key, TileRequest request)
        {
            try
            {
                // Wait for the session before taking a slot so a slow session cannot block other tiles.
                var url = UseGoogle
                    ? GoogleMapTiles.TileUrl(key, (await EnsureGoogleSessionAsync()).session, GoogleApiKey)
                    : Q<TextField>("tiles-url").value.TrimEnd('/') + "/" + key + ".png";
                await tileDownloadSlots.WaitAsync(request.Cancellation.Token);
                byte[] bytes;
                try
                {
                    bytes = await FetchTile(url, request.Cancellation.Token);
                }
                finally { tileDownloadSlots.Release(); }
                if (request.Cancellation.IsCancellationRequested || !neededTileKeys.Contains(key)) return;
                var texture = new Texture2D(2, 2);
                if (!texture.LoadImage(bytes)) { DestroyImmediate(texture); return; }
                tileTextures[key] = texture;
                RefreshMap();
            }
            catch (OperationCanceledException) { }
            catch { /* Coordinates and catalog remain usable when map tiles are unavailable. */ }
            finally
            {
                if (tileRequests.TryGetValue(key, out var current) && ReferenceEquals(current, request))
                    tileRequests.Remove(key);
                request.Cancellation.Dispose();
            }
        }

        private void RefreshOverlay()
        {
            if (map == null || meshOverlay == null || boundsOverlay == null) return;
            meshOverlay.Clear();
            boundsOverlay.Clear();
            if (desired != null)
            {
                meshSummary.text = BuildMeshSummary(desired.selectedGmls);
                var coverage = desired.selectedGmls
                    .GroupBy(entry => JapanMeshCode.GetCatalogBounds(entry.code, entry.url))
                    .Select(group => (Bounds: group.Key, Detail: group.Max(entry => entry.code?.Length ?? 0)))
                    .ToArray();
                var finest = coverage.Length == 0 ? 0 : coverage.Max(item => item.Detail);
                var visible = coverage.OrderByDescending(item => item.Detail).Take(100)
                    .OrderBy(item => item.Detail).ThenByDescending(item => item.Bounds.North)
                    .ThenBy(item => item.Bounds.West).ToArray();
                var number = 0;
                foreach (var item in visible)
                {
                    var detailed = item.Detail == finest;
                    if (detailed)
                    {
                        DrawGeoRect(meshOverlay, item.Bounds, Color.clear, Color.white, 5);
                        DrawGeoRect(meshOverlay, item.Bounds, Color.clear, new Color(0.43f, 0.08f, 0.7f, 1f), 3);
                        var topLeft = GeoToScreen(item.Bounds.North, item.Bounds.West);
                        var badge = new Label((++number).ToString(CultureInfo.InvariantCulture))
                        {
                            pickingMode = PickingMode.Ignore
                        };
                        badge.AddToClassList("mesh-badge");
                        badge.style.left = topLeft.x + 4;
                        badge.style.top = topLeft.y + 4;
                        meshOverlay.Add(badge);
                    }
                    else
                    {
                        DrawGeoRect(meshOverlay, item.Bounds, Color.clear, new Color(0.75f, 0.65f, 0.85f, 0.9f));
                    }
                }
                mapLegend.text = "青: 指定範囲　紫の番号付き枠: 詳細区画　薄紫枠: 広域区画" +
                    (coverage.Length > visible.Length ? "（" + (coverage.Length - visible.Length) + "区画は地図上で省略）" : "");
            }
            else mapLegend.text = "青: 指定範囲　紫: 取得対象の区画（照会後に表示）";
            DrawGeoRect(boundsOverlay, bounds, new Color(0.1f, 0.55f, 1f, 0.12f),
                new Color(0.1f, 0.55f, 1f, 1f));
        }

        private void ShowScreenBounds(Vector2 a, Vector2 b)
        {
            boundsOverlay.Clear();
            DrawScreenRect(boundsOverlay, a, b, new Color(0.1f, 0.55f, 1f, 0.12f),
                new Color(0.1f, 0.55f, 1f, 1f));
        }

        private void DrawGeoRect(VisualElement parent, GeoBounds geo, Color fill, Color stroke, float width = 2)
        {
            var center = ToWorld(centerLatitude, centerLongitude);
            var nearCenter = centerLongitude + GeoBounds.NormalizeLongitude(geo.CenterLongitude - centerLongitude);
            var west = ToWorld(geo.North, nearCenter - geo.LongitudeSpan * 0.5);
            var east = ToWorld(geo.South, nearCenter + geo.LongitudeSpan * 0.5);
            var worldWidth = MapZoomMath.WorldScale(zoom);
            // Draw every world copy intersecting the viewport, keeping narrow crossing selections narrow.
            var firstCopy = (int)Math.Ceiling((center.x - map.contentRect.width / 2 - east.x) / worldWidth);
            var lastCopy = (int)Math.Floor((center.x + map.contentRect.width / 2 - west.x) / worldWidth);
            for (var copy = firstCopy; copy <= lastCopy; copy++)
                DrawScreenRect(parent,
                    new Vector2((float)(west.x + copy * worldWidth - center.x + map.contentRect.width / 2),
                        (float)(west.y - center.y + map.contentRect.height / 2)),
                    new Vector2((float)(east.x + copy * worldWidth - center.x + map.contentRect.width / 2),
                        (float)(east.y - center.y + map.contentRect.height / 2)), fill, stroke, width);
        }

        private static void DrawScreenRect(VisualElement parent, Vector2 a, Vector2 b, Color fill, Color stroke,
            float width = 2)
        {
            var rect = new VisualElement { pickingMode = PickingMode.Ignore };
            rect.style.position = Position.Absolute;
            rect.style.left = Mathf.Min(a.x, b.x);
            rect.style.top = Mathf.Min(a.y, b.y);
            rect.style.width = Mathf.Abs(a.x - b.x);
            rect.style.height = Mathf.Abs(a.y - b.y);
            rect.style.backgroundColor = fill;
            rect.style.borderTopWidth = rect.style.borderBottomWidth = width;
            rect.style.borderLeftWidth = rect.style.borderRightWidth = width;
            rect.style.borderTopColor = rect.style.borderBottomColor = stroke;
            rect.style.borderLeftColor = rect.style.borderRightColor = stroke;
            parent.Add(rect);
        }
    }
}
