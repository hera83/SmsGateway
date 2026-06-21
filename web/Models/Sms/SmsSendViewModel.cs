using System.ComponentModel.DataAnnotations;

namespace web.Models.Sms;

public sealed class SmsSendViewModel
{
    [Required(ErrorMessage = "Modtager telefonnummer er påkrævet")]
    [Display(Name = "Modtager (8 cifre)")]
    [RegularExpression(@"^\d{8}$", ErrorMessage = "Telefonnummer skal være 8 cifre")]
    public string ToPhoneNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Besked er påkrævet")]
    [Display(Name = "Besked")]
    [MaxLength(1600)]
    public string Message { get; set; } = string.Empty;
}
