using System;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text;
using Alduin.Core.Helpers;
using Alduin.Models;
using Microsoft.Extensions.Caching.Memory;
using Alduin.Core.Handlers.AlduinFunctions;
using static System.Runtime.InteropServices.JavaScript.JSType;
using Microsoft.Extensions.DependencyInjection;

namespace Alduin
{
    internal class CustomerServiceHandler : ICustomerServiceHandler
    {
        public CustomerServiceHandler(IServiceProvider serviceProvider, IAlduinFunctionRegistry functions, AlduinSettings settings, IMemoryCache cache, ILogger<CustomerServiceHandler> logger)
        {
            _serviceProvider = serviceProvider;
            _functions = functions;
            _settings = settings;
            _cache = cache;
            _logger = logger;
        }

        private readonly IServiceProvider _serviceProvider;
        private readonly IAlduinFunctionRegistry _functions;
        private readonly AlduinSettings _settings;
        private readonly IMemoryCache _cache;
        private readonly ILogger<CustomerServiceHandler> _logger;

        public async Task HandleAsync(HttpContext httpContext)
        {
            string callKey = $"Alduin_{Guid.NewGuid()}";
            _cache.Set(callKey, new CustomerServiceCallSettings(), TimeSpan.FromHours(2));

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
                HandleOpenAIWebSocket(callKey, openAiWebSocket, clientWebSocket),
                HandleClientWebSocket(callKey, clientWebSocket, openAiWebSocket)
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
                    if (!string.IsNullOrWhiteSpace(eventContent) && eventContent.Contains("error"))
                        _logger.LogInformation("[OPEN AI] An error event was received by the web socket: {0}", eventContent);

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

                    if (eventType == "input_audio_buffer.speech_started")
                    {
                        _cache.Get<CustomerServiceCallSettings>(cacheKey).LastClientSpeech = DateTime.UtcNow;
                    }

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

                        if (type == "function_call")
                        {
                            var functionName = outputObject.GetStringProperty("name");
                            var arguments = outputObject.Value.GetProperty("arguments");
                            var callId = outputObject.GetStringProperty("call_id");

                            _logger.LogInformation("Assistant called function {functionName}. Arguments: {arguments}", functionName, arguments);

                            if (functionName == "end_call")
                            {
                                var response = OpenAIEventsBuilder.BuildFunctionResponseEvent(callId, new { response = "The call will close in around 5 seconds. Warn the user in his language" });
                                await SendWebSocketMessage(openAiWebSocket, response, true);

                                EndCall(clientWebSocket, openAiWebSocket);
                            }
                            else if (_functions.TryGet(functionName, out var handler))
                            {
                                
                                var functionResult = await handler(_serviceProvider, arguments);

                                var response =  OpenAIEventsBuilder.BuildFunctionResponseEvent(callId, functionResult);
                                await SendWebSocketMessage(openAiWebSocket, response, true);
                            }
                            else
                            {
                                _logger.LogError("Function {function} was not found in the registry.", functionName);
                            }
                        }
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
                if (_cache.Get<CustomerServiceCallSettings>(cacheKey).SecondsSinceLastSpeech >= _settings.ClientInactivityTimeout)
                {
                    EndCall(clientWebSocket, openAiWebSocket);
                }

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
                        _cache.Get<CustomerServiceCallSettings>(cacheKey).StreamId = documentRoot.Value.GetProperty("start").GetStringProperty("streamSid");
                        _cache.Get<CustomerServiceCallSettings>(cacheKey).CallSid = documentRoot.Value.GetProperty("start").GetStringProperty("callSid");
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

        private void EndCall(WebSocket clientWebSocket, ClientWebSocket openAiWebSocket)
        {
            _ = Task.Run(async () =>
            {
                _logger.LogInformation("A call has ended.");
                await Task.Delay(TimeSpan.FromSeconds(10));
                await CloseWebSocket(openAiWebSocket, "Open AI");
                await CloseWebSocket(clientWebSocket, "Client");
            });
        }

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
