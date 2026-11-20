using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Game.Core;
using Game.Gameplay;
using Game.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Game.Tests
{
    // Review shots of the friends/party drawer and the backend queue line on the REAL main menu (#389/#391), logged in through a
    // running backend. Not part of the suite: player A's launcher token comes the launcher's way (SUBLEVEL_GAME_TOKEN + SUBLEVEL_API_BASE,
    // read by LoginEnv), player B's from SUBLEVEL_E2E_GAME_B; both accounts start without friends or party. Tokens are never logged.
    //   FRIENDS_SHOTS=<folder> tools/unity.sh test playmode Game.Tests.FriendsCaptureTests
    [Explicit, Category("Capture")]
    public class FriendsCaptureTests
    {
        static string Folder()
        {
            string folder = Environment.GetEnvironmentVariable("FRIENDS_SHOTS");
            if (string.IsNullOrEmpty(folder)) folder = "Logs/friendshots";
            folder = Path.GetFullPath(folder);
            Directory.CreateDirectory(folder);
            return folder;
        }

        static IEnumerator Await<T>(Task<T> t)
        {
            float end = Time.realtimeSinceStartup + 10f;
            while (!t.IsCompleted && Time.realtimeSinceStartup < end) yield return null;
            Assert.IsTrue(t.IsCompleted, "backend call timed out");
        }

        static IEnumerator Settle(float s = 0.5f)
        {
            float end = Time.realtimeSinceStartup + s;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        static T Field<T>(object o, string name) => (T)o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(o);

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return null;
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator CaptureFriendsDrawerAndQueue()
        {
            string api = Environment.GetEnvironmentVariable("SUBLEVEL_API_BASE"), tb = Environment.GetEnvironmentVariable("SUBLEVEL_E2E_GAME_B");
            if (LobbyNet.Backend == null || string.IsNullOrEmpty(api) || string.IsNullOrEmpty(tb)) Assert.Ignore("no launcher login (SUBLEVEL_GAME_TOKEN, SUBLEVEL_API_BASE, SUBLEVEL_E2E_GAME_B)");
            string folder = Folder();
            var b = new HttpBackend(api, tb);
            var meA = LobbyNet.Backend.Me(); var meB = b.Me();
            yield return Await(meA); yield return Await(meB);
            long idA = meA.Result.Value.id; string nickA = meA.Result.Value.nickname;
            yield return NetHarness.WaitUntil(() => LobbyNet.AccountId == idA, "LoginEnv read /api/me", null, 10f);

            SceneManager.LoadScene(MainMenu.SceneName);
            yield return NetHarness.WaitUntil(() => MainMenu.Instance != null, "main menu", null, 20f);
            var menu = MainMenu.Instance;
            yield return NetHarness.WaitUntil(() => Field<FriendsPanel>(menu, "_friends") != null, "friends drawer built", null, 10f);
            var drawer = Field<FriendsPanel>(menu, "_friends");
            var social = menu.Social;
            Camera.main.cullingMask = -1;
            yield return Settle(1.5f);
            yield return UiShot.Save(Path.Combine(folder, "01_menu_logged_in.png"), 1920, 1080);

            drawer.Toggle(); // what the bell does
            yield return Settle(1f);
            yield return UiShot.Save(Path.Combine(folder, "02_drawer_empty.png"), 1920, 1080);

            var req = b.AddFriend(nickA); yield return Await(req); Assert.IsTrue(req.Result.Ok, req.Result.Error);
            yield return NetHarness.WaitUntil(() => social.Friends.incoming.Length > 0, "request reaches A", null, 12f);
            yield return Settle();
            yield return UiShot.Save(Path.Combine(folder, "03_friend_request.png"), 1920, 1080);

            social.AcceptFriend(meB.Result.Value.id);
            var pb = b.Presence("lobby"); yield return Await(pb);
            yield return NetHarness.WaitUntil(() => social.Friends.friends.Length > 0 && social.Friends.friends[0].status == "lobby", "B listed in the lobby", null, 25f);
            yield return Settle();
            yield return UiShot.Save(Path.Combine(folder, "04_friend_online.png"), 1920, 1080);

            var inv = b.InviteToParty(idA); yield return Await(inv); Assert.IsTrue(inv.Result.Ok, inv.Result.Error);
            yield return NetHarness.WaitUntil(() => social.Invites.invites.Length > 0, "invite reaches A", null, 12f);
            yield return Settle();
            yield return UiShot.Save(Path.Combine(folder, "05_party_invite.png"), 1920, 1080);

            social.AcceptInvite(social.Invites.invites[0].party_id);
            yield return NetHarness.WaitUntil(() => social.Party != null && social.PartySize == 2, "A in B's party", null, 12f);
            yield return Settle();
            yield return UiShot.Save(Path.Combine(folder, "06_party_member.png"), 1920, 1080);

            social.AddFriend("NoSuchPlayer42");
            yield return NetHarness.WaitUntil(() => !string.IsNullOrEmpty(social.LastError), "error shown", null, 10f);
            yield return Settle();
            yield return UiShot.Save(Path.Combine(folder, "07_error_unknown_nick.png"), 1920, 1080);

            // The leader (B) starts matching: A, a member, adopts the party's ticket and sees the same line.
            drawer.Show(false);
            var queue = Field<IMatchQueue>(menu, "_backendQueue");
            var ticket = b.CreateTicket("battle-royale"); yield return Await(ticket); Assert.IsTrue(ticket.Result.Ok, ticket.Result.Error);
            yield return NetHarness.WaitUntil(() => queue.Note != null && queue.Note.Contains("인원"), "member adopts the ticket", null, 12f);
            yield return Settle(1f);
            yield return UiShot.Save(Path.Combine(folder, "08_member_waiting.png"), 1920, 1080);
            var cancel = b.CancelTicket(ticket.Result.Value.id); yield return Await(cancel);
            yield return NetHarness.WaitUntil(() => !queue.Waiting, "ticket gone", null, 12f);

            // Alone again: A's own start goes through the panel's button.
            social.LeaveParty();
            yield return NetHarness.WaitUntil(() => social.Party == null || social.PartySize <= 1, "A left", null, 12f);
            yield return Settle(1f);
            menu.Panel.PressStatus();
            yield return NetHarness.WaitUntil(() => queue.Note != null && queue.Note.Contains("인원"), "queue line", null, 12f);
            yield return Settle(1f);
            yield return UiShot.Save(Path.Combine(folder, "09_solo_queue.png"), 1920, 1080);
            menu.Panel.PressStatus(); // 취소
        }
    }
}
