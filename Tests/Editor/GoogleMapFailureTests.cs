using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Zabaglione.PlateauAreaDownloader.Editor.Tests
{
    public class GoogleMapAttributionFailureTests
    {
        private const string Prefs = "Zabaglione.PlateauAreaDownloader.";
        private AreaDownloaderWindow window;
        private string provider, mapType;
        private byte[] png;
        private readonly List<TaskCompletionSource<string>> pending = new List<TaskCompletionSource<string>>();
        private int requests;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            provider = EditorPrefs.GetString(Prefs + "mapProvider", "gsi");
            mapType = EditorPrefs.GetString(Prefs + "googleMapType", "roadmap");
            var texture = new Texture2D(2, 2);
            png = texture.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(texture);
            window = ScriptableObject.CreateInstance<AreaDownloaderWindow>();
            window.CreateGoogleSession = (type, key, token) => Task.FromResult(new GoogleTileSession
                { session = "validation-session", expiry = "4102444800" });
            window.FetchTile = (url, token) => Task.FromResult(png);
            window.FetchGoogleCopyright = (url, token) => Task.FromResult("Validation copyright");
            window.Show();
            yield return null;
            yield return null;
            Provider.value = "Google Maps";
            window.rootVisualElement.Q<TextField>("google-api-key").SetValueWithoutNotify("validation-key");
            Provider.value = "地理院タイル";
            yield return new WaitForSecondsRealtime(0.5f);
            requests = 0;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            window.Close();
            foreach (var task in pending) task.TrySetCanceled();
            pending.Clear();
            yield return null;
            EditorPrefs.SetString(Prefs + "mapProvider", provider);
            EditorPrefs.SetString(Prefs + "googleMapType", mapType);
        }

        private DropdownField Provider => window.rootVisualElement.Q<DropdownField>("map-provider");
        private VisualElement Tiles => window.rootVisualElement.Q<VisualElement>("tiles");
        private Label Attribution => window.rootVisualElement.Q<Label>("map-attribution");

        [UnityTest]
        public IEnumerator FailedCopyright_HidesTilesAndAutomaticallyRetries()
        {
            window.FetchGoogleCopyright = (url, token) => ++requests == 1
                ? Task.FromException<string>(new HttpRequestException("Validation HTTP 503"))
                : Task.FromResult("Recovered copyright");
            Provider.value = "Google Maps";
            yield return new WaitForSecondsRealtime(1);
            Assert.That(Tiles.style.visibility.value, Is.EqualTo(Visibility.Hidden));
            Assert.That(Attribution.text, Is.Empty);
            yield return new WaitForSecondsRealtime(2.2f);
            Assert.That(requests, Is.EqualTo(2));
            Assert.That(Attribution.text, Is.EqualTo("Recovered copyright"));
            Assert.That(Tiles.style.visibility.value, Is.EqualTo(Visibility.Visible));
        }

        [UnityTest]
        public IEnumerator WhitespaceCopyright_IsNotAcceptedAndAutomaticallyRetries()
        {
            window.FetchGoogleCopyright = (url, token) => ++requests == 1
                ? Task.FromResult(" \n\t") : Task.FromResult("Recovered copyright");
            Provider.value = "Google Maps";
            yield return new WaitForSecondsRealtime(1);
            Assert.That(Tiles.style.visibility.value, Is.EqualTo(Visibility.Hidden));
            Assert.That(Attribution.text, Is.Empty);
            yield return new WaitForSecondsRealtime(2.2f);
            Assert.That(Attribution.text, Is.EqualTo("Recovered copyright"));
            Assert.That(Tiles.style.visibility.value, Is.EqualTo(Visibility.Visible));
        }

        [UnityTest]
        public IEnumerator OldCopyright_AfterGsiSwitchCannotRestoreGoogleAttribution()
        {
            var response = new TaskCompletionSource<string>();
            pending.Add(response);
            CancellationToken requestToken = default;
            window.FetchGoogleCopyright = (url, token) => { requests++; requestToken = token; return response.Task; };
            Provider.value = "Google Maps";
            yield return new WaitForSecondsRealtime(1);
            Assert.That(requests, Is.EqualTo(1));
            Provider.value = "地理院タイル";
            Assert.That(requestToken.IsCancellationRequested, Is.True);
            response.SetResult("Stale Google copyright");
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.That(Attribution.text, Is.EqualTo("地理院タイル（国土地理院）"));
            Assert.That(Tiles.style.visibility.value, Is.EqualTo(Visibility.Visible));
        }

        [UnityTest]
        public IEnumerator ChangedViewport_RejectsOldCopyrightAndFetchesCurrent()
        {
            var old = new TaskCompletionSource<string>();
            pending.Add(old);
            window.FetchGoogleCopyright = (url, token) => ++requests == 1
                ? old.Task : Task.FromResult("Current viewport copyright");
            Provider.value = "Google Maps";
            yield return new WaitForSecondsRealtime(1);
            var position = window.position;
            window.position = new Rect(position.x, position.y, position.width + 80, position.height);
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.That(Tiles.style.visibility.value, Is.EqualTo(Visibility.Hidden));
            old.SetResult("Stale viewport copyright");
            yield return new WaitForSecondsRealtime(1.2f);
            Assert.That(requests, Is.EqualTo(2));
            Assert.That(Attribution.text, Is.EqualTo("Current viewport copyright"));
            Assert.That(Tiles.style.visibility.value, Is.EqualTo(Visibility.Visible));
        }

        [UnityTest]
        public IEnumerator LateTiles_DoNotInvalidateAcceptedCopyright()
        {
            var bytes = new TaskCompletionSource<byte[]>();
            window.FetchTile = (url, token) => bytes.Task;
            window.FetchGoogleCopyright = (url, token) => { requests++; return Task.FromResult("Accepted copyright"); };
            Provider.value = "Google Maps";
            yield return new WaitForSecondsRealtime(1);
            Assert.That(requests, Is.EqualTo(1));
            Assert.That(Tiles.style.visibility.value, Is.EqualTo(Visibility.Visible));
            bytes.SetResult(png);
            yield return new WaitForSecondsRealtime(1);
            Assert.That(requests, Is.EqualTo(1));
            Assert.That(Attribution.text, Is.EqualTo("Accepted copyright"));
            Assert.That(Tiles.style.visibility.value, Is.EqualTo(Visibility.Visible));
        }

        [UnityTest]
        public IEnumerator LateTiles_DoNotAccelerateScheduledCopyrightRetry()
        {
            var bytes = new TaskCompletionSource<byte[]>();
            window.FetchTile = (url, token) => bytes.Task;
            window.FetchGoogleCopyright = (url, token) => ++requests == 1
                ? Task.FromException<string>(new HttpRequestException("Validation HTTP 503"))
                : Task.FromResult("Recovered copyright");
            Provider.value = "Google Maps";
            yield return new WaitForSecondsRealtime(1);
            Assert.That(requests, Is.EqualTo(1));
            bytes.SetResult(png);
            yield return new WaitForSecondsRealtime(0.9f);
            Assert.That(requests, Is.EqualTo(1), "Tile completion shortened the retry delay");
            yield return new WaitForSecondsRealtime(1.1f);
            Assert.That(requests, Is.EqualTo(2));
            Assert.That(Attribution.text, Is.EqualTo("Recovered copyright"));
        }

        [UnityTest]
        public IEnumerator RebuiltGui_DoesNotReuseReadyFlagWithEmptyCopyright()
        {
            window.FetchGoogleCopyright = (url, token) => { requests++; return Task.FromResult("Accepted copyright"); };
            Provider.value = "Google Maps";
            yield return new WaitForSecondsRealtime(1);
            Assert.That(Attribution.text, Is.EqualTo("Accepted copyright"));
            window.CreateGUI();
            Assert.That(Tiles.style.visibility.value, Is.EqualTo(Visibility.Hidden));
            Assert.That(Attribution.text, Is.Empty);
            yield return new WaitForSecondsRealtime(1);
            Assert.That(requests, Is.EqualTo(2));
            Assert.That(Attribution.text, Is.EqualTo("Accepted copyright"));
            Assert.That(Tiles.style.visibility.value, Is.EqualTo(Visibility.Visible));
        }
    }

    public class TileBodyFailureTests
    {
        [UnityTest]
        public IEnumerator GoogleBodyStop_SwitchToGsiReleasesDownloadSlots()
        {
            const string prefs = "Zabaglione.PlateauAreaDownloader.";
            var savedProvider = EditorPrefs.GetString(prefs + "mapProvider", "gsi");
            var texture = new Texture2D(2, 2);
            var handler = new SwitchHandler(texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            using var client = new HttpClient(handler);
            var window = ScriptableObject.CreateInstance<AreaDownloaderWindow>();
            window.CreateGoogleSession = (type, key, token) => Task.FromResult(new GoogleTileSession
                { session = "validation-session", expiry = "4102444800" });
            window.FetchGoogleCopyright = (url, token) => Task.FromResult("Validation copyright");
            window.FetchTile = (url, token) => PlateauApi.GetTileAsync(url, token, client);
            try
            {
                window.Show();
                yield return null;
                yield return null;
                var provider = window.rootVisualElement.Q<DropdownField>("map-provider");
                provider.value = "Google Maps";
                window.rootVisualElement.Q<TextField>("google-api-key").SetValueWithoutNotify("validation-key");
                provider.value = "地理院タイル";
                yield return new WaitForSecondsRealtime(0.5f);
                handler.GoogleStreams.Clear();
                handler.GsiRequests = 0;
                provider.value = "Google Maps";
                yield return new WaitForSecondsRealtime(1);
                Assert.That(handler.GoogleStreams.Count, Is.GreaterThanOrEqualTo(4));
                provider.value = "地理院タイル";
                yield return new WaitForSecondsRealtime(1);
                Assert.That(handler.GsiRequests, Is.GreaterThan(0));
                Assert.That(handler.GoogleStreams.TrueForAll(stream => stream.Disposed), Is.True);
            }
            finally
            {
                window.Close();
                EditorPrefs.SetString(prefs + "mapProvider", savedProvider);
            }
        }

        [Test]
        public async Task HeaderSuccess_BodyStopCanBeCanceledAndClientRemainsUsable()
        {
            var stream = new StoppedStream();
            using var client = new HttpClient(new ResponseHandler(stream));
            using var cancellation = new CancellationTokenSource();
            var task = PlateauApi.GetTileAsync("https://validation.invalid/tile", cancellation.Token, client);
            cancellation.Cancel();
            Assert.That(await Task.WhenAny(task, Task.Delay(2000)), Is.SameAs(task));
            Assert.That(task.IsCanceled, Is.True);
            Assert.That(stream.Disposed, Is.True);
            var next = await PlateauApi.GetTileAsync("https://validation.invalid/tile", CancellationToken.None, client);
            Assert.That(next, Is.EqualTo(new byte[] { 1, 2, 3 }));
        }

        [Test]
        public async Task HeaderSuccess_BodyStopTimesOutAfterFifteenSeconds()
        {
            var stream = new StoppedStream();
            using var client = new HttpClient(new ResponseHandler(stream));
            var watch = Stopwatch.StartNew();
            var task = PlateauApi.GetTileAsync("https://validation.invalid/tile", CancellationToken.None, client);
            Assert.That(await Task.WhenAny(task, Task.Delay(18000)), Is.SameAs(task));
            Assert.That(task.IsFaulted, Is.True);
            Assert.That(task.Exception.InnerException, Is.TypeOf<TimeoutException>());
            Assert.That(watch.Elapsed.TotalSeconds, Is.InRange(14.5, 18));
            Assert.That(stream.Disposed, Is.True);
        }

        private sealed class ResponseHandler : HttpMessageHandler
        {
            private readonly Stream stopped;
            private int count;
            internal ResponseHandler(Stream stopped) { this.stopped = stopped; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
                => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StreamContent(count++ == 0 ? stopped : new MemoryStream(new byte[] { 1, 2, 3 })) });
        }

        private sealed class SwitchHandler : HttpMessageHandler
        {
            internal readonly List<StoppedStream> GoogleStreams = new List<StoppedStream>();
            internal int GsiRequests;
            private readonly byte[] png;
            internal SwitchHandler(byte[] png) { this.png = png; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                Stream stream;
                if (request.RequestUri.Host == "tile.googleapis.com")
                {
                    var stopped = new StoppedStream();
                    GoogleStreams.Add(stopped);
                    stream = stopped;
                }
                else
                {
                    GsiRequests++;
                    stream = new MemoryStream(png);
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) });
            }
        }

        private sealed class StoppedStream : Stream
        {
            private readonly TaskCompletionSource<int> read = new TaskCompletionSource<int>();
            internal bool Disposed;
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
                => read.Task; // Deliberately ignores token; disposing the response must interrupt the read.
            protected override void Dispose(bool disposing)
            {
                Disposed = true;
                read.TrySetException(new ObjectDisposedException("Validation stream"));
                base.Dispose(disposing);
            }
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override void Flush() => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
