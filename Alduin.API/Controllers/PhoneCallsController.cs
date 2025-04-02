using System.Diagnostics;
using Alduin.Core.Models.Configs;
using Alduin.Core.Services.PhoneCalls;
using Microsoft.AspNetCore.Mvc;
using Twilio.TwiML;
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

        public const string CURRENT_NGROK_URL = "https://da4a-2804-14d-4c84-11a7-ecb8-8c0a-c330-39c4.ngrok-free.app/api/phonecalls/customer-service";

        [HttpPost("incoming")]
        public IActionResult Incoming([FromForm] string CallSid)
        {
            _phoneCallsService.StartPhoneCall(CallSid);

            var response = new VoiceResponse();
            PlayAudioFile(response, _settings.GreetingsAudio);
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
