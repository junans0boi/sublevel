using System;
using System.Collections;
using System.IO;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // Review shots of the main menu's multiplayer parts (#160): the new buttons, the join form, the refusals (version,
    // unreachable) and the lobby seen by the host and by a guest. Not part of the suite: run on purpose with
    //   LOBBY_SHOTS=<folder> tools/unity.sh test playmode Game.Tests.LobbyCaptureTests
    [Explicit, Category("Capture")]
    public class LobbyCaptureTests
    {
        readonly System.Collections.Generic.List<LobbyNet> _rooms = new System.Collections.Generic.List<LobbyNet>();

        static string Folder()
        {
            string folder = Environment.GetEnvironmentVariable("LOBBY_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = "Logs/lobbyshots";
            folder = Path.GetFullPath(folder);
            Directory.CreateDirectory(folder);
            return folder;
        }

        LobbyNet NewRoom()
        {
            var room = new GameObject("CaptureLobby" + _rooms.Count).AddComponent<LobbyNet>();
            _rooms.Add(room);
            return room;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var r in _rooms) if (r != null) { r.Shutdown(); UnityEngine.Object.Destroy(r.gameObject); }
            _rooms.Clear();
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        static IEnumerator Menu()
        {
            SceneManager.LoadScene(MainMenu.SceneName);
            yield return NetHarness.WaitUntil(() => MainMenu.Instance != null, "main menu", null, 20f);
            yield return null;
            // The menu camera draws nothing itself (culling mask 0); the shot's copy must still draw the UI layer.
            Camera.main.cullingMask = -1;
        }

        static IEnumerator Settle()
        {
            float end = Time.realtimeSinceStartup + 0.4f;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator CaptureJoinFlow()
        {
            string folder = Folder();
            yield return Menu();
            yield return UiShot.Save(Path.Combine(folder, "01_main.png"), 1920, 1080);

            var menu = MainMenu.Instance;
            menu.OpenJoin();
            yield return Settle();
            yield return UiShot.Save(Path.Combine(folder, "02_join_form.png"), 1920, 1080);

            // Nobody there: the address hint.
            menu.Lobby.SetAddress("127.0.0.1:17971");
            menu.Lobby.Join();
            yield return NetHarness.WaitUntil(() => menu.Lobby.View == LobbyScreen.ViewKind.Failed, "unreachable shown", null, 20f);
            yield return Settle();
            yield return UiShot.Save(Path.Combine(folder, "03_failed_unreachable.png"), 1920, 1080);

            // A host of another version.
            var host = NewRoom();
            host.Version = "0.0.0-other/1";
            Assert.IsTrue(host.StartHost(17972));
            menu.Lobby.OpenJoin();
            menu.Lobby.SetAddress("127.0.0.1:17972");
            menu.Lobby.Join();
            yield return NetHarness.WaitUntil(() => menu.Lobby.View == LobbyScreen.ViewKind.Failed && menu.Lobby.FailureText.Contains("버전"), "version refusal shown", null, 20f);
            yield return Settle();
            yield return UiShot.Save(Path.Combine(folder, "04_failed_version.png"), 1920, 1080);
            host.Shutdown();

            // A full room.
            MatchRules.Current.MaxPlayers = 2;
            var full = NewRoom();
            Assert.IsTrue(full.StartHost(17973));
            var filler = NewRoom();
            filler.Join("127.0.0.1", 17973);
            yield return NetHarness.WaitUntil(() => full.Roster.Count == 2, "room is full", null, 20f);
            menu.Lobby.OpenJoin();
            menu.Lobby.SetAddress("127.0.0.1:17973");
            menu.Lobby.Join();
            yield return NetHarness.WaitUntil(() => menu.Lobby.View == LobbyScreen.ViewKind.Failed && menu.Lobby.FailureText.Contains("가득"), "full refusal shown", null, 20f);
            yield return Settle();
            yield return UiShot.Save(Path.Combine(folder, "05_failed_full.png"), 1920, 1080);
            full.Shutdown();
            MatchRules.Current.MaxPlayers = 16;

            // A guest in somebody else's room with two other people, one ready.
            var room = NewRoom();
            Assert.IsTrue(room.StartHost(17974));
            var a = NewRoom(); a.Join("127.0.0.1", 17974);
            var b = NewRoom(); b.Join("127.0.0.1", 17974);
            yield return NetHarness.WaitUntil(() => a.Phase == LobbyNet.PhaseKind.InLobby && b.Phase == LobbyNet.PhaseKind.InLobby, "two guests in", null, 20f);
            a.SetReady(true);
            menu.Lobby.OpenJoin();
            menu.Lobby.SetAddress("127.0.0.1:17974");
            menu.Lobby.Join();
            yield return NetHarness.WaitUntil(() => menu.Lobby.View == LobbyScreen.ViewKind.Room && room.Roster.Count == 3, "menu guest in the room", null, 20f);
            yield return Settle();
            yield return UiShot.Save(Path.Combine(folder, "06_guest_room.png"), 1920, 1080);
            menu.Lobby.Lobby.SetReady(true);
            yield return Settle();
            yield return UiShot.Save(Path.Combine(folder, "07_guest_room_ready.png"), 1920, 1080);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator CaptureHostRoom()
        {
            string folder = Folder();
            yield return Menu();
            var menu = MainMenu.Instance;
            menu.OpenHost();
            yield return NetHarness.WaitUntil(() => menu.Lobby.View == LobbyScreen.ViewKind.Room, "host room shown", null, 20f);
            yield return Settle();
            yield return UiShot.Save(Path.Combine(folder, "10_host_alone.png"), 1920, 1080);

            var hostRoom = menu.Lobby.Lobby;
            var a = NewRoom(); a.Join("127.0.0.1", hostRoom.Port);
            var b = NewRoom(); b.Join("127.0.0.1", hostRoom.Port);
            yield return NetHarness.WaitUntil(() => hostRoom.Roster.Count == 3 && a.Phase == LobbyNet.PhaseKind.InLobby && b.Phase == LobbyNet.PhaseKind.InLobby, "two guests", null, 20f);
            a.SetReady(true);
            yield return NetHarness.WaitUntil(() => hostRoom.Roster.IsReady(a.SelfId), "first guest ready", null, 20f);
            yield return Settle();
            yield return UiShot.Save(Path.Combine(folder, "11_host_waiting.png"), 1920, 1080);
            b.SetReady(true);
            hostRoom.SetBots(6);
            yield return NetHarness.WaitUntil(() => hostRoom.CanStart && hostRoom.Bots == 6, "all ready", null, 20f);
            yield return Settle();
            yield return UiShot.Save(Path.Combine(folder, "12_host_can_start.png"), 1920, 1080);
        }
    }
}
