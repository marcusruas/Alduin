using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Alduin.Core.Models.Configs;
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
        public PhoneCallsService(IHttpClientFactory httpClientFactory, IMemoryCache cache, GeneralSettings settings)
        {
            _httpClientFactory = httpClientFactory;
            _cache = cache;
            _settings = settings;

            _promptsFolder = Path.Combine(AppContext.BaseDirectory, "Prompts");
        }

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMemoryCache _cache;
        private readonly GeneralSettings _settings;

        private readonly string _promptsFolder;

        public async Task StartPhoneCall(string callSid)
        {
            string promptPath = Path.Combine(_promptsFolder, "OperatorPrompt.txt");

            if (!File.Exists(promptPath))
                throw new Exception("Prompt not found");

            var operatorPrompt = await File.ReadAllLinesAsync(promptPath, Encoding.UTF8);
            var unifiedPrompt = string.Join(' ', operatorPrompt.Where(x => !string.IsNullOrWhiteSpace(x)));

            var conversation = new List<ChatMessage>()
            {
                ChatMessage.CreateSystemMessage(unifiedPrompt),
                ChatMessage.CreateAssistantMessage("Olá, aqui é da Unidas. Meu nome é Alduin, com quem eu falo?")
            };

            _cache.Set(callSid, conversation, DateTime.Now.AddHours(1));
        }
    }
}
