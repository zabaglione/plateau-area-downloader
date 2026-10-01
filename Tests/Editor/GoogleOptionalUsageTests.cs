using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
namespace Zabaglione.PlateauAreaDownloader.Editor.Tests
{
    public class GoogleOptionalUsageTests
    {
        private const string Prefs = "Zabaglione.PlateauAreaDownloader.";
        private string savedProvider, savedType;
        private AreaDownloaderWindow window;
        private int sessions, copyrights, googleTiles, gsiTiles;
        private CancellationToken sessionToken;
        private TaskCompletionSource<GoogleTileSession> pendingSession;

        [UnitySetUp]
        public IEnumerator Setup()
        {
            savedProvider = EditorPrefs.GetString(Prefs + "mapProvider", "gsi");
            savedType = EditorPrefs.GetString(Prefs + "googleMapType", "roadmap");
            EditorPrefs.SetString(Prefs + "mapProvider", "gsi");
            sessions = copyrights = googleTiles = gsiTiles = 0;
            var texture = new Texture2D(2, 2);
            var png = texture.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(texture);
            window = ScriptableObject.CreateInstance<AreaDownloaderWindow>();
            window.CreateGoogleSession = (type, key, token) =>
            {
                sessions++;
                sessionToken = token;
                return pendingSession == null ? Task.FromResult(new GoogleTileSession
                    { session = "validation-session", expiry = "4102444800" }) : pendingSession.Task;
            };
            window.FetchGoogleCopyright = (url, token) => { copyrights++; return Task.FromResult("Validation copyright"); };
            window.FetchTile = (url, token) =>
            {
                if (url.StartsWith(GoogleMapTiles.Base, StringComparison.Ordinal)) googleTiles++;
                else gsiTiles++;
                return Task.FromResult(png);
            };
            window.Show();
            yield return new WaitForSecondsRealtime(0.5f);
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            window.Close();
            pendingSession?.TrySetCanceled();
            pendingSession = null;
            EditorPrefs.SetString(Prefs + "mapProvider", savedProvider);
            EditorPrefs.SetString(Prefs + "googleMapType", savedType);
            yield return null;
        }

        private DropdownField Provider => window.rootVisualElement.Q<DropdownField>("map-provider");
        private TextField Key => window.rootVisualElement.Q<TextField>("google-api-key");
        private VisualElement Logo => window.rootVisualElement.Q<VisualElement>("google-logo");

        [UnityTest]
        public IEnumerator GsiOnly_DoesNotRequestGoogleOrLoadGoogleControls()
        {
            window.position = new Rect(30, 60, 560, 420);
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.That(gsiTiles, Is.GreaterThan(0));
            Assert.That(sessions + copyrights + googleTiles, Is.Zero);
            Assert.That(string.IsNullOrEmpty(Key.value), Is.True, "GSI loaded Google key settings");
            Assert.That(Key.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(window.rootVisualElement.Q<DropdownField>("google-map-type").choices, Is.Empty);
            Assert.That(Logo.style.backgroundImage.value.texture, Is.Null);
            Assert.That(window.rootVisualElement.Q<Label>("map-message").text, Is.Empty);
        }

        [UnityTest]
        public IEnumerator SwitchToGoogle_CreatesOnDemandAndGsiCancelsPendingSession()
        {
            Provider.value = "Google Maps";
            Key.SetValueWithoutNotify("validation-key");
            Provider.value = "地理院タイル";
            pendingSession = new TaskCompletionSource<GoogleTileSession>();
            var before = sessions;
            Provider.value = "Google Maps";
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.That(sessions, Is.EqualTo(before + 1));
            Assert.That(Key.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(window.rootVisualElement.Q<DropdownField>("google-map-type").choices.Count, Is.EqualTo(3));
            Assert.That(Logo.style.backgroundImage.value.texture, Is.Not.Null);
            Assert.That(sessionToken.IsCancellationRequested, Is.False);
            Provider.value = "地理院タイル";
            var onGsi = sessions;
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.That(sessionToken.IsCancellationRequested, Is.True);
            Assert.That(sessions, Is.EqualTo(onGsi));
            Assert.That(Key.style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(Logo.style.backgroundImage.value.texture, Is.Null);
            pendingSession.TrySetCanceled();
            pendingSession = null;
            Provider.value = "Google Maps";
            yield return new WaitForSecondsRealtime(1);
            Assert.That(sessions, Is.EqualTo(onGsi + 1));
            Assert.That(googleTiles, Is.GreaterThan(0));
            Assert.That(copyrights, Is.GreaterThan(0));
        }
        [UnityTest]
        public IEnumerator OldSessionSuccess_DoesNotClearCurrentSessionError()
        {
            Provider.value = "Google Maps";
            Key.SetValueWithoutNotify("validation-key");
            Provider.value = "地理院タイル";
            pendingSession = new TaskCompletionSource<GoogleTileSession>();
            Provider.value = "Google Maps";
            yield return new WaitForSecondsRealtime(0.5f);
            var old = pendingSession;
            Provider.value = "地理院タイル";
            pendingSession = null;
            window.CreateGoogleSession = (type, key, token) =>
                Task.FromException<GoogleTileSession>(new InvalidOperationException("Current validation failure"));
            Provider.value = "Google Maps";
            yield return new WaitForSecondsRealtime(0.5f);
            var message = window.rootVisualElement.Q<Label>("map-message");
            Assert.That(message.text.Contains("Current validation failure"), Is.True);
            old.SetResult(new GoogleTileSession { session = "stale-session", expiry = "4102444800" });
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.That(message.text.Contains("Current validation failure"), Is.True);
        }

        [UnityTest]
        public IEnumerator OldSessionFailure_DoesNotPostponeCurrentRetry()
        {
            Provider.value = "Google Maps";
            Key.SetValueWithoutNotify("validation-key");
            Provider.value = "地理院タイル";
            pendingSession = new TaskCompletionSource<GoogleTileSession>();
            Provider.value = "Google Maps";
            yield return new WaitForSecondsRealtime(0.5f);
            var old = pendingSession;
            Provider.value = "地理院タイル";
            pendingSession = null;
            var attempts = 0;
            window.CreateGoogleSession = (type, key, token) =>
            {
                attempts++;
                return attempts == 1
                    ? Task.FromException<GoogleTileSession>(new InvalidOperationException("Current validation failure"))
                    : Task.FromResult(new GoogleTileSession { session = "current-session", expiry = "4102444800" });
            };
            Provider.value = "Google Maps";
            yield return new WaitForSecondsRealtime(10.5f);
            Assert.That(attempts, Is.EqualTo(1));
            old.SetException(new InvalidOperationException("Stale validation failure"));
            yield return new WaitForSecondsRealtime(0.2f);
            var bounds = window.position;
            window.position = new Rect(bounds.x, bounds.y, bounds.width + 80, bounds.height);
            yield return new WaitForSecondsRealtime(1);
            Assert.That(attempts, Is.EqualTo(2), "Stale failure postponed the current session retry");
            Assert.That(window.rootVisualElement.Q<Label>("map-message").text, Is.Empty);
        }
    }
}
