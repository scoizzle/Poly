﻿using System;
using System.Collections.Generic;
using System.Text;

using Poly;
using Poly.Data;
using Poly.Net.Tcp;

namespace Poly.Net.Irc {
    public class Packet : jsObject {
        public static Dictionary<string, string> Formats = new Dictionary<string, string>() {
            { "Pong", "PONG :{Message}" },
            { "User", "USER {Ident} {Visible} * :{Message}" },
            { "Nick", "NICK {Message}" },
            { "Pass", "PASS :{Message}" },
            { "Mode", "MODE {Receiver}[ {Message}]" },
            { "CTCP", "PRIVMSG {Receiver} :\x0001{Message}\x0001" },
            { "CTCPReply", "NOTICE {Receiver} :\x0001{Message}\x0001" },
            { "Msg", "PRIVMSG {Receiver} :{Message}" },
            { "Notice", "NOTICE {Receiver} :{Message}" },
            { "Join", "JOIN {Receiver}[ {Message}]" },
            { "Part", "PART {Receiver} :{Message}" },
            { "Error", "ERROR :{Message}" },
            { "Quit", "QUIT :{Message}" },
            { "Topic", "TOPIC {Receiver} :{Message}" },
            { "Invite", "INVITE {Sender} {Receiver}" },
            { "Kick", "KICK {Sender} {Receiver} :{Message}" },
            { "ListUsers", "LLUSERS" },
            { "Motd", "MOTD" },
            { "Version", "VERSION" },
            { "Stats", "STATS {Receiver}" },
            { "Links", "LINKS {Receiver}" },
            { "Time", "TIME" },
            { "Trace", "TRACE" },
            { "Admin", "ADMIN" },
            { "Info", "INFO" },
            { "Oper", "OPER {Sender} {Message}" },
            { "Names", "NAMES {Receiver}" },
            { "List", "LIST {Receiver}" },
            { "Who", "WHO {Receiver}" },
            { "Whois", "WHOIS {Receiver}" },
            { "Whowas", "WHOWAS {Receiver}" },
            { "OnPing", "PING :{Message}" },
            { "OnQuit", ":{Sender} QUIT :{Message}" },
            { "OnCTCPPing", ":{Sender} PRIVMSG {Receiver} :\x0001PING {Message}\x0001" },
            { "OnCTCPVersion", ":{Sender} PRIVMSG {Receiver} :\x0001VERSION\x0001" },
            { "OnCTCPTime", ":{Sender} PRIVMSG {Receiver} :\x0001TIME\x0001" },
            { "OnCTCP", ":{Sender} PRIVMSG {Receiver} :\x0001{Message}\x0001" },
            { "OnMsg", ":{Sender} PRIVMSG {Receiver} :{Message}" },
            { "OnCTCPReply", ":{Sender} NOTICE {Receiver} :\x0001{Message}\x0001" },
            { "OnNotice", ":{Sender} NOTICE {Receiver} :{Message}" },
            { "OnChannelMode", ":{Sender} MODE {Channel} {Message} {Receiver}" },
            { "OnMode", ":{Sender} MODE {Receiver} :{Message}" },
            { "OnJoin", ":{Sender} JOIN :{Receiver}" },
            { "OnPart", ":{Sender} PART {Receiver}" },
            { "OnNick", ":{Sender} NICK :{Receiver}" },
            { "OnTopic", ":{Sender} TOPIC {Receiver} :{Message}" },
            { "OnConnected", ":{Sender} 001 {Receiver} :{Message}" },
            { "OnHostInfo", ":{Sender} 002 {Receiver} :{Message}" },
            { "OnServerInfo", ":{Sender} 003 {Receiver} :{Message}" },
            { "OnServerMoreInfo", ":{Sender} 004 {Receiver} {Message}" },
            { "OnServerFeatures", ":{Sender} 005 {Receiver} {Features} :{Message}" },
            { "OnServerRecords", ":{Sender} 250 {Receiver} :{Message}" },
            { "OnNetworkUsersStats", ":{Sender} 251 {Receiver} :{Message}" },
            { "OnNetworkOpersCount", ":{Sender} 252 {Receiver} {Count} :{Message}" },
            { "OnUnknownConnectionCount", ":{Sender} 253 {Receiver} {Count} :{Message}" }, 
            { "OnNetworkChannelsCount", ":{Sender} 254 {Receiver} {Count} :{Message}" },
            { "OnServerClientsInfo", ":{Sender} 255 {Receiver} :{Message}" },
            { "OnServerUsersCount", ":{Sender} 265 {Receiver} :{Message}" },
            { "OnNetworkUsersCount", ":{Sender} 266 {Receiver} :{Message}" },
            { "OnWhoEnd", ":{Sender} 315 {Receiver} {Nick} :{Message}" },
            { "OnListStart", ":{Sender} 321 {Receiver} :{Message}" },
            { "OnList", ":{Sender} 322 {Receiver} :{Message}" },
            { "OnListEnd", ":{Sender} 323 {Receiver} :{Message}" },
            { "OnChannelModeIs", ":{Sender} 342 {Receiver} {Message}" },
            { "OnTopicEmpty", ":{Sender} 331 {Nick} {Receiver} :{Message}" },
            { "OnTopicInit", ":{Sender} 332 {Nick} {Receiver} :{Message}" },
            { "OnTopicInfo", ":{Sender} 333 {Nick} {Receiver} {Author} {Message}" },
            { "OnWho", ":{Sender} 352 {Receiver} {Channel} {Ident} {Host} {Server} {Nick} {Modes} :{HopCount} {RealName}" },
            { "OnChannelUserList", ":{Sender} 353 {Receiver} = {Channel} :{Message}" },
            { "OnNAMESEnd", ":{Sender} 366 {Receiver} {Channel} :{Message}" },
            { "OnMOTDStart", ":{Sender} 375 {Receiver} :{Message}" },
            { "OnMOTD", ":{Sender} 372 {Receiver} :{Message}" },
            { "OnMOTDEnd", ":{Sender} 376 {Receiver} :{Message}" },
            { "OnNewHostMask", ":{Sender} 396 {Receiver} {Host} :{Message}" },
            { "OnNoSuchNick", ":{Sender} 401 {Receiver} :{Message}" },
            { "OnNoSuchServer", ":{Sender} 402 {Receiver} :{Message}" },
            { "OnNoSuchChannel", ":{Sender} 403 {Receiver} :{Message}" },
            { "OnCannotSendToChannel", ":{Sender} 404 {Receiver} :{Message}" },
            { "OnTooManyChannels", ":{Sender} 405 {Receiver} :{Message}" },
            { "OnWasNoSuchNick", ":{Sender} 406 {Receiver} :{Message}" },
            { "OnTooManyTargets", ":{Sender} 407 {Receiver} :{Message}" },
            { "OnNoOrigin", ":{Sender} 409 {Receiver} :{Message}" },
            { "OnNoReceiver", ":{Sender} 411 {Receiver} :{Message}" },
            { "OnNoTextToSend", ":{Sender} 412 {Receiver} :{Message}" },
            { "OnNoTopLevel", ":{Sender} 413 {Receiver} :{Message}" },
            { "OnWildTopLevel", ":{Sender} 414 {Receiver} :{Message}" },
            { "OnUnknownCommand", ":{Sender} 421 {Receiver} :{Message}" },
            { "OnNoMOTD", ":{Sender} 422 {Receiver} :{Message}" },
            { "OnNoAdminInfo", ":{Sender} 423 {Receiver} :{Message}" },
            { "OnFileError", ":{Sender} 424 {Receiver} :{Message}" },
            { "OnNoNickGiven", ":{Sender} 431 {Receiver} :{Message}" },
            { "OnInvalidNick", ":{Sender} 432 {Receiver} :{Message}" },
            { "OnNickInUse", ":{Sender} 433 {Receiver} {Nick} :{Message}" },
            { "OnNickCollision", ":{Sender} 436 {Receiver} :{Message}" },
            { "OnUserNotInChannel", ":{Sender} 441 {Receiver} :{Message}" },
            { "OnNotOnChannel", ":{Sender} 442 {Receiver} :{Message}" },
            { "OnUserOnChannel", ":{Sender} 443 {Receiver} :{Message}" },
            { "OnNoLogin", ":{Sender} 444 {Receiver} :{Message}" },
            { "OnSummonDisabled", ":{Sender} 445 {Receiver} :{Message}" },
            { "OnUsersDisabled", ":{Sender} 446 {Receiver} :{Message}" },
            { "OnNotRegistered", ":{Sender} 451 {Receiver} :{Message}" },
            { "OnNeedMoreParams", ":{Sender} 461 {Receiver} :{Message}" },
            { "OnAlreadyRegistered", ":{Sender} 462 {Receiver} :{Message}" },
            { "OnNoPermissionForHost", ":{Sender} 463 {Receiver} :{Message}" },
            { "OnInvalidPass", ":{Sender} 464 {Receiver} :{Message}" },
            { "OnBanned", ":{Sender} 465 {Receiver} :{Message}" },
            { "OnKeyAlreadySet", ":{Sender} 467 {Receiver} :{Message}" },
            { "OnChannelFull", ":{Sender} 471 {Receiver} :{Message}" },
            { "OnUnknownMode", ":{Sender} 472 {Receiver} :{Message}" },
            { "OnChannelInviteOnly", ":{Sender} 473 {Receiver} :{Message}" },
            { "OnBannedFromChannel", ":{Sender} 474 {Receiver} :{Message}" },
            { "OnBadChannelKey", ":{Sender} 475 {Receiver} :{Message}" },
            { "OnNoPermission", ":{Sender} 481 {Receiver} :{Message}" },
            { "OnChannelOpNeeded", ":{Sender} 482 {Receiver} :{Message}" },
            { "OnCantKillServer", ":{Sender} 483 {Receiver} :{Message}" },
            { "OnNoOperHost", ":{Sender} 491 {Receiver} :{Message}" },
            { "OnUnknownModeFlag", ":{Sender} 501 {Receiver} :{Message}" },
            { "OnUsersDontMatch", ":{Sender} 502 {Receiver} :{Message}" },
        };

        public Packet() { }

        public Packet(string TypeName) {
            this.Format = Formats[TypeName];
        }

        public string Format = string.Empty;

        public string Sender {
            get {
                return Get<string>("Sender", string.Empty);
            }
            set {
                Set("Sender", value);
            }
        }

        public string Receiver {
            get {
                return Get<string>("Receiver", string.Empty);
            }
            set {
                Set("Receiver", value);
            }
        }

        public string Action {
            get {
                return Get<string>("Action", string.Empty);
            }
            set {
                Set("Action", value);
            }
        }

        public string Message {
            get {
                return Get<string>("Message", string.Empty);
            }
            set {
                Set("Message", value);
            }
        }

        public void Send(Poly.Net.Tcp.Client Client) {
			if (Client != null) {
            	Client.SendLine(this.Template(Format));
			}
        }

        public bool Receive(Poly.Net.Tcp.Client Client) {
            string Data = Client.Receive();

            if (Data == null)
                return false;

            foreach (var Pair in Formats) {
                if (Data.Match(Pair.Value, false, this) != null) {
                    this.Format = Pair.Value;
                    this.Action = Pair.Key;
                    return true;
                }
            }

            this.Message = Data;
            return false;
        }

        public static implicit operator Packet(string Name) {
            return new Packet(Name);
        }

        public override string ToString(bool humanformat) {
            return this.Template(Format);
        }
    }
}