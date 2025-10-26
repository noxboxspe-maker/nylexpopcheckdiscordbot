using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Collections.Generic;
using Discord;
using Discord.WebSocket;

public class Program
{
    private const string DiscordBotToken = "[discordtoken]";
    private const ulong TargetChannelId = [DISCORDCHANNELID]; 

    private const string ApiUrl = "https://api.steampowered.com/IGameServersService/GetServerList/v1/?key=[STEAMAPIKEY]&filter=addr\\[SERVERADDR:[SERVERPORT]";

    private readonly DiscordSocketClient _client;

    private ulong? _statusMessageId;

    private const int UpdateIntervalSeconds = 2; // [UPDATEINTERVAL], 0 will get you rate limited, 1 is alright if you can restart it every now and than

    public static Task Main(string[] args) => new Program().MainAsync();

    public Program()
    {
        var config = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.AllUnprivileged
        };

        _client = new DiscordSocketClient(config);
        _client.Log += Log;
    }

    public async Task MainAsync()
    {
        await _client.LoginAsync(TokenType.Bot, DiscordBotToken);
        await _client.StartAsync();
        _client.Ready += StartUpdateLoop;
        await Task.Delay(Timeout.Infinite);
    }

    private Task Log(LogMessage msg)
    {
        Console.WriteLine(msg.ToString());
        return Task.CompletedTask;
    }

    private async Task StartUpdateLoop()
    {
        Console.WriteLine($"Bot is ready. Starting player count update loop (every {UpdateIntervalSeconds}s)...");
        _client.Ready -= StartUpdateLoop;
        _ = Task.Run(UpdateLoop);
    }

    private async Task UpdateLoop()
    {
        while (true)
        {
            var (embed, content) = await GetServerStatusEmbed();

            if (_client.GetChannel(TargetChannelId) is ITextChannel channel)
            {
                try
                {
                    if (!_statusMessageId.HasValue)
                    {
                        IUserMessage initialMessage = await channel.SendMessageAsync(
                            text: content,
                            embed: embed 
                        );
                        _statusMessageId = initialMessage.Id;
                        Console.WriteLine($"Posted initial status message: {_statusMessageId}");
                    }
                    else
                    {
                        IUserMessage messageToEdit = (IUserMessage)await channel.GetMessageAsync(_statusMessageId.Value);

                        if (messageToEdit != null)
                        {
                            await messageToEdit.ModifyAsync(msg =>
                            {
                                msg.Content = content;
                                msg.Embed = embed;
                            });
                            Console.WriteLine($"Updated message {_statusMessageId.Value}: Status updated.");
                        }
                        else
                        {
                            Console.WriteLine($"Message {_statusMessageId.Value} not found. Will post a new one next loop.");
                            _statusMessageId = null;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error updating/posting message: {ex.Message}");
                    _statusMessageId = null;
                }
            }
            else
            {
                Console.WriteLine($"Error: Could not find text channel with ID {TargetChannelId}.");
            }

            await Task.Delay(TimeSpan.FromSeconds(UpdateIntervalSeconds));
        }
    }

    private async Task<(Embed, string)> GetServerStatusEmbed()
    {
        try
        {
            using (HttpClient client = new HttpClient())
            {
                HttpResponseMessage response = await client.GetAsync(ApiUrl);
                response.EnsureSuccessStatusCode();

                string jsonContent = await response.Content.ReadAsStringAsync();
                var serverResponse = JsonSerializer.Deserialize<ServerResponseRoot>(jsonContent);
                var servers = serverResponse?.Response?.Servers;

                if (servers != null && servers.Count > 0)
                {
                    var server = servers[0];

                    var embed = new EmbedBuilder()
                        .WithTitle($"🎮 {server.Name}")
                        .WithColor(server.Players > 0 ? Color.Green : Color.LightGrey)
                        .WithDescription($"**Status:** {(server.Players > 0 ? "Online" : "Offline")}")
                        .AddField("Players", $"{server.Players} / {server.MaxPlayers}", true)
                        .AddField("Address", server.Address, true)
                        .AddField("Secure", server.Secure ? "✅ Yes" : "❌ No", true)
                        .WithFooter($"Last Updated: {DateTime.UtcNow.ToString("HH:mm:ss")} UTC")
                        .WithTimestamp(DateTimeOffset.UtcNow)
                        .Build();

                    return (embed, $"Current Player Count: **{server.Players}**");
                }
                else
                {
                    var embed = new EmbedBuilder()
                        .WithTitle("⚠️ Server Not Found")
                        .WithColor(Color.Orange)
                        .WithDescription("The API returned no server results.")
                        .WithTimestamp(DateTimeOffset.UtcNow)
                        .Build();

                    return (embed, "⚠️ Server status unavailable.");
                }
            }
        }
        catch (Exception ex)
        {
            var embed = new EmbedBuilder()
                .WithTitle("❌ API Error")
                .WithColor(Color.Red)
                .WithDescription($"Failed to retrieve server data. Details: {ex.Message.Substring(0, Math.Min(200, ex.Message.Length))}")
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();

            return (embed, "❌ Status update failed.");
        }
    }
}


public class ServerResponseRoot
{
    [JsonPropertyName("response")]
    public ServerListContainer? Response { get; set; }
}

public class ServerListContainer
{
    [JsonPropertyName("servers")]
    public List<ServerInfo>? Servers { get; set; }
}

public class ServerInfo
{
    [JsonPropertyName("addr")]
    public string Address { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("players")]
    public int Players { get; set; }

    [JsonPropertyName("max_players")]
    public int MaxPlayers { get; set; }

    [JsonPropertyName("secure")]
    public bool Secure { get; set; }
}
