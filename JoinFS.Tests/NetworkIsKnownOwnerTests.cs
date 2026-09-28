using System.Collections.Generic;
using JoinFS;

namespace JoinFS.Tests;

/// <summary>
/// Regression coverage for the "ghost aircraft on reconnect" fix: a network-owned position update
/// must not be allowed to create a new Aircraft/Obj for an owner nuid this instance hasn't (yet)
/// registered via its own join handshake - see Network.IsKnownOwner and its call sites in
/// Sim.UpdateAircraft / Sim.UpdateObject.
///
/// Exercised through the static, dependency-free overload so no Main/Sim/Network instance (and its
/// SimConnect/settings baggage) is needed - the instance overload is a one-line forward to it.
/// </summary>
public class NetworkIsKnownOwnerTests
{
    static LocalNode.Nuid InvalidNuid() => new(0, 0, 0);
    static LocalNode.Nuid Nuid(uint ip) => new(ip, 6113, 0);

    [Fact]
    public void InvalidNuid_IsAlwaysKnown_EvenWithAnEmptyNodeList()
    {
        // Owner.Me / Owner.Recorder - own aircraft and Recorder-replayed objects always use an
        // invalid nuid, and must keep working regardless of what peers are currently registered.
        var nodeList = new Dictionary<LocalNode.Nuid, Network.Node>();

        Assert.True(Network.IsKnownOwner(InvalidNuid(), nodeList));
    }

    [Fact]
    public void RegisteredPeer_IsKnown()
    {
        var nuid = Nuid(0x0A000001);
        var nodeList = new Dictionary<LocalNode.Nuid, Network.Node> { [nuid] = new() };

        Assert.True(Network.IsKnownOwner(nuid, nodeList));
    }

    [Fact]
    public void UnregisteredPeer_IsNotKnown()
    {
        // this is the exact gap that let a ghost aircraft's owner slip through: a position packet
        // for a nuid whose join hasn't (yet) registered in nodeList
        var unregistered = Nuid(0x0A000002);
        var nodeList = new Dictionary<LocalNode.Nuid, Network.Node> { [Nuid(0x0A000001)] = new() };

        Assert.False(Network.IsKnownOwner(unregistered, nodeList));
    }

    [Fact]
    public void SamePeer_BecomesKnown_OnceItsJoinRegisters()
    {
        // simulates the race resolving itself: the first packet(s) after a reconnect arrive before
        // RegisterNode/nodeJoin has registered the new nuid, then the very next packet succeeds
        var nuid = Nuid(0x0A000003);
        var nodeList = new Dictionary<LocalNode.Nuid, Network.Node>();

        Assert.False(Network.IsKnownOwner(nuid, nodeList));

        nodeList[nuid] = new();

        Assert.True(Network.IsKnownOwner(nuid, nodeList));
    }

    [Fact]
    public void DifferentUnregisteredNuids_AreEachRejected()
    {
        // two aircraft from the same reconnecting peer (e.g. two Recorder-replayed aircraft
        // broadcast under the same still-unregistered nuid) must both be rejected, not just the
        // first one looked up
        var nodeList = new Dictionary<LocalNode.Nuid, Network.Node>();

        Assert.False(Network.IsKnownOwner(Nuid(0x0A000004), nodeList));
        Assert.False(Network.IsKnownOwner(Nuid(0x0A000005), nodeList));
    }
}
