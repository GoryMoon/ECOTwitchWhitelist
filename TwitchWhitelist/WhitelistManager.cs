using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using Eco.Core.Plugins;
using Eco.Core.Plugins.Interfaces;
using Eco.Core.Utils;
using Eco.Gameplay.Players;
using Eco.Gameplay.Systems.Chat;
using Eco.Gameplay.Systems.TextLinks;
using Eco.Shared.Localization;
using Eco.Shared.Utils;

namespace TwitchWhitelist
{
    [LocDisplayName("TwitchWhitelist Manager")]
    public class WhitelistManager: IModKitPlugin, IThreadedPlugin, IConfigurablePlugin, IChatCommandHandler
    {
        private static string LogTag = "[TwitchWhitelist]";
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

        public ThreadSafeAction<object, string> ParamChanged { get; set; } = new ThreadSafeAction<object, string>();

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

        [ChatCommand("Performs commands for twitch whitelist management.", "tw", ChatAuthorizationLevel.Moderator)]
        public static void TwitchWhitelist(User user)
        {}
        
        [ChatSubCommand("TwitchWhitelist", "Adds user to the manual whitelist by account id, steamid, slgid or username. These users aren't affected by their subscription status")]
        public static void Add(User user, string whitelistIdOrName)
        {
            var user1 = UserManager.FindUser(whitelistIdOrName, out var type);
            if (user1 != null)
            {
                WhitelistUser(user1.SteamId);
                WhitelistUser(user1.SlgId);
                user1.Player?.MsgLoc(FormattableStringFactory.Create("You have been whitelisted by {0}.", (object) user.UILink()));
                user.Player.MsgLoc(FormattableStringFactory.Create("You have whitelisted {0}.", (object) user1.UILink()));
            }
            else if (type == UserIdType.AccountId)
            {
                user.Player.MsgLoc(FormattableStringFactory.Create("There is no existing citizen with account id '{0}'. You can try again with a username, steamid, or slgid.", (object) whitelistIdOrName));
            }
            else
            {
                WhitelistUser(whitelistIdOrName);
                user.Player.MsgLoc(FormattableStringFactory.Create("There is no existing citizen with username, steamid, or slgid '{0}'. Added '{1}' to the whitelist. If '{2}' is a username, this will not do anything.", (object) whitelistIdOrName, (object) whitelistIdOrName, (object) whitelistIdOrName));
            }
        }

        [ChatSubCommand("TwitchWhitelist", "Refreshes the whitelist from the urls in the config")]
        public static void Refresh(User user)
        {
            UpdateWhitelist(user);
        }
        
        private static void WhitelistUser(string id)
        {
            if (string.IsNullOrEmpty(id) || !Config.ManualWhiteList.AddUnique(id))
                return;
            Obj.SaveConfig();
        }
        
        private static void UpdateWhitelist(User user = null)
        {
            var preMessage = Localizer.Do(FormattableStringFactory.Create($"{LogTag} Refreshing whitelist ids..."));
            user?.Player.Msg(preMessage);
            Log.WriteLine(preMessage);
            
            var whiteList = UserManager.Config.WhiteList;
            if (Config.WhitelistUrls.Count <= 0)
            {
                var noChangeMessage = Localizer.Do(FormattableStringFactory.Create($"{LogTag} No links in the config, skipping."));
                user?.Player.Msg(noChangeMessage);
                Log.WriteLine(noChangeMessage);
                return;
            }
            
            var allIds = new HashSet<string>();
            var errored = false;
            foreach (var url in Config.WhitelistUrls.Collection)
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
                var noChange = Localizer.Do(FormattableStringFactory.Create($"{LogTag} Refreshed whitelist ids! Not change in list."));
                user?.Player.Msg(noChange);
                Log.WriteLine(noChange);
                return;
            }
            UserManager.Obj.SaveConfig();
            
            var postMessage = Localizer.Do(FormattableStringFactory.Create($"{LogTag} Refreshed whitelist ids! Old amount: {0} whitelisted, New amount: {1} whitelisted", oldListCount, whiteList.Count));
            user?.Player.Msg(postMessage);
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
                    Log.WriteErrorLine(Localizer.Do(FormattableStringFactory.Create($"{LogTag} Error when fetching whitelist: {0} {1}", url, (object) ex.Message)));
                    exception = ex;
                }
                callback(result, exception);
            });
        }
    }
}
