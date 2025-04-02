using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Alduin.Core.Models.Configs;
using Alduin.Core.Services.OpenAI;
using Microsoft.Extensions.Caching.Memory;
using OpenAI.Chat;
using System.Net.Http;
using Alduin.Core.Models.AI;
using Polly;
using Polly.Contrib.WaitAndRetry;
using Polly.Retry;

namespace Alduin.Core.Services.PhoneCalls
{
    public class PhoneCallsService : IPhoneCallsService
    {
        public PhoneCallsService(IHttpClientFactory httpClientFactory, IMemoryCache cache, IOpenAIService openAIService, GeneralSettings settings)
        {
            _httpClientFactory = httpClientFactory;
            _cache = cache;
            _openAIService = openAIService;
            _settings = settings;
        }

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMemoryCache _cache;
        private readonly IOpenAIService _openAIService;
        private readonly GeneralSettings _settings;

        public void StartPhoneCall(string callSid)
        {
            var conversation = new List<ChatMessage>()
            {
                ChatMessage.CreateSystemMessage(_settings.OpenAISettings.Prompts.OperatorPrompt)
            };

            _cache.Set(callSid, conversation, DateTime.Now.AddHours(1));
        }

        public async Task<AIResponse> GenerateResponseFromAssistant(string callSid, string userRecordingUrl)
        {
            var audio = await GetPhoneCallAudio(userRecordingUrl);

            var audioTranscription = await _openAIService.GenerateTranscriptFromRecording(audio);

            var cachedConversation = _cache.Get<List<ChatMessage>>(callSid);

            if (cachedConversation == null)
                throw new Exception($"A ligação {callSid} não está em cache");

            cachedConversation.Add(ChatMessage.CreateUserMessage(audioTranscription));

            var assistantResponse = await _openAIService.GenerateResponseFromCall(cachedConversation);

            cachedConversation.Add(ChatMessage.CreateAssistantMessage(assistantResponse.ResponseText));
            _cache.Set(callSid, cachedConversation, DateTime.Now.AddHours(1));

            return assistantResponse;
        }

        private async Task<Stream> GetPhoneCallAudio(string recordingUrl)
        {
            var httpClient = _httpClientFactory.CreateClient("Twillio");

            var delay = Backoff.DecorrelatedJitterBackoffV2(TimeSpan.FromSeconds(1), 5);
            var retryPolicy = Policy
            .Handle<HttpRequestException>()
            .OrResult<HttpResponseMessage>(r => r.StatusCode == HttpStatusCode.NotFound)
            .WaitAndRetryAsync(delay, (outcome, timespan, retryAttempt, context) =>
            {
                Console.WriteLine($"Tentativa {retryAttempt} falhou. Aguardando {timespan.TotalSeconds} segundos antes de tentar novamente.");
            });

            var response = await retryPolicy.ExecuteAsync(() => httpClient.GetAsync($"{recordingUrl}.wav"));

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsStreamAsync();
        }
    }
}
