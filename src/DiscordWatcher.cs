﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using DSharpPlus;
using DSharpPlus.EventArgs;
using DSharpPlus.CommandsNext;
using System.Reflection;
using Vintagestory.API.Config;
using DSharpPlus.Entities;
using System.Text.RegularExpressions;
using Vintagestory.GameContent;
using vschatbot.src.Utils;
using vschatbot.src.Commands;
using Vintagestory.API.Util;
using Newtonsoft.Json;

namespace vschatbot.src
{
    public class DiscordWatcher : ModSystem
    {
        public const string PLAYERDATA_LASTSEENKEY = "VSCHATBOT_LASTSEEN";
        public const string PLAYERDATA_TOTALPLAYTIMEKEY = "VSCHATBOT_TOTALPLAYTIME";
        public const string PLAYERDATA_TOTALDEATHCOUNT = "VSCHATBOT_TOTALDEATHCOUNT";

        public static ICoreServerAPI Api;
       
        private ICoreServerAPI api
        {
            get => Api;
            set => Api = value;
        }
        private ModConfig config;
        private DiscordClient client;
        private CommandsNextModule commands;
        private DiscordChannel discordChannel;

        private TemporalStormRunTimeData lastData;
        private SystemTemporalStability temporalSystem;

        private const string CONFIGNAME = "vschatbot.json";

        public static Dictionary<string, DateTime> connectTimeDict = new Dictionary<string, DateTime>();

        public override bool ShouldLoad(EnumAppSide forSide)
        {
            return forSide == EnumAppSide.Server;
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            try
            {
                this.config = api.LoadModConfig<ModConfig>(CONFIGNAME);
            }
            catch (Exception e)
            {
                api.Server.LogError("vschatbot: Failed to load mod config!");
                return;
            }

            if (this.config == null)
            {
                api.Server.LogNotification($"vschatbot: non-existant modconfig at 'ModConfig/{CONFIGNAME}', creating default and disabling mod...");
                api.StoreModConfig(new ModConfig(), CONFIGNAME);

                return;
            }
            else if (this.config.Token == "insert bot token here" || this.config.ChannelId == default || this.config.ServerId == default)
            {
                api.Server.LogError($"vschatbot: invalid modconfig at 'ModConfig/{CONFIGNAME}'!");
                return;
            }

            this.api = api;
            Task.Run(async () => await this.MainAsync(api));
            this.api.Event.SaveGameLoaded += Event_SaveGameLoaded;
            if (this.config.RelayDiscordToGame)
                this.api.Event.PlayerChat += Event_PlayerChat;
            
            this.api.Event.PlayerDisconnect += Event_PlayerDisconnect;
            this.api.Event.PlayerNowPlaying += Event_PlayerNowPlaying;
            if (this.config.SendServerMessages)
            {
                this.api.Event.ServerRunPhase(EnumServerRunPhase.GameReady, Event_ServerStartup);
                this.api.Event.ServerRunPhase(EnumServerRunPhase.Shutdown, Event_ServerShutdown);
            }
            if (this.config.SendDeathMessages)
                this.api.Event.PlayerDeath += Event_PlayerDeath;
        }

        private void Event_PlayerNowPlaying(IServerPlayer byPlayer)
        {
            DiscordWatcher.connectTimeDict.Add(byPlayer.PlayerUID, DateTime.UtcNow);

            sendDiscordMessage($"*{byPlayer.PlayerName} " + this.config.TEXT_PlayerJoinMessage +
                $"* **({api.Server.Players.Count(x => x.ConnectionState != EnumClientState.Offline)}" +
                $"/{api.Server.Config.MaxClients})**");
        }


        //Shout-out to Milo for texts
        private void Event_PlayerDeath(IServerPlayer byPlayer, DamageSource damageSource)
        {
            var deathMessage = (byPlayer?.PlayerName ?? "Unknown player") + " ";
            if (damageSource == null)
                deathMessage += this.config.TEXT_DeathMessageUnknown;
            else
            {
                switch (damageSource.Type)
                {
                    case EnumDamageType.Gravity:
                        deathMessage += this.config.TEXT_DeathMessageGravity;
                        break;
                    case EnumDamageType.Fire:
                        deathMessage += this.config.TEXT_DeathMessageFire;
                        break;
                    case EnumDamageType.Crushing:
                    case EnumDamageType.BluntAttack:
                        deathMessage += this.config.TEXT_DeathMessageCrushing;
                        break;
                    case EnumDamageType.SlashingAttack:
                        deathMessage += this.config.TEXT_DeathMessageSlashingAttack;
                        break;
                    case EnumDamageType.PiercingAttack:
                        deathMessage += this.config.TEXT_DeathMessagePiercingAttack;
                        break;
                    case EnumDamageType.Suffocation:
                        deathMessage += this.config.TEXT_DeathMessageSuffocation;
                        break;
                    case EnumDamageType.Heal:
                        deathMessage += this.config.TEXT_DeathMessageHeal;
                        break;
                    case EnumDamageType.Poison:
                        deathMessage += this.config.TEXT_DeathMessagePoison;
                        break;
                    case EnumDamageType.Hunger:
                        deathMessage += this.config.TEXT_DeathMessageHunger;
                        break;
                    default:
                        deathMessage += this.config.TEXT_DeathMessageDefault;
                        break;
                }

                deathMessage += " ";

                switch (damageSource.Source)
                {
                    case EnumDamageSource.Block:
                        deathMessage += this.config.TEXT_DeathMessageBlock;
                        break;
                    case EnumDamageSource.Player:
                        deathMessage += this.config.TEXT_DeathMessagePVP;
                        break;
                    case EnumDamageSource.Fall:
                        deathMessage += this.config.TEXT_DeathMessageFall;
                        break;
                    case EnumDamageSource.Drown:
                        deathMessage += this.config.TEXT_DeathMessageDrown;
                        break;
                    case EnumDamageSource.Revive:
                        deathMessage += this.config.TEXT_DeathMessageRevive;
                        break;
                    case EnumDamageSource.Void:
                        deathMessage += this.config.TEXT_DeathMessageVoid;
                        break;
                    case EnumDamageSource.Suicide:
                        deathMessage += this.config.TEXT_DeathMessageSuicide;
                        break;
                    case EnumDamageSource.Internal:
                        deathMessage += this.config.TEXT_DeathMessageInternal;
                        break;
                    case EnumDamageSource.Entity:
                        switch (damageSource.SourceEntity.Code.Path)
                        {
                            case "wolf-male":
                            case "wolf-female":
                                deathMessage += this.config.TEXT_DeathMessageWolf;
                                break;
                            case "pig-wild-male":
                                deathMessage += this.config.TEXT_DeathMessagePigM;
                                break;
                            case "pig-wild-female":
                                deathMessage += this.config.TEXT_DeathMessagePigF;
                                break;
                            case "sheep-bighorn-female":
                            case "sheep-bighorn-male":
                                deathMessage += this.config.TEXT_DeathMessageBighorn;
                                break;
                            case "chicken-rooster":
                                deathMessage += this.config.TEXT_DeathMessageСhicken;
                                break;
                            case "locust":
                                deathMessage += this.config.TEXT_DeathMessageLocust;
                                break;
                            case "drifter-normal":
                                deathMessage += this.config.TEXT_DeathMessageDrifter;
                                break;
                            case "drifter-deep":
                                deathMessage += this.config.TEXT_DeathMessageDrifter;
                                break;
                            case "drifter-tainted":
                                deathMessage += this.config.TEXT_DeathMessageDrifter;
                                break;
                            case "drifter-corrupt":
                                deathMessage += this.config.TEXT_DeathMessageDrifter;
                                break;
                            case "drifter-nightmare":
                                deathMessage += this.config.TEXT_DeathMessageDrifter;
                                break;
                            case "beemob":
                                deathMessage += this.config.TEXT_DeathMessageBee;
                                break;
                            case "shintorickae":
                                deathMessage += this.config.TEXT_DeathMessageShintorickae;
                                break;
                            case "tickling":
                                deathMessage += this.config.TEXT_DeathMessageTickling;
                                break;
                            case "turtor":
                                deathMessage += this.config.TEXT_DeathMessageTurtor;
                                break;
                            case "brower":
                                deathMessage += this.config.TEXT_DeathMessageBrower;
                                break;
                            default:
                                deathMessage += this.config.TEXT_DeathMessageMob;
                                break;
                        }
                        break;
                    case EnumDamageSource.Explosion:
                        deathMessage += this.config.TEXT_DeathMessageExplosion;
                        break;
                    case EnumDamageSource.Machine:
                        deathMessage += this.config.TEXT_DeathMessageMachine;
                        break;
                    case EnumDamageSource.Unknown:
                        deathMessage += this.config.TEXT_DeathMessageUnknownS;
                        break;
                    case EnumDamageSource.Weather:
                        deathMessage += this.config.TEXT_DeathMessageWeather;
                        break;
                    default:
                        deathMessage += this.config.TEXT_DeathMessageUnknownU;
                        break;
                }
            }

            var deathCount = 1;
            IServerPlayerData data = null;
            if ((data = this.api.PlayerData.GetPlayerDataByUid(byPlayer.PlayerUID)) != null)
            {
                if(data.CustomPlayerData.TryGetValue(PLAYERDATA_TOTALDEATHCOUNT, out var totalDeathCountJson))
                {
                    deathCount += JsonConvert.DeserializeObject<int>(totalDeathCountJson);
                }

                data.CustomPlayerData[PLAYERDATA_TOTALDEATHCOUNT] = JsonConvert.SerializeObject(deathCount);
            }

            deathMessage += this.config.TEXT_PlayerDeathCountMessage + $" {deathCount}!";

            sendDiscordMessage(deathMessage);
        }

        private void Event_ServerShutdown()
        {
            sendDiscordMessage(this.config.TEXT_ServerStop);
        }

        private void Event_ServerStartup()
        {
            sendDiscordMessage(this.config.TEXT_ServerStart);
        }

        private void Event_PlayerDisconnect(IServerPlayer byPlayer)
        {
            var data = this.api.PlayerData.GetPlayerDataByUid(byPlayer.PlayerUID);
            if (data != null)
            {
                data.CustomPlayerData[PLAYERDATA_LASTSEENKEY] = JsonConvert.SerializeObject(DateTime.UtcNow);

                if( DiscordWatcher.connectTimeDict.TryGetValue(byPlayer.PlayerUID, out var connectedTime) )
                {
                    var timePlayed = DateTime.UtcNow - connectedTime;
                    if (data.CustomPlayerData.TryGetValue(PLAYERDATA_TOTALPLAYTIMEKEY, out var totalPlaytimeJson))
                        timePlayed += JsonConvert.DeserializeObject<TimeSpan>(totalPlaytimeJson);
                    data.CustomPlayerData[PLAYERDATA_TOTALPLAYTIMEKEY] = JsonConvert.SerializeObject(timePlayed);
                }
            }

            DiscordWatcher.connectTimeDict.Remove(byPlayer.PlayerUID);

            sendDiscordMessage($"*{byPlayer.PlayerName} " + this.config.TEXT_PlayerDisconnectMessage +
                $"* **({api.Server.Players.Count(x => x.PlayerUID != byPlayer.PlayerUID && x.ConnectionState == EnumClientState.Playing)}" +
                $"/{api.Server.Config.MaxClients})**");
        }

        private void sendDiscordMessage(string message = "", DiscordEmbed embed = null)
        {
            this.client.SendMessageAsync(this.discordChannel, message, embed: embed);
        }

        private async Task MainAsync(ICoreServerAPI api)
        {
            this.client = new DiscordClient(new DiscordConfiguration()
            {
                Token = this.config.Token,
                TokenType = TokenType.Bot,
                AutoReconnect = true,
                LogLevel = LogLevel.Debug
            });

            this.client.Ready += Client_Ready;
            if (this.config.RelayGameToDiscord)
                this.client.MessageCreated += Client_MessageCreated;
            this.client.ClientErrored += Client_ClientErrored;

            var commandConfiguration = new CommandsNextConfiguration
            {
                StringPrefix = "!",
                EnableMentionPrefix = true,
                EnableDefaultHelp = true
            };

            this.commands = this.client.UseCommandsNext(commandConfiguration);
            this.commands.RegisterCommands<GameCommands>();
            this.commands.RegisterCommands<DebugCommands>();

            try
            {
                await this.client.ConnectAsync();
            }
            catch (Exception)
            {
                this.api.Server.LogError("vschatbot: Failed to login using token...");
                return;
            }

            await Task.Delay(-1);
        }

        private Task Client_ClientErrored(ClientErrorEventArgs e)
        {
            api.Server.LogError("vschatbot: Disconnected from Discord...", e.Exception);
            api.Server.LogError("vschatbot: " + e.Exception.ToString());

            return Task.FromResult(true);
        }

        private void Event_SaveGameLoaded()
        {
            if (this.config.SendStormNotification && api.World.Config.GetString("temporalStorms") != "off")
            {
                temporalSystem = api.ModLoader.GetModSystem<SystemTemporalStability>();
                api.Event.RegisterGameTickListener(onTempStormTick, 5000);
            }
        }

        private void onTempStormTick(float t1)
        {
            var data = this.temporalSystem.StormData;

            if (lastData?.stormDayNotify > 1 && data.stormDayNotify == 1 && this.config.SendStormEarlyNotification)
            {
                var embed = new DiscordEmbedBuilder()
                .WithTitle(this.config.TEXT_StormEarlyWarning.Replace("{strength}", Enum.GetName(typeof(EnumTempStormStrength), data.nextStormStrength).ToLower()))
                .WithColor(DiscordColor.Yellow);

                sendDiscordMessage(embed: embed);
            }

            if (lastData?.stormDayNotify == 1 && data.stormDayNotify == 0)
            {
                var embed = new DiscordEmbedBuilder()
                .WithTitle(this.config.TEXT_StormBegin.Replace("{strength}", Enum.GetName(typeof(EnumTempStormStrength), data.nextStormStrength).ToLower()))
                .WithColor(DiscordColor.Red);

                sendDiscordMessage(embed: embed);
            }

            if (lastData?.stormDayNotify == 0 && data.stormDayNotify == -1)
            {
                var embed = new DiscordEmbedBuilder()
                .WithTitle(this.config.TEXT_StormEnd.Replace("{strength}", Enum.GetName(typeof(EnumTempStormStrength), data.nextStormStrength).ToLower()))
                .WithColor(DiscordColor.Green);

                sendDiscordMessage(embed: embed);
            }

            lastData = JsonConvert.DeserializeObject<TemporalStormRunTimeData>(JsonConvert.SerializeObject(data));
        }

        private Task Client_MessageCreated(MessageCreateEventArgs e)
        {
            if (e.Author.IsBot || e.Channel?.Id != this.discordChannel.Id || (e.Message.Content.StartsWith("!") && e.Message.Content.Length > 1))
                return Task.FromResult(true);

            var content = e.Message.Content;
            MatchCollection matches = new Regex(@"\<\@\!?(\d+)\>").Matches(content);
            try
            {
                var foundUsers = 0;

                foreach (Match match in matches)
                {
                    if (!match.Success || !ulong.TryParse(match.Groups[1].Value, out var id))
                        continue;

                    if (e.Message.MentionedUsers?.Count() > foundUsers)
                    {
                        content = content.Replace(match.Groups[0].Value, "@" + client.GetUserAsync(ulong.Parse(match.Groups[1].Value))?.ConfigureAwait(false).GetAwaiter().GetResult().Username ?? "unknown");
                        foundUsers++;
                    }
                    //else if (e.Message.MentionedChannels.Any(x => x.Id == id))
                    //    content = content.Replace(match.Groups[0].Value, "@" + client.GetChannelAsync(ulong.Parse(match.Groups[1].Value))?.ConfigureAwait(false).GetAwaiter().GetResult().Name ?? "unknown");
                    //else if (e.Message.MentionedRoles.Any(x => x.Id == id))
                    //    content = content.Replace(match.Groups[0].Value, "@" + client.GetGuildAsync(config.ServerId)?.ConfigureAwait(false).GetAwaiter().GetResult().GetRole(ulong.Parse(match.Groups[1].Value))?.Name ?? "unknown");
                }
            }
            catch (Exception)
            {
                this.api.Server.LogError($"vschatbot: Something went wrong while trying to parse the message '{e.Message.Content}'...");
                sendDiscordMessage($"Unfortunately {e.Author.Username}, " +
                    $"an internal error occured while handling your message... (Blame {this.api.World.AllOnlinePlayers?.RandomElement()?.PlayerName ?? "Capsup"} or something)");
                return Task.FromResult(true);
            }

            var customEmojiMatches = new Regex(@"\<(\:.+\:)\d+\>").Matches(content);
            foreach (Match match in customEmojiMatches)
            {
                if (!match.Success)
                    continue;

                content = content.Replace(match.Groups[0].Value, match.Groups[1].Value);
            }

            api.SendMessageToGroup(GlobalConstants.GeneralChatGroup, $"<font color='blue'>[Disc]</color><strong>{e.Author.Username}</strong>» {content.Replace(" > ", "&gt;").Replace("<", "&lt;")}", EnumChatType.OthersMessage);

            return Task.FromResult(true);
        }

        private Task Client_Ready(ReadyEventArgs e)
        {
            this.api.Server.LogNotification("vschatbot: connected to discord and ready!");

            this.discordChannel = this.client.GetChannelAsync(this.config.ChannelId).ConfigureAwait(false).GetAwaiter().GetResult();
            return Task.FromResult(true);
        }

        private void Event_PlayerChat(IServerPlayer byPlayer, int channelId, ref string message, ref string data, Vintagestory.API.Datastructures.BoolRef consumed)
        {
            if (channelId == GlobalConstants.GeneralChatGroup)
            {
                var foundText = new Regex(@".*?> (.+)$").Match(message);
                if (!foundText.Success)
                    return;

                sendDiscordMessage($"**{byPlayer.PlayerName}**: {foundText.Groups[1].Value}");
            }
        }
    } 

}
