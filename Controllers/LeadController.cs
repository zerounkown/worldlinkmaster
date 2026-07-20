using Microsoft.AspNetCore.Mvc;
using WorldLinkMaster.Web.Data;
using WorldLinkMaster.Web.Models;
using WorldLinkMaster.Web.Models.ViewModels;
using WorldLinkMaster.Web.Services;

namespace WorldLinkMaster.Web.Controllers;

public class LeadController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IEmailService _emailService;
    private readonly IRecaptchaService _recaptchaService;

    public LeadController(
        ApplicationDbContext context,
        IEmailService emailService,
        IRecaptchaService recaptchaService)
    {
        _context = context;
        _emailService = emailService;
        _recaptchaService = recaptchaService;
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new LeadFormViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(LeadFormViewModel model)
    {
        if (!string.IsNullOrEmpty(model.Website))
        {
            return Json(new { success = false, message = "تم رفض الطلب" });
        }

        if (!ModelState.IsValid)
        {
            return Json(new
            {
                success = false,
                message = "من فضلك تأكد من البيانات المدخلة",
                errors = ModelState
            });
        }

        bool isHuman = await _recaptchaService.VerifyAsync(model.RecaptchaToken);
        if (!isHuman)
        {
            return Json(new { success = false, message = "فشل التحقق من أنك لست روبوت" });
        }

        var lead = new Lead
        {
            Name = model.Name,
            Email = model.Email,
            Phone = model.Phone,
            Message = model.Message,
            CreatedAt = DateTime.UtcNow
        };

        _context.Leads.Add(lead);
        await _context.SaveChangesAsync();

        try
        {
            await _emailService.SendLeadNotificationAsync(lead);
        }
        catch
        {
            // log it, but don't fail the whole request over an email hiccup
        }

        return Json(new { success = true, message = "تم استلام بياناتك بنجاح، هنتواصل معاك قريب!" });
    }
}
