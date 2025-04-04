using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Alduin.Core.Models.AI;
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
        private string? _streamId;

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

            //IA vai começar a conversa
            var initialConversationItem = new
            {
                type = "conversation.item.create",
                item = new
                {
                    tools = new[]
                    {
                        new
                        {
                            type = "function",
                            name = "consulta_via_cep",
                            description = "Consulta informações de endereço usando o ViaCEP com base no CEP fornecido.",
                            parameters = new
                            {
                                type = "object",
                                properties = new
                                {
                                    cep = new
                                    {
                                        type = "string",
                                        description = "O CEP a ser consultado, no formato '12345678'."
                                    }
                                },
                                required = new[] { "cep" }
                            }
                        },
                    },
                    type = "message",
                    role = "user",
                    content = new[]
                    {
                        new
                        {
                            type = "input_text",
                            text = _operatorPrompt
                        }
                    }
                }
            };

            await SendWebSocketMessage(openAiWebSocket, initialConversationItem);
            await SendWebSocketMessage(openAiWebSocket, new { type = "response.create" });
        }

        private async Task HandleOpenAIWebSocket(ClientWebSocket openAiWebSocket, WebSocket clientWebSocket)
        {
            var buffer = new byte[8192 * 2];
            while (openAiWebSocket.State == WebSocketState.Open)
            {
                var result = await openAiWebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);

                Console.WriteLine("EVENTO: {0}, {1}", JsonSerializer.Serialize(result), message);

                try
                {
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await CloseWebSocket(openAiWebSocket, "API");
                        break;
                    }

                    if (clientWebSocket.State != WebSocketState.Open)
                    {
                        await CloseWebSocket(openAiWebSocket, "Open AI");
                        break;
                    }

                    if (!TryParseJson(message, out var document) || document == null)
                        continue;

                    var root = document.RootElement;
                    var eventType = root.GetProperty("type").GetString();

                    if (eventType == "response.audio.delta" && !string.IsNullOrWhiteSpace(_streamId))
                    {
                        var delta = root.GetProperty("delta").GetString();
                        var audioDelta = new
                        {
                            @event = "media",
                            streamSid = _streamId,
                            media = new
                            {
                                payload = delta
                            }
                        };

                        await SendWebSocketMessage(clientWebSocket, audioDelta);
                    }

                    if (eventType == "response.done")
                    {
                        var outputObjects = root.GetProperty("response").GetProperty("output");

                        if (outputObjects.GetArrayLength() <= 0)
                            continue;

                        var outputObject = outputObjects[0];
                        var type = outputObject.GetProperty("type").GetString();

                        if (type == "function_call" && outputObject.GetProperty("name").GetString() == "consulta_via_cep")
                        {
                            var callId = outputObject.GetProperty("call_id").GetString();
                            Console.WriteLine("Chamando CEP");
                            var arguments = JsonDocument.Parse(outputObject.GetProperty("arguments").GetString());
                            var resultCep = await ConsultarViaCepAsync(arguments.RootElement.GetProperty("cep").GetString());
                            Console.WriteLine("Resultado do CEP: " + resultCep);

                            var conversationItem = new
                            {
                                type = "conversation.item.create",
                                item = new
                                {
                                    type = "function_call_output",
                                    call_id = callId,
                                    output = resultCep
                                }
                            };

                            await SendWebSocketMessage(openAiWebSocket, conversationItem);
                            await SendWebSocketMessage(openAiWebSocket, new { type = "response.create" });
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
            var buffer = new byte[8192 * 2];
            while (clientWebSocket.State == WebSocketState.Open)
            {
                var result = await clientWebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);

                try
                {
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await CloseWebSocket(openAiWebSocket, "Open AI");
                        break;
                    }

                    if (openAiWebSocket.State != WebSocketState.Open)
                    {
                        await CloseWebSocket(clientWebSocket, "API");
                        break;
                    }

                    if (!TryParseJson(message, out var document) || document == null)
                        continue;

                    var root = document.RootElement;
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

        private bool TryParseJson(string json, out JsonDocument? document)
        {
            try
            {
                document = JsonDocument.Parse(json);
                return true;
            }
            catch
            {
                document = null;
                return false;
            }
        }

        private async Task CloseWebSocket(WebSocket websocket, string webSocketName = "Default")
        {
            Console.WriteLine("Closing WebSocket {0}", webSocketName);

            if (websocket.State == WebSocketState.Open)
                await websocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
        }

        public async Task<string> ConsultarViaCepAsync(string cep)
        {
            using (var httpClient = new HttpClient())
            {
                var response = await httpClient.GetAsync($"https://viacep.com.br/ws/{cep}/json/");
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }
                else
                {
                    return $"Erro ao consultar o ViaCEP: {response.StatusCode}";
                }
            }
        }
    }
}
