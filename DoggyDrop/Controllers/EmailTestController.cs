using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity.UI.Services;
using System.Threading.Tasks;

namespace DoggyDrop.Controllers
{
    public class EmailTestController : Controller
    {
        private readonly IEmailSender _emailSender;

        public EmailTestController(IEmailSender emailSender)
        {
            _emailSender = emailSender;
        }

        [HttpGet("/test-email")]
        public async Task<IActionResult> SendTestEmail()
        {
            await _emailSender.SendEmailAsync(
                "admin@doggydrop.app",
                "📧 Testni email iz DoggyDrop",
                "<strong>To je testno obvestilo iz sistema DoggyDrop.</strong><br>Če vidiš to sporočilo, e-pošta deluje pravilno."
            );

            return Content("✅ Testni e-mail poslan na admin@doggydrop.app.");
        }
    }
}
