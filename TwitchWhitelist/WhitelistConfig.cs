﻿using Eco.Core.Utils;
using Eco.Shared.Localization;

namespace TwitchWhitelist
{
    [Localized]
    public class WhitelistConfig
    {
        
        [LocDescription("Links to one or more whitelists containing SLG ID or STEAMID64 ids separated with a newline.")]
        public SerializedSynchronizedCollection<string> WhitelistUrls { get; set; } = new SerializedSynchronizedCollection<string>();
        
        [LocDescription("How often the whitelist should be refreshed in seconds")]
        public int RefreshIntervalSeconds { get; set; } = 300;
        
        [LocDescription("Users on the whitelist do not need to enter a password to connect to a passworded server.  Use either SLG ID or STEAMID64. This is for adding ids not in the links")]
        public SerializedSynchronizedCollection<string> ManualWhiteList { get; set; } = new SerializedSynchronizedCollection<string>();
    }
}