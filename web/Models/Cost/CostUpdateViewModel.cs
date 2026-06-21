using System.ComponentModel.DataAnnotations;

namespace web.Models.Cost;

public sealed class CostUpdateViewModel
{
    [Required]
    [Range(0.001, double.MaxValue, ErrorMessage = "Prisen skal være positiv")]
    [Display(Name = "SMS pris (DKK)")]
    public decimal SmsPriceDkk { get; set; }
}
