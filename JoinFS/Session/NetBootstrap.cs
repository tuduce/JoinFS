using JoinFS.Net;
using JoinFS.Properties;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace JoinFS
{
    /// <summary>
    /// Getting this node onto the network: its addresses (the app side decides them and hands
    /// them to the network thread), the seed hubs, the ban list and the public IP downloaded from
    /// the web, and a DNS cache for hub addresses.
    /// </summary>
    public sealed class NetBootstrap : IEndPointResolver
    {
        const string SeedHubsUrl = "https://raw.githubusercontent.com/tuduce/JoinFS/refs/heads/main/JoinFS/util/seedhubs.txt";
        const string SeedHubsFallbackUrl = "https://drive.google.com/uc?export=download&id=0Byn9605PQfMecnhwdUtITi1yYlk";
        const string BanListUrl = "https://raw.githubusercontent.com/tuduce/JoinFS/refs/heads/main/JoinFS/util/banlist.txt";
        const string BanListFallbackUrl = "https://drive.google.com/uc?export=download&id=1yhrHsv8s0_vnBhzyy7hgSv0Yw_31eJLu";
        const string MyIpUrl = "https://checkip.amazonaws.com/";
        const string MyIpFallbackUrl = "https://ipinfo.io/ip";

        static readonly HttpClient httpClient = new();

        readonly NetworkService service;
        readonly ISessionLog log;

        /// <summary>
        /// This node's addresses and id, as the app decided them. The network thread's own
        /// LocalIdentity gets the same values (it can't be read synchronously from here).
        /// </summary>
        readonly LocalIdentity identity = new();

        readonly Timer internetAddressTimer = new(10800.0);

        // written by download tasks, taken on the app thread
        volatile string myip = null;
        volatile string[] seedHubs = null;
        volatile string[] banList = null;
        bool myipFallback = false;
        bool seedHubsFallback = false;
        bool banListFallback = false;

        readonly Dictionary<string, IPAddress> dnsLookups = [];
        DateTime dnsResetTime = DateTime.Now.AddDays(1);

        /// <param name="localAddressOverride">A LAN address to use instead of the detected one (Docker, NAT), or empty.</param>
        /// <param name="knownPublicIp">The public IP remembered from last time, used until a fresh one is downloaded.</param>
        public NetBootstrap(NetworkService service, ISessionLog log, string localAddressOverride, string knownPublicIp, double now)
        {
            this.service = service;
            this.log = log;

            IPAddress local = LocalIdentity.DetectLocalAddress();
            if (IPAddress.TryParse(localAddressOverride, out IPAddress overrideAddress) && overrideAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                local = overrideAddress;
            }
            identity.LocalAddress = local;
            if (IsIPv4(knownPublicIp, out IPAddress address))
            {
                identity.InternetAddress = address;
            }
            Publish();

            // first refresh of the public IP (hubs only) after one interval
            internetAddressTimer.Elapsed(now);
        }

        public NodeId LocalId => identity.Id;

        public IPAddress LocalAddress => identity.LocalAddress;

        /// <summary>The UDP port changed.</summary>
        public void SetPort(ushort port)
        {
            identity.Port = port;
            Publish();
        }

        void Publish()
        {
            IPAddress local = identity.LocalAddress;
            IPAddress internet = identity.InternetAddress;
            service.Post(core =>
            {
                core.Identity.LocalAddress = local;
                if (!internet.Equals(IPAddress.None)) core.Identity.InternetAddress = internet;
            });
        }

        static bool IsIPv4(string text, out IPAddress address) =>
            IPAddress.TryParse(text, out address) && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;

        // ------------------------------------------------------------------ downloads

        /// <summary>Download the public IP (and the seed hubs and ban list), waiting for them.</summary>
        public void DownloadAll(bool hubs)
        {
            var tasks = new List<Task> { DownloadMyIpAsync(MyIpUrl) };
            if (hubs)
            {
                tasks.Add(DownloadSeedHubsAsync(SeedHubsUrl));
                tasks.Add(DownloadBanListAsync(BanListUrl));
            }
            Task.WhenAll(tasks).GetAwaiter().GetResult();
        }

        async Task DownloadMyIpAsync(string url)
        {
            try
            {
                string result = (await httpClient.GetStringAsync(url)).TrimEnd('\n');
                if (IsIPv4(result, out _))
                {
                    Settings.Default.MyIp = result;
                    myip = result;
                    return;
                }
            }
            catch (Exception ex)
            {
                log.Event("Error downloading IP address from " + url + ": " + ex.Message);
            }
            // try the fallback once, then keep what we had
            if (!myipFallback)
            {
                myipFallback = true;
                await DownloadMyIpAsync(MyIpFallbackUrl);
            }
            else
            {
                myip = Settings.Default.MyIp;
            }
        }

        async Task DownloadSeedHubsAsync(string url)
        {
            try
            {
                seedHubs = (await httpClient.GetStringAsync(url)).Split('\n');
                log.Event("Seedhubs download complete from " + url);
            }
            catch (Exception ex)
            {
                log.Event("Error downloading seedhubs from " + url + ": " + ex.Message);
                if (!seedHubsFallback)
                {
                    seedHubsFallback = true;
                    await DownloadSeedHubsAsync(SeedHubsFallbackUrl);
                }
                else
                {
                    seedHubs = [""];
                }
            }
        }

        async Task DownloadBanListAsync(string url)
        {
            try
            {
                banList = (await httpClient.GetStringAsync(url)).Split('\n');
            }
            catch
            {
                if (!banListFallback)
                {
                    banListFallback = true;
                    await DownloadBanListAsync(BanListFallbackUrl);
                }
                else
                {
                    banList = [""];
                }
            }
        }

        /// <summary>Apply finished downloads, refresh the public IP now and then (hubs), and reset the DNS cache daily.</summary>
        public void DoWork(double now, bool isHub)
        {
            if (isHub && internetAddressTimer.Elapsed(now))
            {
                // fire and forget: applied on a later tick
                _ = DownloadMyIpAsync(MyIpUrl);
            }

            string ip = myip;
            if (ip != null)
            {
                myip = null;
                if (IsIPv4(ip, out IPAddress address))
                {
                    identity.InternetAddress = address;
                    Publish();
                }
            }

            string[] banned = banList;
            if (banned != null)
            {
                banList = null;
                foreach (var entry in banned)
                {
                    service.Post(core => core.BanIP(entry));
                }
            }

            if (DateTime.Now > dnsResetTime)
            {
                dnsLookups.Clear();
                dnsResetTime = DateTime.Now.AddDays(1);
            }
        }

        /// <summary>The downloaded seed hubs, once (empty until the download finished).</summary>
        public List<IPEndPoint> TakeSeedHubs()
        {
            List<IPEndPoint> result = [];
            string[] seeds = seedHubs;
            if (seeds != null)
            {
                seedHubs = null;
                foreach (var seed in seeds)
                {
                    if (seed.Length > 0 && MakeEndPoint(seed.TrimEnd('\r'), Network.DEFAULT_PORT, out IPEndPoint endPoint))
                    {
                        result.Add(endPoint);
                    }
                }
            }
            return result;
        }

        // ------------------------------------------------------------------ endpoints and DNS

        public IPEndPoint MakeEndPoint(NodeId node, ushort port) => identity.MakeEndPoint(node, port);

        public bool MakeEndPoint(string addressText, ushort port, out IPEndPoint endPoint)
        {
            endPoint = new IPEndPoint(0, 0);
            string[] parts = addressText.Split(':');
            if (parts.Length <= 0)
            {
                return false;
            }
            int remotePort = port;
            if (parts.Length > 1 && Int32.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out remotePort) == false)
            {
                return false;
            }
            if (IsIPv4(parts[0], out IPAddress address)
                || DnsLookup(parts[0], out address) && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                endPoint = new IPEndPoint(address, remotePort);
                return true;
            }
            return false;
        }

        /// <summary>Look up a host name (cached for a day, failures included).</summary>
        public bool DnsLookup(string addressText, out IPAddress address)
        {
            if (dnsLookups.TryGetValue(addressText, out address))
            {
                return !address.Equals(IPAddress.None);
            }
            try
            {
                IPAddress[] list = Dns.GetHostAddresses(addressText);
                address = list.Length > 0 ? list[0] : IPAddress.None;
            }
            catch (Exception ex)
            {
                log.Event(ex.Message);
                address = IPAddress.None;
            }
            dnsLookups[addressText] = address;
            return !address.Equals(IPAddress.None);
        }
    }
}
