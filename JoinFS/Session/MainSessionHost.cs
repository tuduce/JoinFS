using JoinFS.Net;
using JoinFS.Properties;
using System;
using System.Collections.Generic;

namespace JoinFS
{
    /// <summary>
    /// The application as the network session's parts see it: the one place in JoinFS/Session that
    /// knows about Main, the simulator, the recorder, the forms and Settings. Subsystems created
    /// after the network (recorder, euroscope, notes, address book) are looked up when used.
    /// </summary>
    sealed class MainSessionHost : ILocalProfile, ISimSink, ISimView, ISessionUi, ISessionLog, IClock, IAtcListener
    {
        readonly Main main;

        public MainSessionHost(Main main)
        {
            this.main = main;
        }

        // ------------------------------------------------------------------ IClock, ISessionLog

        public double Now => main.ElapsedTime;
        public long Timestamp => System.Diagnostics.Stopwatch.GetTimestamp();
        public long Frequency => System.Diagnostics.Stopwatch.Frequency;

        public void Event(string text) => main.MonitorEvent(text);

        public void Network(string text) => main.MonitorNetwork(text);

        // ------------------------------------------------------------------ ILocalProfile

        public Guid Guid => main.guid;
        public uint Uuid => main.uuid;
        public string Nickname => main.settingsNickname;
        public bool Hub => main.settingsHub;
        public string HubName => main.settingsHubName;
        public string HubAbout => main.settingsHubAbout;
        public string HubVoip => main.settingsHubVoip;
        public string HubEvent => main.settingsHubEvent;
        public string HubDomain => main.settingsHubDomain;
        public bool Atc => main.settingsAtc;
        public string AtcAirport => main.settingsAtcAirport;
        public int AtcLevel => Settings.Default.AtcLevel;
        public int AtcFrequency => Settings.Default.AtcFrequency;
        public int ActivityCircle => main.settingsActivityCircle;
        public string MyIp => Settings.Default.MyIp;
        public bool MultipleObjects => main.settingsMultiObjects;
        public bool ShareCockpitEveryone => Settings.Default.ShareCockpitEveryone;
        public bool LowBandwidth => Settings.Default.LowBandwidth;
        public bool ElevationCorrection => Settings.Default.ElevationCorrection;
        public bool PublishWhazzup => main.settingsWhazzup && main.settingsWhazzupPublic;
        public string Password => main.settingsPassword;
        public string StoragePath => main.storagePath;
        public string DocumentsPath => main.documentsPath;
        public List<AddressBook.AddressBookEntry> AddressBook => main.addressBook?.entries ?? [];

        // ------------------------------------------------------------------ IAtcListener

        public void AddAtc(string airport, int level, int frequency) => main.euroscope?.AddAtc(airport, level, frequency);

        public void RemoveAtc(string airport, int level, int frequency) => main.euroscope?.RemoveAtc(airport, level, frequency);

        // ------------------------------------------------------------------ ISimView

        public bool Available => main.sim != null;

        public string SimulatorName => main.sim?.GetSimulatorName();

        public bool SimulatorConnected => main.sim != null && main.sim.Connected;

        public string UserCallsign => main.sim != null ? main.sim.userFlightPlan.callsign : "";

        public Sim.Aircraft UserAircraft => main.sim?.userAircraft;

        public Sim.Aircraft FindUserAircraft(NodeId owner) =>
            main.sim?.objectList.Find(o => o.ownerNuid == owner && o is Sim.Aircraft aircraft && aircraft.user) as Sim.Aircraft;

        public void CountObjects(out ushort planes, out ushort helicopters, out ushort boats, out ushort vehicles)
        {
            planes = helicopters = boats = vehicles = 0;
            Sim sim = main.sim;
            if (sim == null) return;
            foreach (var obj in sim.objectList)
            {
                if (obj.owner == Sim.Obj.Owner.Network || sim.IsBroadcast(obj))
                {
                    if (obj is Sim.Plane) planes++;
                    else if (obj is Sim.Helicopter) helicopters++;
                    else if (obj is Sim.Boat) boats++;
                    else if (obj is Sim.Vehicle) vehicles++;
                }
            }
        }

        public bool TryGetAirport(string code, out Main.Airport airport) => main.airportList.TryGetValue(code, out airport);

        // ------------------------------------------------------------------ ISimSink

        public bool TryGetOwnAircraft(out NodeId owner, out uint netId)
        {
            Sim.Aircraft aircraft = main.sim?.userAircraft;
            owner = aircraft?.ownerNuid ?? new NodeId();
            netId = aircraft?.netId ?? 0;
            return aircraft != null;
        }

        public string CurrentMetar => main.sim?.scheduleMetar;

        bool Recording(Sim.Obj obj) => obj != null && main.recorder.recording && obj.record;

        public void ChangeIdentity(NodeId owner, in IdentityUpdate identity)
        {
            Sim sim = main.sim;
            uint objectId = identity.ObjectId;
            Sim.Obj obj = sim?.objectList.Find(o => o.ownerNuid == owner && o.netId == objectId);
            if (obj == null) return;
            bool modelChanged = identity.Model != obj.ownerModel;
            sim.UpdateObject(obj, identity.Model, identity.Livery, identity.IcaoType, identity.IcaoAirline, identity.ClassCode, identity.Wtc, identity.ClassCodeConfirmed, identity.TypeRole);
            if (modelChanged)
            {
                // respawn under the new model
                sim.RemoveObjectFromSim(obj);
            }
        }

        public void UpdateAircraft(NodeId owner, in IdentityUpdate identity, bool user, string nickname, in PositionUpdate position)
        {
            Sim sim = main.sim;
            if (sim == null) return;
            Sim.AircraftPosition aircraftPosition = SimMessageMapper.ToAircraftPosition(position);
            Sim.Aircraft aircraft = sim.UpdateAircraft(owner, position.ObjectId, user, identity.IsPlane, identity.Callsign, identity.Registration, nickname,
                identity.Model, identity.Livery, identity.IcaoType, identity.IcaoAirline, identity.FlightNumber, identity.ClassCode, identity.Wtc,
                identity.ClassCodeConfirmed, identity.TypeRole, position.NetTime, ref aircraftPosition);
            if (aircraft != null)
            {
                aircraft.paused = (position.StateFlags & PositionStateFlags.Paused) != 0;
                if (Recording(aircraft))
                {
                    main.recorder.Record(aircraft.recorderObj, position.NetTime, ref aircraftPosition);
                }
            }
        }

        public void UpdateOwnAircraft(in PositionUpdate position)
        {
            Sim sim = main.sim;
            if (sim?.userAircraft == null) return;
            sim.UpdateAircraft(sim.userAircraft, position.NetTime, SimMessageMapper.ToAircraftPosition(position));
        }

        public void UpdateObject(NodeId owner, in IdentityUpdate identity, in ObjectPositionUpdate position)
        {
            Sim sim = main.sim;
            if (sim == null) return;
            Sim.ObjectPositionVelocity positionVelocity = SimMessageMapper.ToPositionVelocity(position);
            Sim.Obj simObject = sim.UpdateObject(owner, position.ObjectId, identity.Model, identity.Livery, identity.IcaoType, identity.IcaoAirline,
                identity.ClassCode, identity.Wtc, identity.ClassCodeConfirmed, identity.TypeRole, position.NetTime, ref positionVelocity);
            if (simObject != null)
            {
                simObject.paused = (position.StateFlags & PositionStateFlags.Paused) != 0;
                if (Recording(simObject))
                {
                    main.recorder.Record(simObject.recorderObj, position.NetTime, ref positionVelocity);
                }
            }
        }

        public void UpdateVariables(NodeId owner, uint netId, Dictionary<uint, int> integers, Dictionary<uint, float> floats, Dictionary<uint, string> string8s, bool record)
        {
            Sim sim = main.sim;
            if (sim == null) return;
            if (integers != null)
            {
                Sim.Aircraft aircraft = sim.UpdateAircraft(owner, netId, integers);
                if (record && Recording(aircraft)) main.recorder.Record(aircraft.recorderObj, integers);
            }
            if (floats != null)
            {
                Sim.Aircraft aircraft = sim.UpdateAircraft(owner, netId, floats);
                if (record && Recording(aircraft)) main.recorder.Record(aircraft.recorderObj, floats);
            }
            if (string8s != null)
            {
                Sim.Aircraft aircraft = sim.UpdateAircraft(owner, netId, string8s);
                if (record && Recording(aircraft)) main.recorder.Record(aircraft.recorderObj, string8s);
            }
        }

        public void ApplyEvent(NodeId owner, uint netId, uint eventId, uint data, bool flightControls, bool record)
        {
            Sim.Aircraft aircraft = main.sim?.UpdateAircraft(owner, netId, eventId, data, flightControls);
            if (record && Recording(aircraft))
            {
                main.recorder.Record(aircraft.recorderObj, eventId, data);
            }
        }

        public void RemoveObject(NodeId owner, uint netId) => main.sim?.RemoveObject(owner, netId);

        public void RemoveObjects(NodeId owner) => main.sim?.RemoveObject(owner);

        public void UpdateUserFlightPlan(in FlightPlanUpdate flightPlan)
        {
            Sim sim = main.sim;
            if (sim == null) return;
            SimMessageMapper.CopyTo(flightPlan, sim.userFlightPlan);
            if (sim.userAircraft != null)
            {
                sim.userAircraft.flightPlanVersion++;
                if (sim.userAircraft.flightPlanVersion == 0) sim.userAircraft.flightPlanVersion = 1;
            }
        }

        public void UpdateAircraftFlightPlan(NodeId owner, uint netId, in FlightPlanUpdate flightPlan)
        {
            if (main.sim?.objectList.Find(o => o.ownerNuid == owner && o.netId == netId) is Sim.Aircraft aircraft)
            {
                aircraft.flightPlanVersion = flightPlan.FormatVersion;
                SimMessageMapper.CopyTo(flightPlan, aircraft.flightPlan);
            }
        }

        public void SetWeather(string metar) => main.sim?.SetWeatherObservation(metar);

        public void SetWeather(NodeId from, string metar) => main.sim?.SetWeatherObservation(from, metar);

        public void ShareCockpit(NodeId nuid, ShareCockpitFlags share) => main.sim?.ShareCockpit(nuid, (byte)share);

        public void NicknameChanged(NodeId nuid) => main.sim?.SetAtcId(nuid);

        // ------------------------------------------------------------------ ISessionUi

        public void SessionChanged(int refreshes)
        {
#if !SERVER && !CONSOLE
            main.aircraftForm?.refresher.Schedule(refreshes);
            main.objectsForm?.refresher.Schedule(refreshes);
#endif
#if !CONSOLE
            main.sessionForm?.usersRefresher.Schedule(refreshes);
#endif
        }

        public void HubsChanged(int refreshes)
        {
#if !CONSOLE
            main.hubsForm?.refresher.Schedule(refreshes);
#endif
        }

        public bool CommsVisible
        {
            get
            {
#if !CONSOLE
                return main.sessionForm != null && main.sessionForm.Visible;
#else
                return false;
#endif
            }
        }

        public void CommsChanged()
        {
#if !CONSOLE
            if (main.sessionForm != null && main.mainForm != null && main.sessionForm.Visible)
            {
                main.mainForm.refreshComms = true;
            }
#endif
        }

        public bool AddressBookVisible
        {
            get
            {
#if !CONSOLE
                return main.addressBookForm != null && main.addressBookForm.Visible;
#else
                return false;
#endif
            }
        }

        public bool ShowsGlobalUsers
        {
            get
            {
#if !SERVER && !CONSOLE
                return main.aircraftForm != null && main.aircraftForm.Visible && Settings.Default.IncludeGlobalAircraft
                    || main.atcForm != null && main.atcForm.Visible;
#else
                return false;
#endif
            }
        }

        public void ShowMessage(string message) => main.ShowMessage(message);
    }
}
