using System.Linq;
using System.Reflection;
using Game.Core;
using Game.Net;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public class MatchDirectorPartyLayoutTests
    {
        GameObject _directorObject;
        GameObject _playerPrefabObject;
        GameObject _lobbyObject;

        [TearDown]
        public void TearDown()
        {
            if (_directorObject != null) Object.DestroyImmediate(_directorObject);
            if (_playerPrefabObject != null) Object.DestroyImmediate(_playerPrefabObject);
            if (_lobbyObject != null) Object.DestroyImmediate(_lobbyObject);
            NetSession.Reset();
            TeamMatch.Mode = TeamMode.Solo;
        }

        [Test]
        public void ServerDeployment_UsesQueuePartyBoundaries()
        {
            var plan = MatchComposer.Compose(TeamMode.Squad,
                new[] { new QueueParty(1, 1), new QueueParty(2, 3), new QueueParty(3, 2) },
                new MatchRules(), new TeamRules());
            var start = LobbyStart.FromPlan(7778, TeamMode.Squad, plan);
            Assert.AreEqual(6, start.Players);
            Assert.AreEqual(74, start.Bots);
            Assert.AreEqual((byte)TeamMode.Squad, start.Mode);
            CollectionAssert.AreEqual(new[] { 1, 3, 2 }, start.PartySizes);

            _lobbyObject = new GameObject("PartyLayoutTestLobby");
            var lobby = _lobbyObject.AddComponent<LobbyNet>();
            typeof(LobbyNet).GetProperty("StartInfo").SetValue(lobby, start, null);
            typeof(LobbyNet).GetProperty("IsHost").SetValue(lobby, true, null);
            NetSession.Reset();
            NetSession.Begin(lobby);
            TeamMatch.Mode = TeamMode.Squad;

            CollectionAssert.AreEqual(new[] { 1, 3, 2 }, NetSession.PartySizes);

            _playerPrefabObject = new GameObject("PartyLayoutTestPlayerPrefab");
            var networkObjectType = System.AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("FishNet.Object.NetworkObject", false))
                .FirstOrDefault(t => t != null);
            Assert.IsNotNull(networkObjectType, "FishNet NetworkObject type must be available to the match scene.");
            var playerPrefab = _playerPrefabObject.AddComponent(networkObjectType);
            _directorObject = new GameObject("PartyLayoutTestMatchDirector");
            var director = _directorObject.AddComponent<MatchDirector>();
            typeof(MatchDirector).GetField("_playerPrefab", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(director, playerPrefab);

            typeof(MatchDirector).GetMethod("ServerPlanDeployment", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(director, null);

            var sizes = Enumerable.Range(1, director.Layout.TeamCount).Select(director.Layout.SizeOfTeam).Take(3).ToArray();
            CollectionAssert.AreEqual(new[] { 1, 3, 2 }, sizes);
            Assert.AreEqual(6, director.Layout.Participants);

            NetSession.Reset();
            NetSession.Role = NetSession.RoleKind.Host;
            NetSession.Players = 6;
            typeof(MatchDirector).GetMethod("ServerPlanDeployment", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(director, null);
            CollectionAssert.AreEqual(new[] { 4, 2 }, Enumerable.Range(1, director.Layout.TeamCount).Select(director.Layout.SizeOfTeam));
        }
    }
}
