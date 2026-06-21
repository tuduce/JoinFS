#if CONSOLE
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace JoinFS
{
    public class WebhookService
    {
        readonly Main main;

        readonly uint vuidCom1;
        readonly uint vuidCom2;

        readonly Dictionary<Guid, (string com1, string com2)> _previous = [];

        static readonly HttpClient _http = new();

        public WebhookService(Main main)
        {
            this.main = main;
            vuidCom1 = VariableMgr.CreateVuid("com active frequency:1");
            vuidCom2 = VariableMgr.CreateVuid("com active frequency:2");
        }

        static string FormatFreq(int raw)
        {
            string s = raw.ToString();
            return s.Length < 3 ? "" : s[..3] + "." + s[3..];
        }

        public void DoWork()
        {
            // collect changed aircraft inside the conch lock
            List<object> changed = null;

            if (main.sim != null)
            {
                foreach (var obj in main.sim.objectList)
                {
                    if (obj is not Sim.Aircraft aircraft) continue;

                    Guid guid = main.network.GetNodeGuid(aircraft.ownerNuid);
                    if (guid == Guid.Empty) guid = new Guid(aircraft.simId, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

                    string com1 = "", com2 = "";
                    if (aircraft.variableSet != null)
                    {
                        com1 = FormatFreq(aircraft.variableSet.GetInteger(vuidCom1));
                        com2 = FormatFreq(aircraft.variableSet.GetInteger(vuidCom2));
                    }

                    if (com1.Length == 0 && com2.Length == 0) continue;

                    bool existed = _previous.TryGetValue(guid, out var prev);
                    bool changed_ = !existed || prev.com1 != com1 || prev.com2 != com2;

                    _previous[guid] = (com1, com2);

                    // only fire on actual change (skip first-seen entries)
                    if (existed && changed_)
                    {
                        changed ??= [];
                        changed.Add(new
                        {
                            callsign = aircraft.flightPlan.callsign,
                            nickname = main.network.GetNodeName(aircraft.ownerNuid),
                            com1,
                            com2
                        });
                    }
                }
            }

            if (changed == null) return;

            // capture locals for the async task
            string uri = main.settingsComsWebhookUri;
            string method = main.settingsComsWebhookMethod.ToUpperInvariant();
            bool log = main.settingsWebSocketLog;
            string json = JsonConvert.SerializeObject(new { comsupdate = changed });

            Task.Run(async () =>
            {
                try
                {
                    if (log)
                        main.monitor.Write($"Webhook {method} {uri}\n{json}");

                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    HttpResponseMessage response = method switch
                    {
                        "POST"  => await _http.PostAsync(uri, content),
                        "PATCH" => await _http.PatchAsync(uri, content),
                        "GET"   => await _http.GetAsync(uri),
                        _       => await _http.PutAsync(uri, content)
                    };
                    if (log)
                        main.monitor.Write($"Webhook {method} {uri} → {(int)response.StatusCode}");
                }
                catch (Exception ex)
                {
                    if (log)
                        main.monitor.Write($"Webhook error: {ex.Message}");
                }
            });
        }
    }
}
#endif
