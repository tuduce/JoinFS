using System.Collections.Generic;

namespace JoinFS.Net
{
    /// <summary>
    /// The latest <see cref="IdentityUpdate"/> seen for every object, local or remote, keyed by
    /// (owner, object id). This is the state that lets protocols with different message shapes
    /// interoperate: the legacy protocol inlines identity into every position, JFP2 sends it
    /// separately, so encoding a legacy position (our own, or one being translated from JFP2 at a
    /// hub) reads the identity from here. See docs/network-plugin-architecture.md §2.6.
    ///
    /// Network thread only. Entries are dropped with their owner (peer leaves) or object
    /// (RemoveObject), so nothing leaks the way the old jfp2BridgeIdentityCache did.
    /// </summary>
    public sealed class ObjectStateCache
    {
        readonly Dictionary<(NodeId Owner, uint ObjectId), IdentityUpdate> identities = [];

        public void SetIdentity(NodeId owner, in IdentityUpdate identity) => identities[(owner, identity.ObjectId)] = identity;

        public bool TryGetIdentity(NodeId owner, uint objectId, out IdentityUpdate identity) =>
            identities.TryGetValue((owner, objectId), out identity);

        public void RemoveObject(NodeId owner, uint objectId) => identities.Remove((owner, objectId));

        public void RemoveOwner(NodeId owner)
        {
            List<(NodeId, uint)> doomed = null;
            foreach (var key in identities.Keys)
            {
                if (key.Owner == owner)
                {
                    (doomed ??= []).Add(key);
                }
            }
            if (doomed != null)
            {
                foreach (var key in doomed) identities.Remove(key);
            }
        }

        public void Clear() => identities.Clear();

        public int Count => identities.Count;
    }
}
