using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Alduin.Core.Helpers;
using Alduin.Core.Models.AI;
using Alduin.Core.Models.Configs;
using Alduin.Core.Models.Configs.OpenAI;
using static Alduin.Core.Helpers.JsonDocumentHelper;

namespace Alduin.Core.Services.CustomerService
{
    public class CustomerServiceHandler : ICustomerServiceHandler
    {
        private IHttpClientFactory _httpClientFactory;
        private readonly GeneralSettings _settings;

        public CustomerServiceHandler(IHttpClientFactory httpClientFactory, GeneralSettings settings)
        {
            _httpClientFactory = httpClientFactory;
            _settings = settings;
        }

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
            await SendWebSocketMessage(openAiWebSocket, _settings.OpenAISettings.Events.SessionUpdate);
            await SendWebSocketMessage(openAiWebSocket, _settings.OpenAISettings.Events.StartConversation, true);
        }

        private async Task HandleOpenAIWebSocket(ClientWebSocket openAiWebSocket, WebSocket clientWebSocket)
        {
            var buffer = new byte[8192 * 2];
            while (openAiWebSocket.State == WebSocketState.Open)
            {
                var result = await openAiWebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);

                Console.WriteLine(message);

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

                    if (!message.TryParseToJson(out var root) || !root.HasValue)
                        continue;

                    var eventType = root.GetStringProperty("type");

                    if (eventType == "response.audio.delta" && !string.IsNullOrWhiteSpace(_streamId))
                    {
                        var deltaEvent = OpenAIEventsFactory.BuildDeltaEvent(_streamId, root.GetStringProperty("delta"));
                        await SendWebSocketMessage(clientWebSocket, deltaEvent);
                    }

                    if (eventType == "response.done")
                    {
                        var status = root.Value.GetStringProperty("status");

                        if (status == "failed")
                        {
                            Console.WriteLine("[OPEN AI] [ERROR] {0}", message);
                            continue;
                        }

                        var outputObject = root.Value.GetProperty("response").GetProperty("output").FirstOrDefault();

                        if (outputObject == null)
                            continue;

                        var type = outputObject.Value.GetStringProperty("type");

                        if (type == "function_call" && outputObject.GetStringProperty("name") == "consulta_via_cep")
                        {
                            var callId = outputObject.GetStringProperty("call_id");
                            var arguments = JsonDocument.Parse(outputObject.GetStringProperty("arguments"));
                            var resultCep = await ConsultarViaCepAsync(arguments.RootElement.GetProperty("cep").GetString());

                            await SendResponseToFunction(openAiWebSocket, callId, resultCep);
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

                    if (!message.TryParseToJson(out var documentRoot) || !documentRoot.HasValue)
                        continue;

                    var eventType = documentRoot.GetStringProperty("event");

                    if (eventType == "start")
                    {
                        _streamId = documentRoot.GetStringProperty("streamSid");
                    }
                    
                    if (eventType == "media")
                    {
                        var audio = new
                        {
                            type = "input_audio_buffer.append",
                            audio = documentRoot.Value.GetProperty("media").GetStringProperty("payload")
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

        #region OPEN AI FUNCTION HANDLING

        private async Task SendResponseToFunction(WebSocket openAiWebSocket, string? callId, object response)
        {
            if (string.IsNullOrWhiteSpace(callId))
            {
                Console.WriteLine("[OPEN AI] [ERROR] CallID vazio para chamada de função.");
                return;
            }

            var conversationItem = OpenAIEventsFactory.BuildFunctionResponseEvent(callId, response);
            await SendWebSocketMessage(openAiWebSocket, conversationItem, true);
        }

        public async Task<string> ConsultarViaCepAsync(string cep)
        {
            var httpClient = _httpClientFactory.CreateClient();
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

        #endregion

        #region WEB SOCKET HELPERS

        private async Task SendWebSocketMessage(WebSocket websocket, object message, bool requestResponseFromEvent = false)
        {
            string json;

            if (message is string cast)
                json = cast;
            else
                json = JsonSerializer.Serialize(message);

            var bytes = Encoding.UTF8.GetBytes(json);

            await websocket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken: CancellationToken.None
            );

            if (requestResponseFromEvent)
                await SendWebSocketMessage(websocket, new { type = "response.create" });
        }

        private async Task CloseWebSocket(WebSocket websocket, string webSocketName = "Default")
        {
            Console.WriteLine("Closing WebSocket {0}", webSocketName);

            if (websocket.State == WebSocketState.Open)
                await websocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
        }

        #endregion
    }
}
