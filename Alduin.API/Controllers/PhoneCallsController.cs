using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Alduin.Core;
using Alduin.Core.Models.Configs;
using Alduin.Core.Services.OpenAI;
using Alduin.Core.Services.PhoneCalls;
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

        public PhoneCallsController(IPhoneCallsService phoneCallService, GeneralSettings settings)
        {
            _phoneCallsService = phoneCallService;
            _settings = settings;
        }

        private IPhoneCallsService _phoneCallsService;
        private readonly GeneralSettings _settings;

        public const string CURRENT_NGROK_URL = "https://bd5a-2804-14d-4c84-11a7-911e-dac7-c064-f6f.ngrok-free.app/api/phonecalls/customer-service";

        [HttpPost("incoming")]
        public IActionResult Incoming([FromForm] string CallSid)
        {
            _phoneCallsService.StartPhoneCall(CallSid);

            var response = new VoiceResponse();
            PlayAudioFile(response, _settings.GreetingsAudio, 2);
            return Content(response.ToString(), "application/xml");
        }

        [HttpPost("customer-service")]
        public async Task<IActionResult> CustomerService([FromForm] string RecordingUrl, [FromForm] string CallSid)
        {
            var watch = Stopwatch.StartNew();
            var assistantResponse = await _phoneCallsService.GenerateResponseFromAssistant(CallSid, RecordingUrl);
            watch.Stop();
            Console.WriteLine($"Bot demorou {watch.Elapsed} para responder");

            var response = new VoiceResponse();
            PlayAudioFile(response, assistantResponse.AudioFileName);
            return Content(response.ToString(), "application/xml");
        }

        private void PlayAudioFile(VoiceResponse voice, string audioName, int timeout = 2)
        {
            try
            {
                string url = Path.Combine($"{Request.Scheme}://{Request.Host}", _settings.AudiosFolder, audioName).Replace("\\", "/");

                var speechUri = new Uri(url); 
                voice.Play(speechUri);
                voice.Record(action: new Uri(CURRENT_NGROK_URL), timeout: timeout, maxLength: 30);
            }
            finally { }
        }
    }
}
