using System.ComponentModel.DataAnnotations;

namespace web.Models.Keys;

public sealed class KeysCreateViewModel
{
    [Required(ErrorMessage = "Navn er påkrævet")]
    [Display(Name = "Navn")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Saldo (DKK)")]
    [Range(0, double.MaxValue, ErrorMessage = "Saldo skal være positiv")]
    public decimal Balance { get; set; }

    [Required(ErrorMessage = "Ansvarlig navn er påkrævet")]
    [Display(Name = "Ansvarlig navn")]
    public string ResponsibleName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ansvarlig e-mail er påkrævet")]
    [EmailAddress(ErrorMessage = "Ugyldig e-mail")]
    [Display(Name = "Ansvarlig e-mail")]
    public string ResponsibleEmail { get; set; } = string.Empty;
}
