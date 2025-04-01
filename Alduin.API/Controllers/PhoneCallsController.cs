using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Alduin.Core.Models.Configs;
using Alduin.Core.Services.OpenAI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using OpenAI;
using OpenAI.Chat;
using Twilio.Rest.Api.V2010.Account;
using Twilio.TwiML;
using Twilio.TwiML.Voice;
namespace Alduin.API.Controllers
{
    [Route("api/phonecalls")]
    [ApiController]
    public class PhoneCallsController : Controller
    {

        public PhoneCallsController(IMemoryCache cache, IOpenAIService aiService, GeneralSettings settings)
        {
            _cache = cache;
            _aiService = aiService;
            _settings = settings;
        }

        private readonly IMemoryCache _cache;
        private readonly IOpenAIService _aiService;
        private readonly GeneralSettings _settings;

        public const string CURRENT_NGROK_URL = "https://7f1d-2804-14d-4c84-11a7-2954-56dd-aa0d-8fce.ngrok-free.app/api/phonecalls/customer-service";

        [HttpPost("incoming")]
        public async Task<IActionResult> Incoming([FromForm] string CallSid)
        {
            var test = await _aiService.GenerateSpeech(_settings.OpenAISettings.Prompts.GreetingsPrompt);
            var conversation = new List<ChatMessage>()
            {
                ChatMessage.CreateSystemMessage(_settings.OpenAISettings.Prompts.OperatorPrompt)
            };
            _cache.Set(CallSid, conversation, DateTime.Now.AddHours(2));

            var response = new VoiceResponse();
            PlayAudioFile(response, "greetings.mp3");

            return Content(response.ToString(), "application/xml");
        }

        [HttpPost("customer-service")]
        public async Task<IActionResult> CustomerService([FromForm] string? RecordingUrl, [FromForm] string? CallSid)
        {
            Console.WriteLine(RecordingUrl);

            var watch = Stopwatch.StartNew();
            System.IO.Stream audioStream = null;
            using (var httpClient = new HttpClient())
            {
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", "QUM2ZWUzZTM0ZDVhZmE3N2ViYmZkOTk2MTRiZDFhMzM4ODowMGU3YTZmZWZjY2Q1MDc4MzViODNmMTYzODczMTAxYw=="); ;
                var delaySeconds = 2;

                for (int attempt = 1; attempt <= 5; attempt++)
                {
                    Console.WriteLine("Tentativa " + attempt.ToString());
                    var httpResponse = await httpClient.GetAsync($"{RecordingUrl}.wav");

                    if (httpResponse.IsSuccessStatusCode)
                    {
                        audioStream = await httpResponse.Content.ReadAsStreamAsync();
                        break;
                    }
                    else if (httpResponse.StatusCode == HttpStatusCode.NotFound)
                    {
                        // Arquivo ainda não disponível, aguarde antes de tentar novamente
                        await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                    }
                    else
                    {
                        // Outro erro, lança exceção
                        httpResponse.EnsureSuccessStatusCode();
                    }
                }
            }

            var audioTranscription = await _aiService.GenerateTranscriptFromRecording(audioStream);

            Console.WriteLine("O que o bot ouviu: " + audioTranscription);

            var cachedConversation = (List<ChatMessage>) _cache.Get(CallSid);
            cachedConversation.Add(ChatMessage.CreateUserMessage(audioTranscription));

            var assistantResponse = await _aiService.GenerateResponseFromCall(cachedConversation);
            
            cachedConversation.Add(ChatMessage.CreateAssistantMessage(assistantResponse.ResponseText));
            _cache.Set(CallSid, cachedConversation, DateTime.Now.AddHours(2));

            Console.WriteLine("O que o bot respondeu: " + assistantResponse.ResponseText);
            watch.Stop();
            Console.WriteLine("Demorou: " + watch.Elapsed.ToString());
            var response = new VoiceResponse();
            PlayAudioFile(response, assistantResponse.AudioFileName);


            return Content(response.ToString(), "application/xml");
        }

        private void PlayAudioFile(VoiceResponse voice, string audioName)
        {
            try
            {
                string url = Path.Combine($"{Request.Scheme}://{Request.Host}", _settings.AudiosFolder, audioName).Replace("\\", "/");

                var speechUri = new Uri(url); 
                voice.Play(speechUri);
                voice.Record(action: new Uri(CURRENT_NGROK_URL), timeout: 3, maxLength: 30);
            }
            finally { }
        }
    }
}
