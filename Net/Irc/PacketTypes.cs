namespace Poly.Net.Irc {
    public partial class Packet {
        public const string Ping = "PING",
                            Pong = "PONG",
                            User = "USER",
                            Pass = "PASS",
                            Nick = "NICK",
                            Msg = "PRIVMSG",
                            Notice = "NOTICE",
                            Join = "JOIN",
                            Part = "PART",
                            Mode = "MODE",
                            Error = "ERROR",
                            Quit = "QUIT",
                            Topic = "TOPIC",
                            Invite = "INVITE",
                            Kick = "KICK",
                            ListUsers = "LLUSERS",
                            Motd = "MOTD",
                            Version = "VERSION",
                            Stats = "STATS",
                            Links = "LINKS",
                            IsOn = "ISON",
                            Time = "TIME",
                            Trace = "TRACE",
                            Admin = "ADMIN",
                            Info = "INFO",
                            Oper = "OPER",
                            Names = "NAMES",
                            List = "LIST",
                            Who = "WHO",
                            Whois = "WHOIS",
                            Whowas = "WHOWAS";

        public enum Reply : int {
            OnConnected = 1,
            OnHostInfo,
            OnServerInfo,
            OnServerMoreInfo,
            OnServerFeatures,

            OnModeIs = 221,

            OnServerRecords = 250,
            OnNetworkUsersStats,
            OnNetworkOpersCount,
            OnUnknownConnectionCount,
            OnNetworkChannelsCount,
            OnServerClientsInfo,

            OnServerUsersCount = 265,
            OnNetworkUsersCount,

            OnISON = 303,

            OnWhoEnd = 315,
            OnListStart = 321,
            OnList,
            OnListEnd,

            OnTopicEmpty = 331,
            OnTopicInit,
            OnTopicInfo,

            OnChannelModeIs = 342,

            OnWho = 352,
            OnChannelUserList,

            OnNAMESEnd = 366,

            OnMOTD = 372,
            OnMOTDStart = 375,
            OnMOTDEnd,

            OnNewHostMask = 396,

            OnNoSuchNick = 401,

            OnNoSuchServer,
            OnNoSuchChannel,
            OnCannotSendToChannel,
            OnTooManyChannels,
            OnWasNoSuchNick,
            OnTooManyTargets,

            OnNoOrigin = 409,

            OnNoReceiver = 411,
            OnNoTextToSend,
            OnNoTopLevel,
            OnWildTopLevel,

            OnUnknownCommand = 421,
            OnNoMOTD,
            OnNoAdminInfo,
            OnFileError,

            OnNoNickGiven = 431,
            OnInvalidNick,
            OnNickInUse,

            OnNickCollision = 436,

            OnUserNotInChannel = 441,
            OnNotOnChannel,
            OnUserOnChannel,
            OnNoLogin,
            OnSummonDisabled,
            OnUsersDisabled,
            OnNotRegistered = 451,
            OnNeedMoreParams = 461,
            OnAlreadyRegistered,
            OnNoPermissionForHost,
            OnInvalidPass,
            OnBanned,
            OnKeyAlreadySet = 467,
            OnChannelFull = 471,
            OnUnknownMode,
            OnChannelInviteOnly,
            OnBannedFromChannel,
            OnBadChannelKey,
            OnNoPermission = 481,
            OnChannelOpNeeded,
            OnCantKillServer,
            OnNoOpHost = 491,
            OnUnknownModeFlag = 501,
            OnUsersDontMatch
        };
    }
}
