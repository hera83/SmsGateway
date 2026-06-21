using System.ComponentModel.DataAnnotations;

namespace web.Models.Account;

public class LoginViewModel
{
    [Required(ErrorMessage = "E-mail er påkrævet")]
    [EmailAddress(ErrorMessage = "Ugyldig e-mailadresse")]
    [Display(Name = "E-mail")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Adgangskode er påkrævet")]
    [DataType(DataType.Password)]
    [Display(Name = "Adgangskode")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Husk mig")]
    public bool RememberMe { get; set; }
}
