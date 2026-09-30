using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Mail;
using System.Threading;

namespace JoinFS.Net
{
    /// <summary>
    /// Email/password-hash credentials for sessions that require login (hub operators). Persisted
    /// in password.txt in the documents folder ("email|name|hash" per line), reloaded when that
    /// file changes, and replaced wholesale when a member-list CSV (",email" in the second column)
    /// is dropped into the folder. Moved from LocalNode (#region Credentials); file formats are
    /// unchanged.
    ///
    /// Thread-safe: file watchers fire on thread-pool threads while logins are checked on the
    /// network thread.
    /// </summary>
    public sealed class CredentialStore : IDisposable
    {
        readonly string folder;
        readonly Action<string> error;
        readonly object gate = new();
        readonly Dictionary<MailAddress, uint> credentials = [];
        FileSystemWatcher passwordWatcher;
        FileSystemWatcher importWatcher;

        string PasswordFile => Path.Combine(folder, "password.txt");

        public CredentialStore(string folder, Action<string> error)
        {
            this.folder = folder;
            this.error = error;
        }

        /// <summary>Load password.txt and start watching the folder.</summary>
        public void Start()
        {
            Load();
            if (!Directory.Exists(folder))
            {
                return;
            }
            try
            {
                passwordWatcher = new FileSystemWatcher(folder)
                {
                    // Program.Code("password.txt", true, 1234), as the original LocalNode.PasswordWatcher
                    Filter = Program.Code("jDJ~>Jj3.\\\"d", false, 1234),
                };
                passwordWatcher.Changed += (_, _) => { Thread.Sleep(1000); Load(); };
                passwordWatcher.EnableRaisingEvents = true;

                importWatcher = new FileSystemWatcher(folder)
                {
                    // Program.Code("*.csv", true, 1234)
                    Filter = Program.Code("vsS\\)", false, 1234),
                };
                importWatcher.Changed += (_, e) => { Thread.Sleep(1000); Import(e.FullPath); };
                importWatcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                error?.Invoke("ERROR: Watching credentials folder - " + ex.Message);
            }
        }

        public void Dispose()
        {
            passwordWatcher?.Dispose();
            importWatcher?.Dispose();
            passwordWatcher = null;
            importWatcher = null;
        }

        /// <summary>Check a login attempt; stores <paramref name="hash"/> when <paramref name="verify"/> is set and the address is known.</summary>
        public LoginResult Check(MailAddress address, uint hash, bool verify)
        {
            lock (gate)
            {
                if (!credentials.TryGetValue(address, out uint stored))
                {
                    return LoginResult.InvalidAddress;
                }
                if (!verify && stored == 0)
                {
                    return LoginResult.VerifyPassword;
                }
                if (!verify && stored != hash)
                {
                    return LoginResult.InvalidPassword;
                }
                if (verify)
                {
                    credentials[address] = hash;
                    Save();
                }
                return LoginResult.Accepted;
            }
        }

        /// <summary>For tests: set one entry without touching files.</summary>
        public void Set(MailAddress address, uint hash)
        {
            lock (gate) credentials[address] = hash;
        }

        public void Load()
        {
            lock (gate)
            {
                try
                {
                    credentials.Clear();
                    if (!File.Exists(PasswordFile))
                    {
                        return;
                    }
                    foreach (string line in File.ReadLines(PasswordFile))
                    {
                        string[] parts = line.Split('|');
                        try
                        {
                            MailAddress address = new(parts[0]);
                            if (parts.Length == 2 && uint.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out uint hash))
                            {
                                credentials[address] = hash;
                            }
                            else if (parts.Length >= 3 && uint.TryParse(parts[2], NumberStyles.Number, CultureInfo.InvariantCulture, out hash))
                            {
                                credentials[address] = hash;
                            }
                            else
                            {
                                credentials[address] = 0;
                            }
                        }
                        catch (Exception ex)
                        {
                            error?.Invoke("ERROR: Invalid credentials - " + parts[0] + " - " + ex.Message);
                        }
                    }
                }
                catch (Exception ex)
                {
                    error?.Invoke("ERROR: Reading password file - " + ex.Message);
                }
            }
        }

        // caller holds gate
        void Save()
        {
            try
            {
                using var writer = new StreamWriter(PasswordFile);
                foreach (var entry in credentials)
                {
                    writer.WriteLine(entry.Key + "|" + NetHash.GenerateName(entry.Key.ToString().ToUpper()) + "|" + entry.Value);
                }
            }
            catch (Exception ex)
            {
                error?.Invoke("ERROR: Writing password file - " + ex.Message);
            }
        }

        void Import(string path)
        {
            lock (gate)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        List<MailAddress> imported = [];
                        foreach (string line in File.ReadLines(path))
                        {
                            string[] parts = line.Split(',');
                            if (parts.Length > 1)
                            {
                                try { imported.Add(new MailAddress(parts[1])); } catch { }
                            }
                        }
                        foreach (MailAddress address in imported)
                        {
                            credentials.TryAdd(address, 0);
                        }
                        List<MailAddress> removed = [];
                        foreach (MailAddress address in credentials.Keys)
                        {
                            if (!imported.Contains(address)) removed.Add(address);
                        }
                        foreach (MailAddress address in removed)
                        {
                            credentials.Remove(address);
                        }
                        Save();
                    }
                }
                catch (Exception ex)
                {
                    error?.Invoke("ERROR: Reading password file - " + ex.Message);
                }
                try { File.Delete(path); } catch { }
            }
        }
    }
}
