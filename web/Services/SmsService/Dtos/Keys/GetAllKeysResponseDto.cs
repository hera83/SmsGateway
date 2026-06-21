namespace web.Services.SmsService.Dtos.Keys;

public sealed class GetAllKeysResponseDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public decimal Balance { get; set; }
    public string ResponsibleName { get; set; } = string.Empty;
    public string ResponsibleEmail { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
