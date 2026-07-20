using System.ComponentModel.DataAnnotations;

namespace WorldLinkMaster.Web.Models.ViewModels;

public class LeadFormViewModel
{
    [Required(ErrorMessage = "الاسم مطلوب")]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "البريد الإلكتروني مطلوب")]
    [EmailAddress(ErrorMessage = "البريد الإلكتروني غير صحيح")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "رقم الهاتف مطلوب")]
    [Phone(ErrorMessage = "رقم الهاتف غير صحيح")]
    public string Phone { get; set; } = string.Empty;

    // اختياري
    public string? Message { get; set; }

    // Honeypot: حقل مخفي، البوتات بتملأه والمستخدمين الحقيقيين لأ
    public string? Website { get; set; }

    // توكن reCAPTCHA اللي بييجي من الـ frontend
    public string? RecaptchaToken { get; set; }
}
