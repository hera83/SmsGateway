using System.ComponentModel.DataAnnotations;

namespace web.Models.Account;

public class SetupViewModel
{
    [Required(ErrorMessage = "E-mail er påkrævet")]
    [EmailAddress(ErrorMessage = "Ugyldig e-mailadresse")]
    [Display(Name = "E-mail")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Adgangskode er påkrævet")]
    [MinLength(8, ErrorMessage = "Adgangskode skal være mindst 8 tegn")]
    [DataType(DataType.Password)]
    [Display(Name = "Adgangskode")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Bekræft adgangskode er påkrævet")]
    [DataType(DataType.Password)]
    [Compare("Password", ErrorMessage = "Adgangskoderne stemmer ikke overens")]
    [Display(Name = "Bekræft adgangskode")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
