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
            Assert.AreEqual(QueueParties.AddResult.NewTicket, q.Add(5, 12));
            Assert.AreEqual(QueueParties.AddResult.JoinedTicket, q.Add(8, 12));
            Assert.AreEqual(QueueParties.AddResult.NewTicket, q.Add(9, 0), "no party: its own ticket");
            Assert.AreEqual(QueueParties.AddResult.NewTicket, q.Add(10, 0));
            CollectionAssert.AreEqual(new[] { 5, 8 }, q.Members(5));
            Assert.AreEqual(QueueParties.AddResult.Duplicate, q.Add(8, 12));
        }

        [Test]
        public void TicketStays_UntilTheLastMemberLeaves()
        {
            var q = new QueueParties();
            q.Add(5, 12); q.Add(8, 12);
            Assert.IsFalse(q.Remove(5, out _), "one member left, the party's ticket remains");
            CollectionAssert.AreEqual(new[] { 8 }, q.Members(5));
            Assert.IsTrue(q.Remove(8, out int ticket)); Assert.AreEqual(5, ticket);
            Assert.AreEqual(QueueParties.AddResult.NewTicket, q.Add(5, 12), "a fresh ticket for the same party afterwards");
            Assert.IsFalse(q.Remove(99, out _));
        }
    }
}
