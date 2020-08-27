using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using Eco.Core.Plugins;
using Eco.Core.Plugins.Interfaces;
using Eco.Gameplay.Players;
using Eco.Gameplay.Systems.Chat;
using Eco.Shared.Localization;
using Eco.Shared.Utils;

namespace TwitchWhitelist
{
    [LocDisplayName("TwitchWhitelist Manager")]
    public class WhitelistManager: IModKitPlugin, IThreadedPlugin, IConfigurablePlugin, IChatCommandHandler, IShutdownablePlugin
    {
        private readonly PluginConfig<WhitelistConfig> _config;

        private static WhitelistManager Obj { get; set; }
        private static WhitelistConfig Config => Obj._config.Config;
        private readonly ManualResetEvent _waitEvent = new ManualResetEvent(false);
        
        private volatile bool _running = true;
        
        public object GetEditObject() => _config.Config;
        public IPluginConfig PluginConfig => Obj._config;

        public WhitelistManager()
        {
            Obj = this;
            _config = new PluginConfig<WhitelistConfig>("TwitchWhitelist");
        }

        public override string ToString()
        {
            return "Twitch Whitelist";
        }

        public void OnEditObjectChanged(object o, string param)
        {
            Log.WriteLine(Localizer.Do(FormattableStringFactory.Create("[TwitchWhitelist] Config changed, updating")));
            this.SaveConfig();
            UpdateWhitelist();
        }
        
        public string GetStatus()
        {
            return "Active";
        }

        public void Shutdown()
        {
            _running = false;
        }

        public void Run()
        {
            while (_running)
            {
                _waitEvent.Reset();
                UpdateWhitelist();
                _waitEvent.WaitOne(TimeSpan.FromSeconds(Math.Max(Config.RefreshIntervalSeconds, 5)));
            }
        }

        [ChatCommand("refresh-whitelist", "Refreshes the whitelist from the urls in the config", ChatAuthorizationLevel.Moderator)]
        public static void RefreshWhitelist(User user)
        {
            UpdateWhitelist(user);
        }
        
        private static void UpdateWhitelist(User user = null)
        {
            var preMessage = Localizer.Do(FormattableStringFactory.Create("[TwitchWhitelist] Refreshing whitelist ids..."));
            user?.Player.SendTemporaryMessage(preMessage);
            Log.WriteLine(preMessage);
            
            var whiteList = UserManager.Config.WhiteList;
            if (Config.WhitelistUrls.Count <= 0)
            {
                var noChangeMessage = Localizer.Do(FormattableStringFactory.Create("[TwitchWhitelist] No links in the config, skipping."));
                user?.Player.SendTemporaryMessage(noChangeMessage);
                Log.WriteLine(noChangeMessage);
                return;
            }
            
            var allIds = new HashSet<string>();
            var errored = false;
            foreach (var url in Config.WhitelistUrls)
            {
                List<string> ids = null;
                var wait = new ManualResetEvent(false);
                FetchWhitelist(url, (result, error) =>
                {
                    ids = result;
                    if (error != null)
                    {
                        errored = true;
                    }
                    wait.Set();
                });
                wait.WaitOne();
                allIds.AddAll(ids);
            }

            var old = whiteList.Clone();
            var oldListCount = whiteList.Count;
            if (!errored)
            {
                whiteList.Clear();
            }
            whiteList.AddUniqueRange(allIds.CleanStrings());
            whiteList.AddUniqueRange(Config.ManualWhiteList.CleanStrings());
            if (old.SequenceEqual(whiteList))
            {
                var noChange = Localizer.Do(FormattableStringFactory.Create("[TwitchWhitelist] Refreshed whitelist ids! Not change in list."));
                user?.Player.SendTemporaryMessage(noChange);
                Log.WriteLine(noChange);
                return;
            }
            UserManager.Obj.SaveConfig();
            
            var postMessage = Localizer.Do(FormattableStringFactory.Create("[TwitchWhitelist] Refreshed whitelist ids! Old amount: {0} whitelisted, New amount: {1} whitelisted", oldListCount, whiteList.Count));
            user?.Player.SendTemporaryMessage(postMessage);
            Log.WriteLine(postMessage);
        }
        
        private static void FetchWhitelist(string url, Action<List<string>, Exception> callback)
        {
            List<string> result = null;
            ThreadPool.QueueUserWorkItem(ar =>
            {
                Exception exception = null;
                try
                {
                    var webRequest = WebRequest.Create(url);
                    webRequest.Timeout = 10000;
                    using (var response = webRequest.GetResponse())
                    {
                        using (var responseStream = response.GetResponseStream())
                        {
                            using (var streamReader = new StreamReader(responseStream))
                                result = new List<string>(streamReader
                                    .ReadToEnd()
                                    .Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteErrorLine(Localizer.Do(FormattableStringFactory.Create("[TwitchWhitelist] Error when fetching whitelist: {0} {1}", url, (object) ex.Message)));
                    exception = ex;
                }
                callback(result, exception);
            });
        }
    }
}
