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
    // WARNING: Replace with your actual Bot Token and the ID of the channel you want to post in
    private const string DiscordBotToken = "[discordtoken]";
    private const ulong TargetChannelId = 1431880175636844644; // Replace with your Discord Channel ID

    // 1. Define the Steam API URL
    private const string ApiUrl = "https://api.steampowered.com/IGameServersService/GetServerList/v1/?key=[STEAMAPIKEY]&filter=addr\\[SERVERADDR:[SERVERPORT]";

    private readonly DiscordSocketClient _client;

    // Field to store the ID of the message we will update.
    private ulong? _statusMessageId;

    // Define the new update interval (3 seconds)
    private const int UpdateIntervalSeconds = 2;

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
            // 2. Get the new Embed object and content (to be used if posting a new message)
            var (embed, content) = await GetServerStatusEmbed();

            if (_client.GetChannel(TargetChannelId) is ITextChannel channel)
            {
                try
                {
                    if (!_statusMessageId.HasValue)
                    {
                        // 3. FIRST RUN: Post the initial message using the Embed
                        IUserMessage initialMessage = await channel.SendMessageAsync(
                            text: content, // Use this for a clean, short status message or error.
                            embed: embed // Use the rich Embed for the main data.
                        );
                        _statusMessageId = initialMessage.Id;
                        Console.WriteLine($"Posted initial status message: {_statusMessageId}");
                    }
                    else
                    {
                        // 4. SUBSEQUENT RUNS: Edit the existing message
                        IUserMessage messageToEdit = (IUserMessage)await channel.GetMessageAsync(_statusMessageId.Value);

                        if (messageToEdit != null)
                        {
                            // Modify the existing message with the new Embed and text
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

            // 5. Wait for the faster interval (3 seconds)
            await Task.Delay(TimeSpan.FromSeconds(UpdateIntervalSeconds));
        }
    }

    // 6. Updated API method to return an Embed and a simple content string
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

                    // Create a rich Discord Embed
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

                    // Return the rich embed and a simple text status
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

// --- JSON Deserialization Models (UNCHANGED) ---
// (The model definitions below are unchanged and are required for the code above to compile)

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
