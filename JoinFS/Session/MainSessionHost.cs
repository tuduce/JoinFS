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
        // Read from the sim thread's snapshot (Sim.View): this runs on the app thread.

        public bool Available => main.sim != null;

        public string SimulatorName => main.sim?.View.SimulatorName;

        public bool SimulatorConnected => main.sim != null && main.sim.View.Connected;

        public string UserCallsign => main.sim != null ? main.sim.View.UserFlightPlan.callsign : "";

        public Sim.Aircraft UserAircraft => main.sim?.View.UserAircraft;

        public Sim.Aircraft FindUserAircraft(NodeId owner)
        {
            Sim sim = main.sim;
            if (sim == null) return null;
            foreach (var obj in sim.View.Objects)
            {
                if (obj.ownerNuid == owner && obj is Sim.Aircraft aircraft && aircraft.user) return aircraft;
            }
            return null;
        }

        public void CountObjects(out ushort planes, out ushort helicopters, out ushort boats, out ushort vehicles)
        {
            planes = helicopters = boats = vehicles = 0;
            Sim sim = main.sim;
            if (sim == null) return;
            foreach (var obj in sim.View.Objects)
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
        // Queries read the snapshot; everything that changes the simulator or the recorder is
        // posted to the sim thread, in arrival order (docs/sim-thread-architecture.md §2.5).

        public bool TryGetOwnAircraft(out NodeId owner, out uint netId)
        {
            Sim.Aircraft aircraft = main.sim?.View.UserAircraft;
            owner = aircraft?.ownerNuid ?? new NodeId();
            netId = aircraft?.netId ?? 0;
            return aircraft != null;
        }

        public string CurrentMetar => main.sim?.View.CurrentMetar;

        /// <summary>Run <paramref name="action"/> on the sim thread, if there is a simulator</summary>
        void Post(Action<Sim> action)
        {
            if (main.sim == null) return;
            main.PostToSim(() =>
            {
                Sim sim = main.sim;
                if (sim != null) action(sim);
            });
        }

        // sim thread
        bool Recording(Sim.Obj obj) => obj != null && main.recorder.recording && obj.record;

        public void ChangeIdentity(NodeId owner, in IdentityUpdate identity)
        {
            IdentityUpdate update = identity;
            Post(sim =>
            {
                uint objectId = update.ObjectId;
                Sim.Obj obj = sim.objectList.Find(o => o.ownerNuid == owner && o.netId == objectId);
                if (obj == null) return;
                bool modelChanged = update.Model != obj.ownerModel;
                sim.UpdateObject(obj, update.Model, update.Livery, update.IcaoType, update.IcaoAirline, update.ClassCode, update.Wtc, update.ClassCodeConfirmed, update.TypeRole);
                if (modelChanged)
                {
                    // respawn under the new model
                    sim.RemoveObjectFromSim(obj);
                }
            });
        }

        public void UpdateAircraft(NodeId owner, in IdentityUpdate identity, bool user, string nickname, in PositionUpdate position, double receivedAt)
        {
            IdentityUpdate id = identity;
            PositionUpdate update = position;
            Post(sim =>
            {
                Sim.AircraftPosition aircraftPosition = SimMessageMapper.ToAircraftPosition(update);
                Sim.Aircraft aircraft = sim.UpdateAircraft(owner, update.ObjectId, user, id.IsPlane, id.Callsign, id.Registration, nickname,
                    id.Model, id.Livery, id.IcaoType, id.IcaoAirline, id.FlightNumber, id.ClassCode, id.Wtc,
                    id.ClassCodeConfirmed, id.TypeRole, update.NetTime, ref aircraftPosition, receivedAt);
                if (aircraft != null)
                {
                    aircraft.paused = (update.StateFlags & PositionStateFlags.Paused) != 0;
                    if (Recording(aircraft))
                    {
                        main.recorder.Record(aircraft.recorderObj, update.NetTime, ref aircraftPosition);
                    }
                }
            });
        }

        public void UpdateOwnAircraft(in PositionUpdate position, double receivedAt)
        {
            PositionUpdate update = position;
            Post(sim =>
            {
                if (sim.userAircraft == null) return;
                sim.UpdateAircraft(sim.userAircraft, update.NetTime, SimMessageMapper.ToAircraftPosition(update), receivedAt);
            });
        }

        public void UpdateObject(NodeId owner, in IdentityUpdate identity, in ObjectPositionUpdate position, double receivedAt)
        {
            IdentityUpdate id = identity;
            ObjectPositionUpdate update = position;
            Post(sim =>
            {
                Sim.ObjectPositionVelocity positionVelocity = SimMessageMapper.ToPositionVelocity(update);
                Sim.Obj simObject = sim.UpdateObject(owner, update.ObjectId, id.Model, id.Livery, id.IcaoType, id.IcaoAirline,
                    id.ClassCode, id.Wtc, id.ClassCodeConfirmed, id.TypeRole, update.NetTime, ref positionVelocity, receivedAt);
                if (simObject != null)
                {
                    simObject.paused = (update.StateFlags & PositionStateFlags.Paused) != 0;
                    if (Recording(simObject))
                    {
                        main.recorder.Record(simObject.recorderObj, update.NetTime, ref positionVelocity);
                    }
                }
            });
        }

        public void UpdateVariables(NodeId owner, uint netId, Dictionary<uint, int> integers, Dictionary<uint, float> floats, Dictionary<uint, string> string8s, bool record)
        {
            Post(sim =>
            {
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
            });
        }

        public void ApplyEvent(NodeId owner, uint netId, uint eventId, uint data, bool flightControls, bool record)
        {
            Post(sim =>
            {
                Sim.Aircraft aircraft = sim.UpdateAircraft(owner, netId, eventId, data, flightControls);
                if (record && Recording(aircraft))
                {
                    main.recorder.Record(aircraft.recorderObj, eventId, data);
                }
            });
        }

        public void RemoveObject(NodeId owner, uint netId) => Post(sim => sim.RemoveObject(owner, netId));

        public void RemoveObjects(NodeId owner) => Post(sim => sim.RemoveObject(owner));

        public void UpdateUserFlightPlan(in FlightPlanUpdate flightPlan)
        {
            FlightPlanUpdate update = flightPlan;
            Post(sim =>
            {
                SimMessageMapper.CopyTo(update, sim.userFlightPlan);
                sim.MarkViewDirty();
            });
        }

        public void UpdateAircraftFlightPlan(NodeId owner, uint netId, in FlightPlanUpdate flightPlan)
        {
            FlightPlanUpdate update = flightPlan;
            Post(sim =>
            {
                if (sim.objectList.Find(o => o.ownerNuid == owner && o.netId == netId) is Sim.Aircraft aircraft)
                {
                    SimMessageMapper.CopyTo(update, aircraft.flightPlan);
                }
            });
        }

        public void SetWeather(string metar) => Post(sim => sim.SetWeatherObservation(metar));

        public void SetWeather(NodeId from, string metar) => Post(sim => sim.SetWeatherObservation(from, metar));

        public void ShareCockpit(NodeId nuid, ShareCockpitFlags share) => Post(sim => sim.ShareCockpit(nuid, (byte)share));

        public void NicknameChanged(NodeId nuid) => Post(sim => sim.SetAtcId(nuid));

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
