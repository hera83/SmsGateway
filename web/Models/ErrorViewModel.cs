namespace web.Models;

public class ErrorViewModel
{
    public int StatusCode { get; set; } = 500;
    public string Title { get; set; } = "Der opstod en fejl";
    public string Description { get; set; } = "Noget gik galt. Prøv igen eller kontakt administratoren.";
    public string? RequestId { get; set; }
    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
}
