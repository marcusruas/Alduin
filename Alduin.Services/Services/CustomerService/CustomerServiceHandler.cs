using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Alduin.Core.Models.Configs;
using static System.Collections.Specialized.BitVector32;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Alduin.Core.Services.CustomerService
{
    public class CustomerServiceHandler : ICustomerServiceHandler
    {
        private readonly GeneralSettings _settings;

        public CustomerServiceHandler(GeneralSettings settings)
        {
            _settings = settings;
            _operatorPrompt = GetOperatorPrompt();
        }

        private readonly string _operatorPrompt;
        private string _streamId;

        public async Task HandleWebSocket(WebSocket clientWebSocket)
        {
            using (var openAiWebSocket = new ClientWebSocket())
            {
                var wsUri = new Uri(_settings.OpenAISettings.RealtimeWebSocketUrl);

                openAiWebSocket.Options.SetRequestHeader("Authorization", $"Bearer {_settings.OpenAISettings.ApiKey}");
                openAiWebSocket.Options.SetRequestHeader("OpenAI-Beta", "realtime=v1");
                await openAiWebSocket.ConnectAsync(wsUri, CancellationToken.None);

                Console.WriteLine("Conexão com Web Socket da Open AI estabelecida.");

                await InitializeOpenAISession(openAiWebSocket);

                Console.WriteLine("Sessão inicializada. Processando eventos dos dois web sockets");

                var wsHandlers = new Task[]
                {
                    HandleOpenAIWebSocket(openAiWebSocket, clientWebSocket),
                    HandleClientWebSocket(clientWebSocket, openAiWebSocket)
                };
                
                await Task.WhenAll(wsHandlers);
            }
        }

        private async Task InitializeOpenAISession(ClientWebSocket openAiWebSocket)
        {
            var sessionUpdate = new
            {
                type = "session.update",
                session = new
                {
                    turn_detection = new { type = "server_vad" },
                    input_audio_format = "g711_ulaw",
                    output_audio_format = "g711_ulaw",
                    voice = "echo",
                    instructions = _operatorPrompt,
                    modalities = new[] { "text", "audio" },
                    temperature = 0.8
                }
            };

            await SendWebSocketMessage(openAiWebSocket, sessionUpdate);
        }

        private async Task HandleOpenAIWebSocket(ClientWebSocket openAiWebSocket, WebSocket clientWebSocket)
        {
            var buffer = new byte[4096];
            while (openAiWebSocket.State == WebSocketState.Open)
            {
                //Console.WriteLine("\n[OPEN AI] Recebido um novo evento.");

                var result = await openAiWebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);

                try
                {
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Console.WriteLine("[OPEN AI] [AVISO] Web Socket da OpenAI fechou.");
                        await openAiWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                        break;
                    }
                    else if (string.IsNullOrWhiteSpace(message) || !message.StartsWith("{") || !message.EndsWith("}"))
                    {
                        Console.WriteLine("[OPEN AI] [AVISO] Mensagem está vazia.\n");
                    }
                    else if (clientWebSocket.State != WebSocketState.Open)
                    {
                        Console.WriteLine("[OPEN AI] [AVISO] Web Socket do Client fechou.\n");
                        await openAiWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                        break;
                    }
                    else
                    {
                        //Console.WriteLine($"[OPEN AI] Evento: {message}\n");

                        using (var doc = JsonDocument.Parse(message))
                        {
                            var root = doc.RootElement;
                            var eventType = root.GetProperty("type").GetString();

                            Console.WriteLine("[OPEN AI] Tipo de evento: " + eventType);

                            if (eventType == "response.audio.delta")
                            {
                                if (!string.IsNullOrWhiteSpace(_streamId))
                                {
                                    var delta = root.GetProperty("delta").GetString();
                                    var mediaEvent = new MediaEvent
                                    {
                                        StreamSid = _streamId,
                                        Media = new MediaPayload
                                        {
                                            Payload = delta
                                        }
                                    };

                                    await SendWebSocketMessage(clientWebSocket, mediaEvent);
                                }
                            }
                        }
                    }
                }
                catch(Exception ex)
                {
                    Console.WriteLine("Falha na Open AI: " + ex);
                }
            }
        }

        private async Task HandleClientWebSocket(WebSocket clientWebSocket, ClientWebSocket openAiWebSocket)
        {
            var buffer = new byte[4096];
            while (clientWebSocket.State == WebSocketState.Open)
            {
                //Console.WriteLine("\n[CLIENT] Recebido um novo evento.");

                var result = await clientWebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);

                try
                {
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Console.WriteLine("[CLIENT] [AVISO] Web Socket da API fechou.\n");
                        await openAiWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                        break;
                    }
                    else if (string.IsNullOrWhiteSpace(message))
                    {
                        Console.WriteLine("[CLIENT] [AVISO] Mensagem está vazia.\n");
                    }
                    else if (openAiWebSocket.State != WebSocketState.Open)
                    {
                        Console.WriteLine("[CLIENT] [AVISO] Web Socket da Open AI fechou.\n");
                        await clientWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                        break;
                    }
                    else
                    {
                        //Console.WriteLine($"[CLIENT] Evento: {message}\n");

                        using (var doc = JsonDocument.Parse(message))
                        {
                            var root = doc.RootElement;
                            var eventType = root.GetProperty("event").GetString();

                            if (eventType == "start")
                            {
                                _streamId = root.GetProperty("streamSid").GetString();
                            }
                            else if (eventType == "media")
                            {
                                var base64Audio = root.GetProperty("media").GetProperty("payload").GetString();
                                var audio = new
                                {
                                    type = "input_audio_buffer.append",
                                    audio = base64Audio
                                };
                                await SendWebSocketMessage(openAiWebSocket, audio);
                            }
                        }
                    }
                }
                catch(Exception ex)
                {
                    Console.WriteLine("Falha no Client: " + ex);
                }
                
            }
        }

        private string GetOperatorPrompt()
        {
            var promptsFolder = Path.Combine(AppContext.BaseDirectory, "Prompts");
            string promptPath = Path.Combine(promptsFolder, "OperatorPrompt.txt");

            if (!File.Exists(promptPath))
                throw new Exception("Prompt not found");

            var operatorPrompt = File.ReadAllLines(promptPath, Encoding.UTF8);
            return string.Join(' ', operatorPrompt.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        private async Task SendWebSocketMessage(WebSocket websocket, object message)
        {
            string json = JsonSerializer.Serialize(message);
            var bytes = Encoding.UTF8.GetBytes(json);

            await websocket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken: CancellationToken.None
            );
        }
    }

    public class MediaEvent
    {
        [JsonPropertyName("event")]
        public string Event => "media";

        [JsonPropertyName("streamSid")]
        public string StreamSid { get; set; }

        [JsonPropertyName("media")]
        public MediaPayload Media { get; set; }
    }

    public class MediaPayload
    {
        [JsonPropertyName("payload")]
        public string Payload { get; set; }
    }
}
