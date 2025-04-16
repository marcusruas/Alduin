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
            string callKey = $"AlduinCall_{Guid.NewGuid()}";
            _cache.Set(callKey, new CustomerServiceCallSettings(), TimeSpan.FromHours(2));

            using var twilioWebSocket = await httpContext.WebSockets.AcceptWebSocketAsync();
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
                HandleOpenAIWebSocket(callKey, openAiWebSocket, twilioWebSocket),
                HandleTwilioWebSocket(callKey, twilioWebSocket, openAiWebSocket)
            };

            await Task.WhenAll(wsHandlers);
        }

        private async Task InitializeOpenAISession(ClientWebSocket openAiWebSocket)
        {
            await SendMessage(openAiWebSocket, OpenAIEventsBuilder.BuildSessionUpdateEvent(_settings));
            await SendMessage(openAiWebSocket, OpenAIEventsBuilder.StartConversationEvent, true);
        }

        private async Task HandleOpenAIWebSocket(string cacheKey, ClientWebSocket openAiWebSocket, WebSocket twilioWebSocket)
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
                        await CloseWebSocket(openAiWebSocket);
                        break;
                    }

                    if (twilioWebSocket.State != WebSocketState.Open)
                    {
                        await CloseWebSocket(openAiWebSocket);
                        break;
                    }

                    var settings = _cache.Get<CustomerServiceCallSettings>(cacheKey);

                    if (settings == null)
                    {
                        _logger.LogError("Call {cacheKey} has ended because the cache expired.", cacheKey);
                        await CloseAllWebSockets(openAiWebSocket, twilioWebSocket);
                        continue;
                    }

                    if (!eventContent.TryParseToJson(out var root) || !root.HasValue)
                        continue;

                    if (!string.IsNullOrWhiteSpace(eventContent) && eventContent.Contains("error"))
                        _logger.LogInformation("[OPEN AI] An error event was received by the web socket: {0}", eventContent);

                    var eventType = root.GetStringProperty("type");

                    switch (eventType)
                    {
                        case "input_audio_buffer.speech_started":
                            await InterruptAssistant(twilioWebSocket, openAiWebSocket, settings);
                            break;
                        case "response.audio.delta":
                            var delta = root.GetStringProperty("delta");
                            if (string.IsNullOrWhiteSpace(delta))
                                continue;

                            var deltaEvent = OpenAIEventsBuilder.BuildDeltaEvent(settings.StreamSid, delta);
                            await SendMessage(twilioWebSocket, deltaEvent);

                            if (settings.FirstDeltaFromCurrentResponse == null)
                                settings.FirstDeltaFromCurrentResponse = settings.LatestTimestamp;

                            var currentAssistant = root.GetStringProperty("item_id");
                            if (!string.IsNullOrWhiteSpace(currentAssistant))
                                settings.LastAssistantId = currentAssistant;

                            await SendMark(twilioWebSocket, settings);
                            break;
                        case "response.done":
                            if (root.Value.GetStringProperty("status") == "failed")
                            {
                                _logger.LogError("Open AI Web Socket received a failed response event: {content}", eventContent);
                                continue;
                            }

                            var outputObject = root.Value.GetProperty("response").GetProperty("output").FirstOrDefault();

                            if (outputObject == null)
                                continue;

                            var type = outputObject.Value.GetStringProperty("type");

                            if (type != "function_call")
                                continue;

                            var functionName = outputObject.GetStringProperty("name");
                            var arguments = outputObject.Value.GetProperty("arguments");
                            var callId = outputObject.GetStringProperty("call_id");

                            _logger.LogInformation("Assistant called function {functionName}. Arguments: {arguments}", functionName, arguments);

                            if (functionName == "end_call")
                            {
                                var response = OpenAIEventsBuilder.BuildFunctionResponseEvent(callId, new { response = "The call will close in around 10 seconds. Warn the user in his language" });
                                await SendMessage(openAiWebSocket, response, true);
                                EndCall(twilioWebSocket, openAiWebSocket);
                            }
                            else if (_functions.TryGet(functionName, out var handler))
                            {
                                var functionResult = await handler(_serviceProvider, arguments); 
                                var response = OpenAIEventsBuilder.BuildFunctionResponseEvent(callId, functionResult);
                                await SendMessage(openAiWebSocket, response, true);
                            }

                            _logger.LogError("Function {function} was not found in the registry.", functionName);
                            EndCall(twilioWebSocket, openAiWebSocket);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process Open AI Web Socket event. Event details: {content}. Call SID: {cacheKey}", eventContent, cacheKey);
                }
            }
        }

        private async Task HandleTwilioWebSocket(string cacheKey, WebSocket twilioWebSocket, ClientWebSocket openAiWebSocket)
        {
            var buffer = new byte[8192 * 2];
            while (twilioWebSocket.State == WebSocketState.Open)
            {
                var receivedEvent = await twilioWebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                var eventContent = Encoding.UTF8.GetString(buffer, 0, receivedEvent.Count);

                _logger.LogDebug("[Twilio] Web Socket Event received: {event}. Content: {content}", JsonSerializer.Serialize(receivedEvent), eventContent);

                try
                {
                    if (receivedEvent.MessageType == WebSocketMessageType.Close)
                    {
                        await CloseWebSocket(openAiWebSocket);
                        break;
                    }

                    if (openAiWebSocket.State != WebSocketState.Open)
                    {
                        await CloseWebSocket(twilioWebSocket);
                        break;
                    }

                    var settings = _cache.Get<CustomerServiceCallSettings>(cacheKey);

                    if (settings == null)
                    {
                        _logger.LogError("Call {cacheKey} has ended because the cache expired.", cacheKey);
                        await CloseAllWebSockets(openAiWebSocket, twilioWebSocket);
                        continue;
                    }

                    if (settings.SecondsSinceLastSpeech >= _settings.ClientInactivityTimeout)
                    {
                        _logger.LogInformation("Call {cacheKey} has ended due to inactivity.", cacheKey);
                        await CloseAllWebSockets(openAiWebSocket, twilioWebSocket);
                        continue;
                    }

                    if (!eventContent.TryParseToJson(out var documentRoot) || !documentRoot.HasValue)
                        continue;

                    if (!string.IsNullOrWhiteSpace(eventContent) && eventContent.Contains("error"))
                        _logger.LogInformation("[Twilio] An error event was received by the web socket: {event}", eventContent);

                    var eventType = documentRoot.GetStringProperty("event");

                    switch (eventType)
                    {
                        case "start":
                            settings.StreamSid = documentRoot.Value.GetProperty("start").GetStringProperty("streamSid");
                            settings.FirstDeltaFromCurrentResponse = null;
                            break;
                        case "media":
                            var mediaObject = documentRoot.Value.GetProperty("media");

                            settings.LatestTimestamp = mediaObject.GetIntProperty("timestamp") ?? 0;
                            await SendMessage(openAiWebSocket, new
                            {
                                type = "input_audio_buffer.append",
                                audio = mediaObject.GetStringProperty("payload")
                            });
                            break;
                        case "mark" when settings.Marks.Any():
                            settings.Marks.TryDequeue(out _);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process Twilio Web Socket event. Event details: {content}. Call SID: {cacheKey}", eventContent, cacheKey);
                }
            }
        }

        private async Task InterruptAssistant(WebSocket twilioWebSocket, ClientWebSocket openAiWebSocket, CustomerServiceCallSettings settings)
        {
            settings.LastClientSpeech = DateTime.UtcNow;

            if (!settings.Marks.Any() || settings.FirstDeltaFromCurrentResponse == null)
                return;

            var elapsedTime = settings.LatestTimestamp - settings.FirstDeltaFromCurrentResponse.Value;

            if (!string.IsNullOrWhiteSpace(settings.LastAssistantId))
            {
                await SendMessage(openAiWebSocket, new
                {
                    type = "conversation.item.truncate",
                    item_id = settings.LastAssistantId,
                    content_index = 0,
                    audio_end_ms = elapsedTime
                });
            }

            await SendMessage(twilioWebSocket, new
            {
                @event = "clear",
                streamSid = settings.StreamSid
            });

            settings.Marks.Clear();
            settings.LastAssistantId = null;
            settings.FirstDeltaFromCurrentResponse = null;
        }

        private async Task SendMark(WebSocket twilioWebSocket, CustomerServiceCallSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.StreamSid))
                return;

            await SendMessage(twilioWebSocket, new
            {
                @event = "mark",
                streamSid = settings.StreamSid,
                mark = new { name = "responsePart" }
            });

            settings.Marks.Enqueue("responsePart");
        }

        #region WEB SOCKET HELPERS

        private void EndCall(WebSocket twilioWebSocket, ClientWebSocket openAiWebSocket)
        {
            _ = Task.Run(async () =>
            {
                _logger.LogInformation("A call has ended.");
                await Task.Delay(TimeSpan.FromSeconds(10));
                await CloseAllWebSockets(openAiWebSocket, twilioWebSocket);
            });
        }

        private static async Task SendMessage(WebSocket websocket, object message, bool requestResponseFromEvent = false)
        {
            string messageJson = message is string cast ? cast : JsonSerializer.Serialize(message);
            var bytes = Encoding.UTF8.GetBytes(messageJson);

            await websocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);

            if (requestResponseFromEvent)
                await SendMessage(websocket, new { type = "response.create" });
        }

        private static async Task CloseAllWebSockets(ClientWebSocket openAiWebSocket, WebSocket twilioWebSocket)
        {
            await CloseWebSocket(openAiWebSocket);
            await CloseWebSocket(twilioWebSocket);
        }

        private static async Task CloseWebSocket(WebSocket websocket)
        {
            if (websocket.State == WebSocketState.Open)
                await websocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
        }

        #endregion
    }
}
