using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    public class QueuePartiesTests
    {
        [Test]
        public void PartyMembers_ShareOneTicket_AndAllAreExpanded()
        {
            var q = new QueueParties();
            Assert.AreEqual(QueueParties.AddResult.NewTicket, q.Add(5, 12, 4));
            Assert.AreEqual(QueueParties.AddResult.JoinedTicket, q.Add(8, 12, 4));
            Assert.AreEqual(QueueParties.AddResult.NewTicket, q.Add(9, 0, 1), "no party: its own ticket");
            Assert.AreEqual(QueueParties.AddResult.NewTicket, q.Add(10, 0, 1));
            CollectionAssert.AreEqual(new[] { 5, 8 }, q.Members(5));
            Assert.AreEqual(QueueParties.AddResult.Duplicate, q.Add(8, 12, 4));
        }

        [Test]
        public void TicketStays_UntilTheLastMemberLeaves()
        {
            var q = new QueueParties();
            q.Add(5, 12, 4); q.Add(8, 12, 4);
            Assert.IsFalse(q.Remove(5, out _), "one member left, the party's ticket remains");
            CollectionAssert.AreEqual(new[] { 8 }, q.Members(5));
            Assert.IsTrue(q.Remove(8, out int ticket)); Assert.AreEqual(5, ticket);
            Assert.AreEqual(QueueParties.AddResult.NewTicket, q.Add(5, 12, 4), "a fresh ticket for the same party afterwards");
            Assert.IsFalse(q.Remove(99, out _));
        }

        [Test]
        public void PartyBeyondItsTicketSize_IsRefused()
        {
            var q = new QueueParties();
            Assert.AreEqual(QueueParties.AddResult.NewTicket, q.Add(5, 12, 2), "a duo ticket");
            Assert.AreEqual(QueueParties.AddResult.JoinedTicket, q.Add(8, 12, 2));
            Assert.AreEqual(QueueParties.AddResult.Refused, q.Add(9, 12, 4), "a third member would push the next team, whatever size it claims (#499)");
            CollectionAssert.AreEqual(new[] { 5, 8 }, q.Members(5));
            Assert.IsFalse(q.Has(9));
        }

        [Test]
        public void ReusedIdOfALeftLeader_DoesNotTakeOverThePartyTicket()
        {
            var q = new QueueParties();
            q.Add(5, 12, 4); q.Add(8, 12, 4);
            q.Remove(5, out _); // the ticket keeps the leader's id while 8 still waits
            Assert.AreEqual(QueueParties.AddResult.Refused, q.Add(5, 0, 1), "a new connection with the reused id (#499)");
            CollectionAssert.AreEqual(new[] { 8 }, q.Members(5));
        }
    }
}
