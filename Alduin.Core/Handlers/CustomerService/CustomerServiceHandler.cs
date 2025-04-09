using System;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text;
using Alduin.Core.Helpers;
using Alduin.Models;
using Microsoft.Extensions.Caching.Memory;

namespace Alduin
{
    internal class CustomerServiceHandler : ICustomerServiceHandler
    {
        public CustomerServiceHandler(AlduinSettings settings, IMemoryCache cache, ILogger<CustomerServiceHandler> logger)
        {
            _settings = settings;
            _cache = cache;
            _logger = logger;
        }

        private readonly AlduinSettings _settings;
        private readonly IMemoryCache _cache;
        private readonly ILogger<CustomerServiceHandler> _logger;

        public async Task HandleAsync(HttpContext httpContext)
        {
            var callSid = httpContext.Request.Query["CallSid"].ToString();

            if (string.IsNullOrEmpty(callSid))
            {
                httpContext.Response.StatusCode = 400;
                await httpContext.Response.WriteAsync("Missing CallSid");
                return;
            }

            string sidKey = $"Alduin_{callSid}";
            _cache.Set(sidKey, new CustomerServiceCallSettings(), TimeSpan.FromHours(2));

            using var clientWebSocket = await httpContext.WebSockets.AcceptWebSocketAsync();
            using var openAiWebSocket = new ClientWebSocket();

            var openAiWebSocketUri = new Uri(string.Format(AlduinSettings.OPEN_AI_WEBSOCKET_URL, _settings.RealtimeModel));
            openAiWebSocket.Options.SetRequestHeader("Authorization", $"Bearer {_settings.OpenAIApiKey}");
            openAiWebSocket.Options.SetRequestHeader("OpenAI-Beta", "realtime=v1");
            await openAiWebSocket.ConnectAsync(openAiWebSocketUri, CancellationToken.None);

            _logger.LogInformation("Connection to the OpenAI WebSocket stablished.");

            await InitializeOpenAISession(openAiWebSocket);

            _logger.LogInformation("Open AI session started. Processing the Web Socket requests.");

            var wsHandlers = new Task[]
            {
                HandleOpenAIWebSocket(sidKey, openAiWebSocket, clientWebSocket),
                HandleClientWebSocket(sidKey, clientWebSocket, openAiWebSocket)
            };

            await Task.WhenAll(wsHandlers);
        }

        private async Task InitializeOpenAISession(ClientWebSocket openAiWebSocket)
        {
            await SendWebSocketMessage(openAiWebSocket, OpenAIEventsBuilder.BuildSessionUpdateEvent(_settings));
            await SendWebSocketMessage(openAiWebSocket, OpenAIEventsBuilder.StartConversationEvent, true);
        }

        private async Task HandleOpenAIWebSocket(string cacheKey, ClientWebSocket openAiWebSocket, WebSocket clientWebSocket)
        {
            var buffer = new byte[8192 * 2];
            while (openAiWebSocket.State == WebSocketState.Open)
            {
                var receivedEvent = await openAiWebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                var eventContent = Encoding.UTF8.GetString(buffer, 0, receivedEvent.Count);

                _logger.LogDebug("[OPEN AI] Web Socket Event received: {event}. Content: {content}", JsonSerializer.Serialize(receivedEvent), eventContent);

                try
                {
                    if (receivedEvent.MessageType == WebSocketMessageType.Close)
                    {
                        await CloseWebSocket(openAiWebSocket, "Twillio");
                        break;
                    }

                    if (clientWebSocket.State != WebSocketState.Open)
                    {
                        await CloseWebSocket(openAiWebSocket, "Open AI");
                        break;
                    }

                    if (!eventContent.TryParseToJson(out var root) || !root.HasValue)
                        continue;

                    var eventType = root.GetStringProperty("type");

                    if (eventType == "response.audio.delta")
                    {
                        var streamId = _cache.Get<CustomerServiceCallSettings>(cacheKey).StreamId;

                        if (!string.IsNullOrWhiteSpace(streamId))
                        {
                            var deltaEvent = OpenAIEventsBuilder.BuildDeltaEvent(streamId, root.GetStringProperty("delta"));
                            await SendWebSocketMessage(clientWebSocket, deltaEvent);
                        }
                    }

                    if (eventType == "response.done")
                    {
                        var status = root.Value.GetStringProperty("status");

                        if (status == "failed")
                        {
                            _logger.LogError("Open AI Web Socket received a failed response event: {content}", eventContent);
                            continue;
                        }

                        var outputObject = root.Value.GetProperty("response").GetProperty("output").FirstOrDefault();

                        if (outputObject == null)
                            continue;

                        var type = outputObject.Value.GetStringProperty("type");

                        //if (type == "function_call" && outputObject.GetStringProperty("name") == "consulta_via_cep")
                        //{
                        //    var callId = outputObject.GetStringProperty("call_id");
                        //    var arguments = JsonDocument.Parse(outputObject.GetStringProperty("arguments"));
                        //    var resultCep = await ConsultarViaCepAsync(arguments.RootElement.GetProperty("cep").GetString());

                        //    await SendResponseToFunction(openAiWebSocket, callId, resultCep);
                        //}
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process Open AI Web Socket event. Event details: {content}. Call SID: {callSid}", eventContent, cacheKey);
                }
            }
        }

        private async Task HandleClientWebSocket(string cacheKey, WebSocket clientWebSocket, ClientWebSocket openAiWebSocket)
        {
            var buffer = new byte[8192 * 2];
            while (clientWebSocket.State == WebSocketState.Open)
            {
                var receivedEvent = await clientWebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                var eventContent = Encoding.UTF8.GetString(buffer, 0, receivedEvent.Count);

                _logger.LogDebug("[TWILLIO] Web Socket Event received: {event}. Content: {content}", JsonSerializer.Serialize(receivedEvent), eventContent);

                try
                {
                    if (receivedEvent.MessageType == WebSocketMessageType.Close)
                    {
                        await CloseWebSocket(openAiWebSocket, "Open AI");
                        break;
                    }

                    if (openAiWebSocket.State != WebSocketState.Open)
                    {
                        await CloseWebSocket(clientWebSocket, "Twillio");
                        break;
                    }

                    if (!eventContent.TryParseToJson(out var documentRoot) || !documentRoot.HasValue)
                        continue;

                    var eventType = documentRoot.GetStringProperty("event");

                    if (eventType == "start")
                    {
                        _cache.Get<CustomerServiceCallSettings>(cacheKey).StreamId = documentRoot.GetStringProperty("streamSid");
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
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process Twillio Web Socket event. Event details: {content}. Call SID: {callSid}", eventContent, cacheKey);
                }
            }
        }

        #region WEB SOCKET HELPERS

        private async Task SendWebSocketMessage(WebSocket websocket, object message, bool requestResponseFromEvent = false)
        {
            string json = message is string cast ? cast : JsonSerializer.Serialize(message);
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
            _logger.LogInformation("Closing WebSocket {websocket}", webSocketName);

            if (websocket.State == WebSocketState.Open)
                await websocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
        }

        #endregion
    }
}
